// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus.Internal;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Security;
using System.Security.Authentication;
using Gocgun.Bus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using V1 = Gocgun.Bus.V1;

/// <summary>
/// RabbitMQ transport. One publisher connection, one or more consumer connections, confirm channels pooled.
/// </summary>
internal sealed class RabbitMqMessageBus : IMessageBus, IHostedService, IAsyncDisposable
{
    /// <summary>Gets the AMQP content type for an envelope body.</summary>
    internal const string ContentType = "application/x-protobuf";

    /// <summary>Gets the dead-letter reason header name.</summary>
    internal const string ReasonHeader = "gocgun-dead-letter-reason";

    /// <summary>Gets the exception type header name. The exception message is not copied.</summary>
    internal const string ExceptionHeader = "gocgun-exception-type";

    private readonly IServiceProvider services;
    private readonly string name;
    private readonly IReadOnlyList<IHandlerBinding> bindings;
    private readonly BusOptions busOptions;
    private readonly RabbitMqOptions options;
    private readonly TimeProvider clock;
    private readonly ILogger<RabbitMqMessageBus> logger;
    private readonly List<IConnection> consumerConnections = new();
    private readonly List<ConsumerSlot> consumers = new();
    private readonly ConcurrentQueue<IChannel> publishChannels = new();
    private readonly SemaphoreSlim publishSlots = new(0, int.MaxValue);
    private readonly CancellationTokenSource stopping = new();
    private IConnection? publisher;
    private int publisherBlocked;
    private int consumerBlocked;
    private int accepting;
    private int inFlight;
    private int disposed;
    private int stopped;

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMqMessageBus"/> class.
    /// </summary>
    /// <param name="services">Root provider.</param>
    /// <param name="name">Bus name. Publish filters are keyed by it, and it is part of the connection name.</param>
    /// <param name="bindings">Handlers registered on this bus.</param>
    /// <param name="busOptions">Shared limits.</param>
    /// <param name="options">Broker settings.</param>
    /// <param name="clock">Clock.</param>
    /// <param name="logger">Logger.</param>
    public RabbitMqMessageBus(
        IServiceProvider services,
        string name,
        IReadOnlyList<IHandlerBinding> bindings,
        BusOptions busOptions,
        RabbitMqOptions options,
        TimeProvider clock,
        ILogger<RabbitMqMessageBus> logger)
    {
        this.services = services;
        this.name = name;
        this.bindings = bindings;
        this.busOptions = busOptions;
        this.options = options;
        this.clock = clock;
        this.logger = logger;
    }

    /// <summary>
    /// Gets a value indicating whether both connection roles are open and not blocked.
    /// </summary>
    internal bool ConnectionsHealthy
    {
        get
        {
            var publisherConnection = this.publisher;
            return publisherConnection is { IsOpen: true }
                && this.publisherBlocked == 0
                && this.consumerConnections.Count > 0
                && this.consumerConnections.TrueForAll(connection => connection.IsOpen)
                && this.consumerBlocked == 0;
        }
    }

