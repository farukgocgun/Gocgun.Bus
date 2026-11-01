// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus.Tests;

using Gocgun.Bus.Internal;
using Gocgun.Bus.Tests.Messages;
using Gocgun.Bus.Tests.Support;
using Gocgun.Bus.V1;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// In-memory transport behavior.
/// </summary>
public sealed class InMemoryBusTests
{
    /// <summary>
    /// Publish delivers the payload, ids, headers, and clock time.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task PublishDeliversPayloadAndMetadata()
    {
        var filter = new PublisherFilter { PublisherId = "core" };
        using var host = Build(
            bus =>
            {
                bus.UseInMemory();
                bus.AddHandler<Ping, PingHandler>();
            },
            filter);
        await host.StartAsync(TestContext.Current.CancellationToken);
        var id = Guid.CreateVersion7();
        await host.Services.GetRequiredService<IMessageBus>().PublishAsync(
            new Ping { Text = "hello" },
            new PublishOptions(id, "corr-1", new Dictionary<string, string>(StringComparer.Ordinal) { ["source"] = "unit" }),
            TestContext.Current.CancellationToken);
        var call = await WaitForCallAsync(host);
        Assert.Equal("hello", call.Text);
        Assert.Equal(id, call.Context.MessageId);
        Assert.Equal("corr-1", call.Context.CorrelationId);
        Assert.Equal("unit", call.Context.Headers["source"]);
        Assert.Equal("core", call.Context.PublisherId);
        Assert.Equal(FixedClock.Instant, call.Context.SentAt);
        Assert.Equal(1, call.Context.DeliveryCount);
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Send reaches the command handler and not the event handler.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task SendReachesCommandHandler()
    {
        using var host = Build(bus =>
        {
            bus.UseInMemory();
            bus.AddHandler<Ping, PingHandler>();
            bus.AddHandler<Pong, PongHandler>();
        });
        await host.StartAsync(TestContext.Current.CancellationToken);
        await host.Services.GetRequiredService<IMessageBus>().SendAsync(new Pong { Text = "go" }, cancellationToken: TestContext.Current.CancellationToken);
        var call = await WaitForCallAsync(host);
        Assert.Equal(nameof(PongHandler), call.Handler);
        Assert.Equal("go", call.Text);
        Assert.DoesNotContain(host.Services.GetRequiredService<CallLog>().Snapshot(), item => string.Equals(item.Handler, nameof(PingHandler), StringComparison.Ordinal));
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// One event is copied to every registered handler.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task PublishFansOutToEachHandler()
    {
        using var host = Build(bus =>
        {
            bus.UseInMemory();
            bus.AddHandler<Ping, PingHandler>();
            bus.AddHandler<Ping, PingCopyHandler>();
        });
        await host.StartAsync(TestContext.Current.CancellationToken);
        await host.Services.GetRequiredService<IMessageBus>().PublishAsync(new Ping { Text = "both" }, cancellationToken: TestContext.Current.CancellationToken);
        var log = host.Services.GetRequiredService<CallLog>();
        var ready = await WaitUntilAsync(() => log.Snapshot().Count >= 2);
        Assert.True(ready);
        var names = log.Snapshot().Select(call => call.Handler).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal([nameof(PingCopyHandler), nameof(PingHandler)], names);
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A null message and unsafe headers fail at the caller.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task RejectsNullMessageAndInjectedHeaders()
    {
        using var host = Build(bus =>
        {
            bus.UseInMemory();
            bus.AddHandler<Ping, PingHandler>();
        });
        await host.StartAsync(TestContext.Current.CancellationToken);
        var bus = host.Services.GetRequiredService<IMessageBus>();
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await bus.PublishAsync<Ping>(null!, cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false));
        var injected = new Dictionary<string, string>(StringComparer.Ordinal) { ["x"] = "a\r\nb" };
        await Assert.ThrowsAsync<ArgumentException>(async () => await bus.PublishAsync(new Ping { Text = "x" }, new PublishOptions(Headers: injected), TestContext.Current.CancellationToken).ConfigureAwait(false));
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Registration rejects a second transport, lossy mode, and a message with no marker.
    /// </summary>
    [Fact]
    public void RegistrationRejectsUserMistakes()
    {
        Assert.Throws<InvalidOperationException>(() => Build(bus =>
        {
            bus.UseInMemory();
            bus.UseRabbitMq(_ => { });
        }));
        Assert.Throws<InvalidOperationException>(() => Build(bus =>
        {
            bus.UseInMemory(options => options.FullMode = System.Threading.Channels.BoundedChannelFullMode.DropOldest);
        }));
        Assert.Throws<InvalidOperationException>(() => Build(bus =>
        {
            bus.UseInMemory();
            bus.AddHandler<Neutral, NeutralHandler>();
        }));
        Assert.Throws<InvalidOperationException>(() => Build(bus =>
        {
            bus.UseRabbitMq(options => options.HostName = "localhost");
        }));
    }

    /// <summary>
    /// A full channel blocks the publisher until a reader takes a message.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task FullChannelBlocksPublisher()
    {
        var gate = new GateHandler();
        using var host = Build(
            bus =>
            {
                bus.UseInMemory(options => options.Capacity = 1);
                bus.AddHandler<Ping, GateHandler>();
            },
            gate: gate);
        await host.StartAsync(TestContext.Current.CancellationToken);
        var bus = host.Services.GetRequiredService<IMessageBus>();
        await bus.PublishAsync(new Ping { Text = "one" }, cancellationToken: TestContext.Current.CancellationToken);
        await gate.Entered.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await bus.PublishAsync(new Ping { Text = "two" }, cancellationToken: TestContext.Current.CancellationToken);
        var third = bus.PublishAsync(new Ping { Text = "three" }, cancellationToken: TestContext.Current.CancellationToken).AsTask();
        var winner = await Task.WhenAny(third, Task.Delay(400, TestContext.Current.CancellationToken));
        Assert.NotSame(third, winner);
        gate.Release();
        await third.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Failures retry and then succeed.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task RetriesThenHandles()
    {
        var flaky = new FlakyHandler { FailUntil = 2 };
        using var host = Build(
            bus =>
            {
                bus.Configure(options => options.MaxImmediateRetries = 3);
                bus.UseInMemory();
                bus.AddHandler<Ping, FlakyHandler>();
            },
            flaky: flaky);
        await host.StartAsync(TestContext.Current.CancellationToken);
        await host.Services.GetRequiredService<IMessageBus>().PublishAsync(new Ping { Text = "retry-me" }, cancellationToken: TestContext.Current.CancellationToken);
        var ready = await WaitUntilAsync(() => flaky.Attempts >= 3);
        Assert.True(ready);
        Assert.Empty(host.Services.GetRequiredService<IInMemoryDeadLetters>().Snapshot());
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The last failed attempt is dead-lettered without the exception message.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task RetriesThenDeadLettersWithoutPayloadInLogs()
    {
        var logs = new CollectingLoggerProvider();
        var always = new AlwaysThrowHandler();
        using var host = Build(
            bus =>
            {
                bus.Configure(options => options.MaxImmediateRetries = 2);
                bus.UseInMemory();
                bus.AddHandler<Ping, AlwaysThrowHandler>();
            },
            logs: logs,
            always: always);
        await host.StartAsync(TestContext.Current.CancellationToken);
        await host.Services.GetRequiredService<IMessageBus>().PublishAsync(new Ping { Text = "top-secret" }, cancellationToken: TestContext.Current.CancellationToken);
        var ready = await WaitUntilAsync(() => host.Services.GetRequiredService<IInMemoryDeadLetters>().Snapshot().Count == 1);
        Assert.True(ready);
        Assert.Equal(2, always.Attempts);
        var letter = Assert.Single(host.Services.GetRequiredService<IInMemoryDeadLetters>().Snapshot());
        Assert.Equal("retries-exhausted", letter.Reason);
        Assert.Equal(typeof(InvalidOperationException).FullName, letter.ExceptionType);
        Assert.DoesNotContain("top-secret", letter.ExceptionType, StringComparison.Ordinal);
        Assert.DoesNotContain("secret payload", string.Join('\n', logs.Lines), StringComparison.Ordinal);
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A completed message id is acknowledged and not handled again.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task IdempotencySkipsRedelivery()
    {
        using var host = Build(bus =>
        {
            bus.UseInMemory();
            bus.UseIdempotencyStore<MemoryIdempotencyStore>();
            bus.AddHandler<Ping, PingHandler>();
        });
        await host.StartAsync(TestContext.Current.CancellationToken);
        var id = Guid.CreateVersion7();
        var bus = host.Services.GetRequiredService<IMessageBus>();
        var options = new PublishOptions(id);
        await bus.PublishAsync(new Ping { Text = "once" }, options, TestContext.Current.CancellationToken);
        await WaitForCallAsync(host);
        await bus.PublishAsync(new Ping { Text = "once" }, options, TestContext.Current.CancellationToken);
        await Task.Delay(200, TestContext.Current.CancellationToken);
        Assert.Single(host.Services.GetRequiredService<CallLog>().Snapshot());
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Shutdown records a blocked delivery as lost.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task ShutdownDrainDropsBlockedWork()
    {
        var gate = new GateHandler();
        using var host = Build(
            bus =>
            {
                bus.UseInMemory(options => options.DrainTimeout = TimeSpan.FromMilliseconds(50));
                bus.AddHandler<Ping, GateHandler>();
            },
            gate: gate);
        await host.StartAsync(TestContext.Current.CancellationToken);
        await host.Services.GetRequiredService<IMessageBus>().PublishAsync(new Ping { Text = "stuck" }, cancellationToken: TestContext.Current.CancellationToken);
        await gate.Entered.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);
        var transport = host.Services.GetRequiredService<InMemoryMessageBus>();
        Assert.True(transport.LostCount >= 1);
    }

    /// <summary>
    /// Unknown types, forged payloads, bad headers, oversized bodies, and spoofed publisher headers are dead-lettered.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task AttackerBodiesAreDeadLettered()
    {
        using var host = Build(bus =>
        {
            bus.Configure(options => options.MaxPayloadBytes = 256);
            bus.UseInMemory();
            bus.AddHandler<Ping, PingHandler>();
        });
        await host.StartAsync(TestContext.Current.CancellationToken);
        var transport = host.Services.GetRequiredService<InMemoryMessageBus>();
        var known = MapMessageType.TypeUrl<Ping>();
        await transport.InjectAsync(EnvelopeBytes("type.googleapis.com/gocgun.bus.tests.v1.Nope", []), "core", CancellationToken.None);
        await transport.InjectAsync(EnvelopeBytes(known, "not-protobuf"u8.ToArray()), "core", CancellationToken.None);
        var injected = EnvelopeWithHeader(known, "user_id", "attacker\r\nsecond");
        await transport.InjectAsync(injected, "core", CancellationToken.None);
        var oversized = new byte[257];
        await transport.InjectAsync(oversized, null, CancellationToken.None);
        var spoof = EnvelopeWithHeader(known, "user_id", "attacker");
        await transport.InjectAsync(spoof, publisherId: null, CancellationToken.None);
        var handled = await WaitForCallAsync(host);
        Assert.Null(handled.Context.PublisherId);
        Assert.Equal("attacker", handled.Context.Headers["user_id"]);
        var letters = host.Services.GetRequiredService<IInMemoryDeadLetters>().Snapshot();
        Assert.Contains(letters, letter => string.Equals(letter.Reason, "unknown-type", StringComparison.Ordinal));
        Assert.Contains(letters, letter => string.Equals(letter.Reason, "invalid-payload", StringComparison.Ordinal));
        Assert.Contains(letters, letter => string.Equals(letter.Reason, "invalid-headers", StringComparison.Ordinal));
        Assert.Contains(letters, letter => string.Equals(letter.Reason, "payload-too-large", StringComparison.Ordinal));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await host.Services.GetRequiredService<IMessageBus>().PublishAsync(new Ping { Text = new string('x', 400) }, cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false));
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// An inner filter's DeadLetter survives an outer filter that returns Continue.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task InnerFilterDeadLetterIsHonored()
    {
        using var host = Build(bus =>
        {
            bus.UseInMemory();
            bus.AddHandler<Ping, PingHandler>();
            bus.AddConsumeFilter<PassThroughFilter>();
            bus.AddConsumeFilter<DeadLetterFilter>();
        });
        await host.StartAsync(TestContext.Current.CancellationToken);
        await host.Services.GetRequiredService<IMessageBus>().PublishAsync(new Ping { Text = "x" }, cancellationToken: TestContext.Current.CancellationToken);
        var letters = host.Services.GetRequiredService<IInMemoryDeadLetters>();
        Assert.True(await WaitUntilAsync(() => letters.Snapshot().Any(l => string.Equals(l.Reason, "filter", StringComparison.Ordinal))));
        Assert.Empty(host.Services.GetRequiredService<CallLog>().Snapshot());
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A filter returning Continue without calling next still lets the handler run.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task ContinueWithoutNextRunsHandler()
    {
        using var host = Build(bus =>
        {
            bus.UseInMemory();
            bus.AddHandler<Ping, PingHandler>();
            bus.AddConsumeFilter<ContinueWithoutNextFilter>();
        });
        await host.StartAsync(TestContext.Current.CancellationToken);
        await host.Services.GetRequiredService<IMessageBus>().PublishAsync(new Ping { Text = "x" }, cancellationToken: TestContext.Current.CancellationToken);
        var call = await WaitForCallAsync(host);
        Assert.Equal("x", call.Text);
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    private static IHost Build(
        Action<BusBuilder> configure,
        PublisherFilter? filter = null,
        GateHandler? gate = null,
        FlakyHandler? flaky = null,
        AlwaysThrowHandler? always = null,
        CollectingLoggerProvider? logs = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        if (logs is not null)
        {
            builder.Logging.AddProvider(logs);
        }

        builder.Services.AddSingleton<CallLog>();
        builder.Services.AddSingleton<TimeProvider, FixedClock>();
        if (filter is not null)
        {
            builder.Services.AddKeyedSingleton<IPublishFilter>(BusNames.Default, filter);
        }

        if (gate is not null)
        {
            builder.Services.AddSingleton(gate);
        }

        if (flaky is not null)
        {
            builder.Services.AddSingleton(flaky);
        }

        if (always is not null)
        {
            builder.Services.AddSingleton(always);
        }

        builder.Services.AddGocgunBus(configure);
        return builder.Build();
    }

    private static async Task<Call> WaitForCallAsync(IHost host)
    {
        var log = host.Services.GetRequiredService<CallLog>();
        await log.First.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        return Assert.Single(log.Snapshot());
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> ready)
    {
        var start = Environment.TickCount64;
        while (Environment.TickCount64 - start < 5000)
        {
            if (ready())
            {
                return true;
            }

            await Task.Delay(20).ConfigureAwait(false);
        }

        return ready();
    }

    private static byte[] EnvelopeBytes(string typeUrl, byte[] payload)
    {
        var envelope = new Envelope
        {
            MessageId = Guid.CreateVersion7().ToString("D"),
            Payload = new EnvelopePayload { TypeUrl = typeUrl, Value = payload },
        };
        return ProtoCodec.Serialize(envelope);
    }

    private static byte[] EnvelopeWithHeader(string typeUrl, string key, string value)
    {
        var envelope = new Envelope
        {
            MessageId = Guid.CreateVersion7().ToString("D"),
            Payload = new EnvelopePayload { TypeUrl = typeUrl, Value = ProtoCodec.Serialize(new Ping { Text = "safe" }) },
        };
        envelope.Headers[key] = value;
        return ProtoCodec.Serialize(envelope);
    }
}
