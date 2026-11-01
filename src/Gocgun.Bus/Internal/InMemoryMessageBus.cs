// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus.Internal;

using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using V1 = Gocgun.Bus.V1;

/// <summary>
/// Bounded-channel transport. Not durable.
/// </summary>
internal sealed class InMemoryMessageBus : IMessageBus, IInMemoryDeadLetters, IHostedService, IAsyncDisposable
{
    private readonly IServiceProvider services;
    private readonly string name;
    private readonly IReadOnlyList<IHandlerBinding> bindings;
    private readonly BusOptions busOptions;
    private readonly InMemoryBusOptions memoryOptions;
    private readonly TimeProvider clock;
    private readonly ILogger<InMemoryMessageBus> logger;
    private readonly ConcurrentDictionary<string, Channel<QueuedDelivery>> channels = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<DeadLetter> dead = new();
    private readonly List<Task> readers = new();
    private CancellationTokenSource? stop;
    private int lost;
    private int disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryMessageBus"/> class.
    /// </summary>
    /// <param name="services">Root provider.</param>
    /// <param name="name">Bus name. Publish filters are keyed by it.</param>
    /// <param name="bindings">Handlers registered on this bus.</param>
    /// <param name="busOptions">Shared limits.</param>
    /// <param name="memoryOptions">Channel limits.</param>
    /// <param name="clock">Clock for sent time.</param>
    /// <param name="logger">Logger.</param>
    public InMemoryMessageBus(
        IServiceProvider services,
        string name,
        IReadOnlyList<IHandlerBinding> bindings,
        BusOptions busOptions,
        InMemoryBusOptions memoryOptions,
        TimeProvider clock,
        ILogger<InMemoryMessageBus> logger)
    {
        this.services = services;
        this.name = name;
        this.bindings = bindings;
        this.busOptions = busOptions;
        this.memoryOptions = memoryOptions;
        this.clock = clock;
        this.logger = logger;
        foreach (var binding in bindings)
        {
            var channel = Channel.CreateBounded<QueuedDelivery>(new BoundedChannelOptions(memoryOptions.Capacity)
            {
                FullMode = memoryOptions.FullMode,
                SingleReader = busOptions.MaxConcurrency <= 1,
                SingleWriter = false,
            });
            this.channels[binding.ChannelKey] = channel;
        }
    }

    /// <summary>
    /// Gets how many in-flight or queued deliveries were dropped on shutdown.
    /// </summary>
    internal int LostCount => Volatile.Read(ref this.lost);

    /// <inheritdoc />
    public IReadOnlyList<DeadLetter> Snapshot()
    {
        return this.dead.ToArray();
    }

    /// <inheritdoc />
    public ValueTask PublishAsync<TEvent>(
        TEvent message,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default)
        where TEvent : class, IIntegrationEvent
    {
        return this.EnqueueAsync(message, isCommand: false, options?.MessageId, options?.CorrelationId, options?.Headers, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask SendAsync<TCommand>(
        TCommand message,
        SendOptions? options = null,
        CancellationToken cancellationToken = default)
        where TCommand : class, IIntegrationCommand
    {
        return this.EnqueueAsync(message, isCommand: true, options?.MessageId, options?.CorrelationId, options?.Headers, cancellationToken);
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        this.stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = this.stop.Token;
        foreach (var binding in this.bindings)
        {
            var channel = this.channels[binding.ChannelKey];
            var loops = this.busOptions.MaxConcurrency < 1 ? 1 : this.busOptions.MaxConcurrency;
            for (var index = 0; index < loops; index++)
            {
                this.readers.Add(Task.Run(() => this.ReadAsync(binding, channel, token), CancellationToken.None));
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var channel in this.channels.Values)
        {
            channel.Writer.TryComplete();
        }

        if (this.readers.Count == 0)
        {
            return;
        }

        var all = Task.WhenAll(this.readers);
        var finished = await Task.WhenAny(all, Task.Delay(this.memoryOptions.DrainTimeout, cancellationToken)).ConfigureAwait(false);
        if (!ReferenceEquals(finished, all))
        {
            if (this.stop is not null)
            {
                await this.stop.CancelAsync().ConfigureAwait(false);
            }

            foreach (var channel in this.channels.Values)
            {
                while (channel.Reader.TryRead(out var leftover))
                {
                    Interlocked.Increment(ref this.lost);
                    this.Remember(leftover.Body, "shutdown", exceptionType: null);
                }
            }

            await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None)).ConfigureAwait(false);
        }

        if (this.lost > 0)
        {
            BusLog.ShutdownLost(this.logger, this.lost);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref this.disposed, 1) != 0)
        {
            return;
        }

        await this.StopAsync(CancellationToken.None).ConfigureAwait(false);
        this.stop?.Dispose();
    }