    /// <inheritdoc />
    public ValueTask PublishAsync<TEvent>(
        TEvent message,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default)
        where TEvent : class, IIntegrationEvent
    {
        return this.PublishCoreAsync(message, isCommand: false, options?.MessageId, options?.CorrelationId, options?.Headers, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask SendAsync<TCommand>(
        TCommand message,
        SendOptions? options = null,
        CancellationToken cancellationToken = default)
        where TCommand : class, IIntegrationCommand
    {
        return this.PublishCoreAsync(message, isCommand: true, options?.MessageId, options?.CorrelationId, options?.Headers, cancellationToken);
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var instance = Environment.MachineName + "-" + Environment.ProcessId;
        this.publisher = await this.OpenAsync("publisher", instance, cancellationToken).ConfigureAwait(false);
        this.HookBlocked(this.publisher, publisher: true);
        var consumerCount = this.options.ConsumerConnectionCount;
        for (var index = 0; index < consumerCount; index++)
        {
            var connection = await this.OpenAsync("consumer-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture), instance, cancellationToken).ConfigureAwait(false);
            this.HookBlocked(connection, publisher: false);
            this.consumerConnections.Add(connection);
        }

        var needed = this.bindings.Count + this.options.PublisherChannelPoolSize + 1;
        if (this.publisher.ChannelMax != 0 && needed >= this.publisher.ChannelMax)
        {
            throw new InvalidOperationException("Handler count plus the publisher pool exceeds the broker channel max.");
        }

        await this.DeclareTopologyAsync(cancellationToken).ConfigureAwait(false);
        for (var index = 0; index < this.options.PublisherChannelPoolSize; index++)
        {
            this.publishChannels.Enqueue(await this.OpenPublishChannelAsync(cancellationToken).ConfigureAwait(false));
            this.publishSlots.Release();
        }

        for (var index = 0; index < this.bindings.Count; index++)
        {
            var binding = this.bindings[index];
            var connection = this.consumerConnections[index % this.consumerConnections.Count];
            var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            await channel.BasicQosAsync(0, (ushort)this.options.PrefetchCount, false, cancellationToken).ConfigureAwait(false);
            var slot = new ConsumerSlot(binding, channel, new SemaphoreSlim(1, 1));
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += (_, args) => this.OnReceivedAsync(slot, args);
            var tag = await channel.BasicConsumeAsync(EnvelopeFactory.QueueName(this.options, binding), autoAck: false, consumer, cancellationToken).ConfigureAwait(false);
            slot.ConsumerTag = tag;
            this.consumers.Add(slot);
        }

        Volatile.Write(ref this.accepting, 1);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Volatile.Write(ref this.stopped, 1);
        Volatile.Write(ref this.accepting, 0);
        foreach (var slot in this.consumers)
        {
            try
            {
                await slot.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (!string.IsNullOrEmpty(slot.ConsumerTag))
                    {
                        await slot.Channel.BasicCancelAsync(slot.ConsumerTag, false, cancellationToken).ConfigureAwait(false);
                    }
                }
                finally
                {
                    slot.Gate.Release();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        if (!await this.WaitForInFlightAsync().ConfigureAwait(false))
        {
            // Handlers outlived the grace period: ask them to stop, then drain again before channels go away.
            await this.stopping.CancelAsync().ConfigureAwait(false);
            await this.WaitForInFlightAsync().ConfigureAwait(false);
        }

        await this.DisposeConnectionsAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref this.disposed, 1) != 0)
        {
            return;
        }

        if (Volatile.Read(ref this.stopped) == 0)
        {
            await this.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }

        this.publishSlots.Dispose();
        this.stopping.Dispose();
    }

    private async Task<bool> WaitForInFlightAsync()
    {
        var drain = Task.Run(
            async () =>
            {
                while (Volatile.Read(ref this.inFlight) > 0)
                {
                    await Task.Delay(20, CancellationToken.None).ConfigureAwait(false);
                }
            },
            CancellationToken.None);
        return await Task.WhenAny(drain, Task.Delay(this.options.ShutdownTimeout, CancellationToken.None)).ConfigureAwait(false) == drain;
    }

    private async ValueTask PublishCoreAsync<TMessage>(
        TMessage message,
        bool isCommand,
        Guid? messageId,
        string? correlationId,
        IReadOnlyDictionary<string, string>? headers,
        CancellationToken cancellationToken)
        where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(message);
        if (Volatile.Read(ref this.accepting) == 0)
        {
            throw new InvalidOperationException("The RabbitMQ bus is not accepting publishes.");
        }

        HeaderRules.ThrowIfUnsafe(headers, this.busOptions);
        var payload = ProtoCodec.Serialize(message);
        if (payload.Length > this.busOptions.MaxPayloadBytes)
        {
            throw new ArgumentException("Payload exceeds MaxPayloadBytes.", nameof(message));
        }

        var scope = this.services.CreateAsyncScope();
        try
        {
        var filters = scope.ServiceProvider.GetKeyedServices<IPublishFilter>(this.name).ToArray();
        var created = await EnvelopeFactory
            .CreateAsync(message, payload, messageId, correlationId, headers, filters, this.clock, cancellationToken)
            .ConfigureAwait(false);
        var body = created.Body;
        if (body.Length > this.busOptions.MaxPayloadBytes)
        {
            throw new ArgumentException("Payload exceeds MaxPayloadBytes.", nameof(message));
        }

        using var activity = BusActivitySource.Source.StartActivity("Gocgun.Bus.Publish", ActivityKind.Producer);
        var typeUrl = created.Envelope.Payload?.TypeUrl ?? string.Empty;
        var wire = EnvelopeFactory.WireName(typeUrl);
        if (isCommand)
        {
            var targets = this.bindings.Where(binding => binding.IsCommand && string.Equals(binding.TypeUrl, typeUrl, StringComparison.Ordinal)).ToArray();
            if (targets.Length == 0)
            {
                throw new InvalidOperationException("No handler is registered for this command.");
            }

            foreach (var target in targets)
            {
                await this.ConfirmPublishAsync(string.Empty, EnvelopeFactory.QueueName(this.options, target), created, body, true, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        await this.ConfirmPublishAsync(this.options.Exchange, wire, created, body, false, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task ConfirmPublishAsync(
        string exchange,
        string routingKey,
        (V1.Envelope Envelope, byte[] Body, string? PublisherId) created,
        byte[] body,
        bool isCommand,
        CancellationToken cancellationToken)
    {
        var properties = new BasicProperties
        {
            ContentType = ContentType,
            DeliveryMode = DeliveryModes.Persistent,
            MessageId = created.Envelope.MessageId,
            CorrelationId = created.Envelope.CorrelationId,
            Type = EnvelopeFactory.WireName(created.Envelope.Payload?.TypeUrl ?? string.Empty),
            UserId = string.IsNullOrEmpty(created.PublisherId) ? null : created.PublisherId,
        };
        var returned = await this.ConfirmPublishCoreAsync(exchange, routingKey, properties, body, cancellationToken).ConfigureAwait(false);
        if (!returned)
        {
            return;
        }

        if (isCommand)
        {
            throw new InvalidOperationException("The broker could not route the command to any queue.");
        }

        BusLog.Unroutable(this.logger, created.Envelope.MessageId, routingKey);
    }

    /// <summary>
    /// Publishes with mandatory routing on a pooled confirm channel and waits for the broker confirm.
    /// </summary>
    /// <returns>True when the broker returned the message as unroutable.</returns>
    private async Task<bool> ConfirmPublishCoreAsync(
        string exchange,
        string routingKey,
        BasicProperties properties,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken)
    {
        if (!await this.publishSlots.WaitAsync(this.options.PublishConfirmTimeout, cancellationToken).ConfigureAwait(false))
        {
            throw new TimeoutException("Publisher channel pool wait timed out.");
        }

        IChannel? channel;
        try
        {
            if (!this.publishChannels.TryDequeue(out channel))
            {
                // The pool shrank after a fault while the connection was down. Slots stay constant, so refill lazily.
                if (this.publisher is not { IsOpen: true })
                {
                    throw new InvalidOperationException("The RabbitMQ publisher connection is not open.");
                }

                channel = await this.OpenPublishChannelAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            this.publishSlots.Release();
            throw;
        }

        var faulted = false;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(this.options.PublishConfirmTimeout);
            await channel.BasicPublishAsync(exchange, routingKey, mandatory: true, properties, body, timeout.Token).ConfigureAwait(false);
            return false;
        }
        catch (PublishReturnException)
        {
            // The broker confirmed but returned the message: no queue matched. The channel stays healthy.
            return true;
        }
        catch
        {
            faulted = true;
            throw;
        }
        finally
        {
            await this.ReturnChannelAsync(channel, faulted).ConfigureAwait(false);
        }
    }

    private async Task ReturnChannelAsync(IChannel channel, bool faulted)
    {
        try
        {
            if (faulted || !channel.IsOpen)
            {
                await channel.DisposeAsync().ConfigureAwait(false);
                try
                {
                    if (this.publisher is { IsOpen: true })
                    {
                        channel = await this.OpenPublishChannelAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    else
                    {
                        return;
                    }
                }
                catch (Exception)
                {
                    // Never mask the publish outcome. The next publish refills the pool lazily.
                    return;
                }
            }

            this.publishChannels.Enqueue(channel);
        }
        finally
        {
            this.publishSlots.Release();
        }
    }

    private async Task OnReceivedAsync(ConsumerSlot slot, BasicDeliverEventArgs args)
    {
        if (Volatile.Read(ref this.accepting) == 0)
        {
            // Shutting down: leave the delivery unacked so the broker redelivers it.
            return;
        }

        Interlocked.Increment(ref this.inFlight);
        try
        {
            var stoppingToken = this.stopping.Token;
            var publisherId = string.IsNullOrEmpty(args.BasicProperties.UserId) ? null : args.BasicProperties.UserId;
            var body = args.Body.ToArray();
            DeliveryResult result;
            try
            {
                result = await DeliveryExecutor
                    .ExecuteAsync(body, slot.Binding, publisherId, this.services, this.busOptions, this.logger, stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                result = new DeliveryResult(true, "retries-exhausted", ex.GetType().FullName, null, null);
            }

            await this.CompleteAsync(slot, args, result).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref this.inFlight);
        }
    }

    private async Task CompleteAsync(ConsumerSlot slot, BasicDeliverEventArgs args, DeliveryResult result)
    {
        try
        {
            await slot.Gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            if (!result.DeadLetter)
            {
                await slot.Channel.BasicAckAsync(args.DeliveryTag, false, CancellationToken.None).ConfigureAwait(false);
                return;
            }

            await this.DeadLetterAsync(slot, args, result).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or AlreadyClosedException)
        {
            // Channel is gone; the broker redelivers.
        }
        finally
        {
            slot.Gate.Release();
        }
    }

    private async Task DeadLetterAsync(ConsumerSlot slot, BasicDeliverEventArgs args, DeliveryResult result)
    {
        var dlq = EnvelopeFactory.QueueName(this.options, slot.Binding) + ".dlq";
        try
        {
            var properties = new BasicProperties
            {
                ContentType = args.BasicProperties.ContentType,
                DeliveryMode = DeliveryModes.Persistent,
                MessageId = args.BasicProperties.MessageId,
                CorrelationId = args.BasicProperties.CorrelationId,
                Headers = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [ReasonHeader] = result.Reason,
                    [ExceptionHeader] = result.ExceptionType ?? string.Empty,
                },
            };

            // The consumer channel has no publisher confirms, so move the copy on a confirm channel and ack only after the broker confirmed it.
            var returned = await this.ConfirmPublishCoreAsync(string.Empty, dlq, properties, args.Body, CancellationToken.None).ConfigureAwait(false);
            if (returned)
            {
                throw new InvalidOperationException("The broker returned the dead-letter copy as unroutable.");
            }

            await slot.Channel.BasicAckAsync(args.DeliveryTag, false, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not (ObjectDisposedException or AlreadyClosedException))
        {
            // Copy not confirmed: nack without requeue so the queue's DLX moves the original atomically.
            try
            {
                await slot.Channel.BasicNackAsync(args.DeliveryTag, false, false, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception nackEx) when (nackEx is ObjectDisposedException or AlreadyClosedException)
            {
                // Channel closed; the broker redelivers.
            }
        }
    }

    private async Task DeclareTopologyAsync(CancellationToken cancellationToken)
    {
        var channel = await this.publisher!.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        await using (channel.ConfigureAwait(false))
        {
            var dlx = this.options.Exchange + ".dlx";
            await channel.ExchangeDeclareAsync(this.options.Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: cancellationToken).ConfigureAwait(false);
            await channel.ExchangeDeclareAsync(dlx, ExchangeType.Direct, durable: true, autoDelete: false, cancellationToken: cancellationToken).ConfigureAwait(false);
            foreach (var binding in this.bindings)
            {
                var queue = EnvelopeFactory.QueueName(this.options, binding);
                var dlq = queue + ".dlq";
                var queueArgs = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["x-queue-type"] = "quorum",
                    ["x-delivery-limit"] = this.options.DeliveryLimit,
                    ["x-dead-letter-exchange"] = dlx,
                    ["x-dead-letter-routing-key"] = dlq,
                };
                await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, arguments: queueArgs, cancellationToken: cancellationToken).ConfigureAwait(false);
                var dlqArgs = new Dictionary<string, object?>(StringComparer.Ordinal) { ["x-queue-type"] = "quorum" };
                await channel.QueueDeclareAsync(dlq, durable: true, exclusive: false, autoDelete: false, arguments: dlqArgs, cancellationToken: cancellationToken).ConfigureAwait(false);
                await channel.QueueBindAsync(dlq, dlx, dlq, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (!binding.IsCommand)
                {
                    await channel.QueueBindAsync(queue, this.options.Exchange, binding.WireName, cancellationToken: cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    private async Task<IChannel> OpenPublishChannelAsync(CancellationToken cancellationToken)
    {
        var created = await this.publisher!.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken).ConfigureAwait(false);
        return created;
    }

    private Task<IConnection> OpenAsync(string role, string instance, CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = this.options.HostName,
            Port = this.options.Port,
            VirtualHost = this.options.VirtualHost,
            UserName = this.options.UserName,
            Password = this.options.Password,
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,
            ClientProvidedName = this.ConnectionName(role, instance),
        };
        if (this.options.UseTls)
        {
            factory.Ssl.Enabled = true;
            factory.Ssl.ServerName = this.options.HostName;
            factory.Ssl.Version = SslProtocols.Tls12 | SslProtocols.Tls13;
            factory.Ssl.AcceptablePolicyErrors = SslPolicyErrors.None;
            factory.Port = this.options.Port == 5672 ? 5671 : this.options.Port;
        }

        return factory.CreateConnectionAsync(cancellationToken);
    }

    private string ConnectionName(string role, string instance)
    {
        // The default bus keeps the original connection name, so existing dashboards still match.
        var bus = string.Equals(this.name, BusNames.Default, StringComparison.Ordinal) ? string.Empty : "." + this.name;
        return this.options.ConnectionNamePrefix + "." + this.options.ServiceName + bus + "." + instance + "." + role;
    }

    private void HookBlocked(IConnection connection, bool publisher)
    {
        connection.ConnectionBlockedAsync += (_, _) =>
        {
            if (publisher)
            {
                Interlocked.Exchange(ref this.publisherBlocked, 1);
            }
            else
            {
                Interlocked.Exchange(ref this.consumerBlocked, 1);
            }

            return Task.CompletedTask;
        };
        connection.ConnectionUnblockedAsync += (_, _) =>
        {
            if (publisher)
            {
                Interlocked.Exchange(ref this.publisherBlocked, 0);
            }
            else
            {
                Interlocked.Exchange(ref this.consumerBlocked, 0);
            }

            return Task.CompletedTask;
        };
    }

    private async Task DisposeConnectionsAsync()
    {
        foreach (var slot in this.consumers)
        {
            await slot.Channel.DisposeAsync().ConfigureAwait(false);
            slot.Gate.Dispose();
        }

        this.consumers.Clear();
        while (this.publishChannels.TryDequeue(out var channel))
        {
            await channel.DisposeAsync().ConfigureAwait(false);
        }

        foreach (var connection in this.consumerConnections)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }

        this.consumerConnections.Clear();
        if (this.publisher is not null)
        {
            await this.publisher.DisposeAsync().ConfigureAwait(false);
            this.publisher = null;
        }
    }

    private sealed class ConsumerSlot
    {
        public ConsumerSlot(IHandlerBinding binding, IChannel channel, SemaphoreSlim gate)
        {
            this.Binding = binding;
            this.Channel = channel;
            this.Gate = gate;
        }

        public IHandlerBinding Binding { get; }

        public IChannel Channel { get; }

        public SemaphoreSlim Gate { get; }

        public string? ConsumerTag { get; set; }
    }
}
