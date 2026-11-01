// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus.Tests;

using System.Text;
using Gocgun.Bus.Internal;
using Gocgun.Bus.Tests.Messages;
using Gocgun.Bus.Tests.Support;
using Gocgun.Bus.V1;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;

/// <summary>
/// Broker integration. The container user is passed explicitly; the package has no guest default.
/// </summary>
[Collection(RabbitCollection.Name)]
public sealed class RabbitIntegrationTests
{
    private readonly RabbitFixture fixture;

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitIntegrationTests"/> class.
    /// </summary>
    /// <param name="fixture">Running broker.</param>
    public RabbitIntegrationTests(RabbitFixture fixture)
    {
        this.fixture = fixture;
    }

    /// <summary>
    /// A confirmed publish is handled with the broker user id.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task PublishIsConfirmedAndHandled()
    {
        var filter = new PublisherFilter { PublisherId = "guest" };
        using var host = await this.StartHostAsync(filter, idempotent: false);
        var bus = host.Services.GetRequiredService<IMessageBus>();
        await bus.PublishAsync(new Ping { Text = "broker" }, new PublishOptions(Guid.CreateVersion7(), "c-1"), TestContext.Current.CancellationToken);
        var log = host.Services.GetRequiredService<CallLog>();
        await log.First.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
        var call = Assert.Single(log.Snapshot());
        Assert.Equal("broker", call.Text);
        Assert.Equal("guest", call.Context.PublisherId);
        Assert.Equal("c-1", call.Context.CorrelationId);
        var health = host.Services.GetRequiredKeyedService<RabbitMqHealthCheck>(BusNames.Default);
        var status = await health.CheckHealthAsync(new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext(), TestContext.Current.CancellationToken);
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy, status.Status);
    }

    /// <summary>
    /// The broker rejects a user id that is not the connected user.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task MismatchedPublisherIdIsRejected()
    {
        var filter = new PublisherFilter { PublisherId = "attacker" };
        using var host = await this.StartHostAsync(filter, idempotent: false);
        var bus = host.Services.GetRequiredService<IMessageBus>();
        await Assert.ThrowsAnyAsync<Exception>(async () => await bus.PublishAsync(new Ping { Text = "nope" }, cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false));
        Assert.Empty(host.Services.GetRequiredService<CallLog>().Snapshot());
    }

    /// <summary>
    /// A second delivery of the same id does not run the handler again.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task RedeliveryWithSameIdIsSkipped()
    {
        using var host = await this.StartHostAsync(filter: null, idempotent: true);
        var id = Guid.CreateVersion7();
        var bus = host.Services.GetRequiredService<IMessageBus>();
        var options = new PublishOptions(id);
        await bus.PublishAsync(new Ping { Text = "once" }, options, TestContext.Current.CancellationToken);
        var log = host.Services.GetRequiredService<CallLog>();
        await log.First.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
        await bus.PublishAsync(new Ping { Text = "once" }, options, TestContext.Current.CancellationToken);
        await Task.Delay(500, TestContext.Current.CancellationToken);
        Assert.Single(log.Snapshot());
    }

    /// <summary>
    /// An unknown type and a forged payload are dead-lettered with a reason header and no handler call.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task UnknownAndForgedBodiesGoToDeadLetter()
    {
        using var host = await this.StartHostAsync(filter: null, idempotent: false);
        var options = host.Services.GetRequiredKeyedService<RabbitMqOptions>(BusNames.Default);
        var bindings = host.Services.GetRequiredKeyedService<IReadOnlyList<IHandlerBinding>>(BusNames.Default);
        var queue = EnvelopeFactory.QueueName(options, bindings[0]);
        var unknown = ProtoCodec.Serialize(new Envelope
        {
            MessageId = Guid.CreateVersion7().ToString("D"),
            Payload = new EnvelopePayload
            {
                TypeUrl = "type.googleapis.com/gocgun.bus.tests.v1.Nope",
                Value = [],
            },
        });
        var forged = ProtoCodec.Serialize(new Envelope
        {
            MessageId = Guid.CreateVersion7().ToString("D"),
            Payload = new EnvelopePayload
            {
                TypeUrl = MapMessageType.TypeUrl<Ping>(),
                Value = "not-protobuf"u8.ToArray(),
            },
        });
        await this.PublishRawAsync(queue, unknown);
        await this.PublishRawAsync(queue, forged);
        var reasons = await this.ReadDeadReasonsAsync(queue + ".dlq", 2);
        Assert.Contains("unknown-type", reasons, StringComparer.Ordinal);
        Assert.Contains("invalid-payload", reasons, StringComparer.Ordinal);
        Assert.Empty(host.Services.GetRequiredService<CallLog>().Snapshot());
    }

    /// <summary>
    /// An event with no bound queue is returned by the broker and logged, not silently confirmed.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task UnroutableEventIsLogged()
    {
        var logs = new CollectingLoggerProvider();
        using var host = await this.StartHostAsync(filter: null, idempotent: false, withPing: false, withPong: true, logs: logs, exchange: "isolated." + Guid.NewGuid().ToString("N"));
        await host.Services.GetRequiredService<IMessageBus>().PublishAsync(new Ping { Text = "nobody" }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(logs.Lines, line => line.Contains("unroutable", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A command whose queue vanished fails the send instead of being confirmed and dropped.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task UnroutableCommandThrows()
    {
        using var host = await this.StartHostAsync(filter: null, idempotent: false, withPing: false, withPong: true);
        var options = host.Services.GetRequiredKeyedService<RabbitMqOptions>(BusNames.Default);
        var bindings = host.Services.GetRequiredKeyedService<IReadOnlyList<IHandlerBinding>>(BusNames.Default);
        var queue = EnvelopeFactory.QueueName(options, bindings[0]);
        var factory = this.Factory();
        var connection = await factory.CreateConnectionAsync(TestContext.Current.CancellationToken);
        await using (connection.ConfigureAwait(false))
        {
            var channel = await connection.CreateChannelAsync(cancellationToken: TestContext.Current.CancellationToken);
            await using (channel.ConfigureAwait(false))
            {
                await channel.QueueDeleteAsync(queue, cancellationToken: TestContext.Current.CancellationToken);
            }
        }

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await host.Services.GetRequiredService<IMessageBus>().SendAsync(new Pong(), cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Faulted publisher channels are replaced, so the pool never runs dry.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task PoolSurvivesMoreFaultsThanChannels()
    {
        var filter = new PublisherFilter { PublisherId = "attacker" };
        using var host = await this.StartHostAsync(filter, idempotent: false);
        var options = host.Services.GetRequiredKeyedService<RabbitMqOptions>(BusNames.Default);
        var bus = host.Services.GetRequiredService<IMessageBus>();
        for (var attempt = 0; attempt < options.PublisherChannelPoolSize + 2; attempt++)
        {
            await Assert.ThrowsAnyAsync<Exception>(async () => await bus.PublishAsync(new Ping { Text = "bad" }, cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false));
        }

        filter.PublisherId = "guest";
        await bus.PublishAsync(new Ping { Text = "ok" }, cancellationToken: TestContext.Current.CancellationToken);
        var log = host.Services.GetRequiredService<CallLog>();
        await log.First.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Two RabbitMQ buses and the in-memory bus run in one host; each publish reaches only its own bus's handler.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task TwoRabbitBusesAndInMemoryRouteSeparately()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<CallLog>();
        builder.Services.AddGocgunBus(bus => bus.UseInMemory().AddHandler<Pong, PongHandler>());
        builder.Services.AddGocgunBus("first", bus => bus
            .UseRabbitMq(options => this.Broker(options, "first" + suffix, "first." + suffix))
            .AddHandler<Ping, PingHandler>());
        builder.Services.AddGocgunBus("second", bus => bus
            .UseRabbitMq(options => this.Broker(options, "second" + suffix, "second." + suffix))
            .AddHandler<Ping, PingCopyHandler>());
        using var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        var log = host.Services.GetRequiredService<CallLog>();

        await host.Services.GetRequiredKeyedService<IMessageBus>("first").PublishAsync(new Ping { Text = "one" }, cancellationToken: TestContext.Current.CancellationToken);
        await host.Services.GetRequiredKeyedService<IMessageBus>("second").PublishAsync(new Ping { Text = "two" }, cancellationToken: TestContext.Current.CancellationToken);
        await host.Services.GetRequiredService<IMessageBus>().SendAsync(new Pong { Text = "three" }, cancellationToken: TestContext.Current.CancellationToken);
        var start = Environment.TickCount64;
        while (log.Snapshot().Count < 3 && Environment.TickCount64 - start < 20_000)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        await Task.Delay(300, TestContext.Current.CancellationToken);
        var calls = log.Snapshot();
        Assert.Equal(3, calls.Count);
        Assert.Contains(calls, call => string.Equals(call.Handler, nameof(PingHandler), StringComparison.Ordinal) && string.Equals(call.Text, "one", StringComparison.Ordinal));
        Assert.Contains(calls, call => string.Equals(call.Handler, nameof(PingCopyHandler), StringComparison.Ordinal) && string.Equals(call.Text, "two", StringComparison.Ordinal));
        Assert.Contains(calls, call => string.Equals(call.Text, "three", StringComparison.Ordinal));
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    private static string? Header(IDictionary<string, object?>? headers, string key)
    {
        if (headers is null || !headers.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        if (value is byte[] bytes)
        {
            return Encoding.UTF8.GetString(bytes);
        }

        return value.ToString();
    }

    private async Task<IHost> StartHostAsync(PublisherFilter? filter, bool idempotent, bool withPing = true, bool withPong = false, CollectingLoggerProvider? logs = null, string? exchange = null)
    {
        var service = "it" + Guid.NewGuid().ToString("N")[..10];
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        if (logs is not null)
        {
            builder.Logging.AddProvider(logs);
        }

        builder.Services.AddSingleton<CallLog>();
        if (filter is not null)
        {
            builder.Services.AddKeyedSingleton<IPublishFilter>(BusNames.Default, filter);
        }

        builder.Services.AddGocgunBus(bus =>
        {
            bus.UseRabbitMq(options =>
            {
                options.HostName = this.fixture.Container.Hostname;
                options.Port = this.fixture.Container.GetMappedPublicPort(5672);
                options.UserName = "guest";
                options.Password = "guest";
                options.ServiceName = service;
                options.VirtualHost = "/";
                if (exchange is not null)
                {
                    options.Exchange = exchange;
                }
            });
            if (withPing)
            {
                bus.AddHandler<Ping, PingHandler>();
            }

            if (withPong)
            {
                bus.AddHandler<Pong, PongHandler>();
            }

            if (idempotent)
            {
                bus.UseIdempotencyStore<MemoryIdempotencyStore>();
            }
        });
        var host = builder.Build();
        await host.StartAsync().ConfigureAwait(false);
        return host;
    }

    private void Broker(RabbitMqOptions options, string service, string exchange)
    {
        options.HostName = this.fixture.Container.Hostname;
        options.Port = this.fixture.Container.GetMappedPublicPort(5672);
        options.UserName = "guest";
        options.Password = "guest";
        options.ServiceName = service;
        options.Exchange = exchange;
    }

    private async Task PublishRawAsync(string queue, byte[] body)
    {
        var factory = this.Factory();
        var connection = await factory.CreateConnectionAsync().ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var channel = await connection.CreateChannelAsync().ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                await channel.BasicPublishAsync(string.Empty, queue, false, new BasicProperties { ContentType = RabbitMqMessageBus.ContentType }, body).ConfigureAwait(false);
            }
        }
    }

    private async Task<List<string>> ReadDeadReasonsAsync(string queue, int expected)
    {
        var found = new List<string>();
        var start = Environment.TickCount64;
        var factory = this.Factory();
        var connection = await factory.CreateConnectionAsync().ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var channel = await connection.CreateChannelAsync().ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                while (Environment.TickCount64 - start < 20000 && found.Count < expected)
                {
                    var message = await channel.BasicGetAsync(queue, autoAck: true).ConfigureAwait(false);
                    if (message is null)
                    {
                        await Task.Delay(100).ConfigureAwait(false);
                        continue;
                    }

                    found.Add(Header(message.BasicProperties.Headers, RabbitMqMessageBus.ReasonHeader) ?? string.Empty);
                }
            }
        }

        return found;
    }

    private ConnectionFactory Factory()
    {
        return new ConnectionFactory
        {
            HostName = this.fixture.Container.Hostname,
            Port = this.fixture.Container.GetMappedPublicPort(5672),
            UserName = "guest",
            Password = "guest",
            VirtualHost = "/",
        };
    }
}