    /// <summary>
    /// Feeds raw envelope bytes through the consume rules.
    /// </summary>
    /// <param name="body">Envelope bytes.</param>
    /// <param name="publisherId">Transport publisher id. Headers cannot replace it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the body is acked or dead-lettered.</returns>
    internal async ValueTask InjectAsync(byte[] body, string? publisherId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        var typeUrl = EnvelopeFactory.TryTypeUrl(body);
        var matches = this.bindings.Where(binding => string.Equals(binding.TypeUrl, typeUrl, StringComparison.Ordinal)).ToArray();
        if (matches.Length == 0)
        {
            await this.ConsumeOneAsync(body, binding: null, publisherId, cancellationToken).ConfigureAwait(false);
            return;
        }

        foreach (var match in matches)
        {
            await this.ConsumeOneAsync(body, match, publisherId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask EnqueueAsync<TMessage>(
        TMessage message,
        bool isCommand,
        Guid? messageId,
        string? correlationId,
        IReadOnlyDictionary<string, string>? headers,
        CancellationToken cancellationToken)
        where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(message);
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

            var targets = this.bindings.Where(binding => binding.IsCommand == isCommand && string.Equals(binding.TypeUrl, created.Envelope.Payload?.TypeUrl, StringComparison.Ordinal)).ToArray();
            if (targets.Length == 0)
            {
                this.Remember(body, "unknown-type", exceptionType: null);
                return;
            }

            var queued = new QueuedDelivery(body, created.PublisherId);
            foreach (var target in targets)
            {
                try
                {
                    await this.channels[target.ChannelKey].Writer.WriteAsync(queued, cancellationToken).ConfigureAwait(false);
                }
                catch (ChannelClosedException ex)
                {
                    throw new InvalidOperationException("The in-memory bus is stopped.", ex);
                }
            }
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task ReadAsync(IHandlerBinding binding, Channel<QueuedDelivery> channel, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var delivery in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await this.ConsumeOneAsync(delivery.Body, binding, delivery.PublisherId, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    Interlocked.Increment(ref this.lost);
                    this.Remember(delivery.Body, "shutdown", exceptionType: null);
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown cancelled the reader.
        }
    }

    private async ValueTask ConsumeOneAsync(
        byte[] body,
        IHandlerBinding? binding,
        string? publisherId,
        CancellationToken cancellationToken)
    {
        var result = await DeliveryExecutor
            .ExecuteAsync(body, binding, publisherId, this.services, this.busOptions, this.logger, cancellationToken)
            .ConfigureAwait(false);
        if (result.DeadLetter)
        {
            this.Remember(body, result.Reason, result.ExceptionType);
        }
    }

    private void Remember(byte[] body, string reason, string? exceptionType)
    {
        string? typeUrl = null;
        Guid? messageId = null;

        // A body that does not parse keeps only the raw bytes. The reason already says why it failed.
        if (ProtoCodec.TryDeserialize<V1.Envelope>(body, out var envelope) && envelope is not null)
        {
            typeUrl = envelope.Payload?.TypeUrl;
            if (Guid.TryParse(envelope.MessageId, out var parsed))
            {
                messageId = parsed;
            }
        }

        this.dead.Enqueue(new DeadLetter(body, reason, typeUrl, messageId, exceptionType));
    }

    private readonly record struct QueuedDelivery(byte[] Body, string? PublisherId);
}
