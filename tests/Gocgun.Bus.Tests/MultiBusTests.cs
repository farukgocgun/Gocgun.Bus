// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus.Tests;

using Gocgun.Bus.Internal;
using Gocgun.Bus.Tests.Messages;
using Gocgun.Bus.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

/// <summary>
/// Several named buses in one service collection: many RabbitMQ buses plus at most one in-memory bus.
/// </summary>
public sealed class MultiBusTests
{
    /// <summary>
    /// An in-memory default bus and two RabbitMQ buses register side by side, each with its own transport.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task InMemoryAndSeveralRabbitBusesCoexist()
    {
        var services = NewServices();
        services.AddGocgunBus(bus => bus.UseInMemory().AddHandler<Ping, PingHandler>());
        services.AddGocgunBus("orders", bus => bus.UseRabbitMq(options => Rabbit(options, "broker-a", "orders")).AddHandler<Ping, PingHandler>());
        services.AddGocgunBus("analytics", bus => bus.UseRabbitMq(options => Rabbit(options, "broker-b", "analytics")).AddHandler<Ping, PingCopyHandler>());
        await using var provider = services.BuildServiceProvider();

        Assert.IsType<InMemoryMessageBus>(provider.GetRequiredService<IMessageBus>());
        Assert.Same(provider.GetRequiredService<IMessageBus>(), provider.GetRequiredKeyedService<IMessageBus>(BusNames.Default));
        var orders = Assert.IsType<RabbitMqMessageBus>(provider.GetRequiredKeyedService<IMessageBus>("orders"));
        var analytics = Assert.IsType<RabbitMqMessageBus>(provider.GetRequiredKeyedService<IMessageBus>("analytics"));
        Assert.NotSame(orders, analytics);
        Assert.Equal("broker-a", provider.GetRequiredKeyedService<RabbitMqOptions>("orders").HostName);
        Assert.Equal("broker-b", provider.GetRequiredKeyedService<RabbitMqOptions>("analytics").HostName);
        Assert.Equal(3, provider.GetServices<IHostedService>().Count(service => service is InMemoryMessageBus or RabbitMqMessageBus));

        var orderHandlers = provider.GetRequiredKeyedService<IReadOnlyList<IHandlerBinding>>("orders");
        var analyticsHandlers = provider.GetRequiredKeyedService<IReadOnlyList<IHandlerBinding>>("analytics");
        Assert.Equal("orders", Assert.Single(orderHandlers).BusName);
        Assert.EndsWith(nameof(PingCopyHandler), Assert.Single(analyticsHandlers).HandlerName, StringComparison.Ordinal);

        var checks = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations.Select(check => check.Name).ToArray();
        Assert.Contains("gocgun-bus-rabbitmq-orders", checks, StringComparer.Ordinal);
        Assert.Contains("gocgun-bus-rabbitmq-analytics", checks, StringComparer.Ordinal);
    }

    /// <summary>
    /// Only one in-memory bus is allowed, whatever its name.
    /// </summary>
    [Fact]
    public void SecondInMemoryBusThrows()
    {
        var services = NewServices();
        services.AddGocgunBus(bus => bus.UseInMemory());
        var error = Assert.Throws<InvalidOperationException>(() => services.AddGocgunBus("local", bus => bus.UseInMemory()));
        Assert.Contains("Only one in-memory bus", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Bus names are unique, including the implicit default name.
    /// </summary>
    [Fact]
    public void DuplicateNameThrows()
    {
        var services = NewServices();
        services.AddGocgunBus("orders", bus => bus.UseRabbitMq(options => Rabbit(options, "broker-a", "orders")));
        Assert.Throws<InvalidOperationException>(() => services.AddGocgunBus("orders", bus => bus.UseRabbitMq(options => Rabbit(options, "broker-b", "orders2"))));

        services.AddGocgunBus(bus => bus.UseInMemory());
        Assert.Throws<InvalidOperationException>(() => services.AddGocgunBus(BusNames.Default, bus => bus.UseRabbitMq(options => Rabbit(options, "broker-c", "other"))));
    }

    /// <summary>
    /// Two RabbitMQ buses that would share queues are rejected; changing the service name is enough.
    /// </summary>
    [Fact]
    public void SameRabbitQueueNamespaceTwiceThrows()
    {
        var services = NewServices();
        services.AddGocgunBus("a", bus => bus.UseRabbitMq(options => Rabbit(options, "broker", "svc")));
        Assert.Throws<InvalidOperationException>(() => services.AddGocgunBus("b", bus => bus.UseRabbitMq(options => Rabbit(options, "BROKER", "svc"))));
        services.AddGocgunBus("c", bus => bus.UseRabbitMq(options => Rabbit(options, "broker", "svc-2")));
    }

    /// <summary>
    /// One bus still has one transport; the error points to registering another bus.
    /// </summary>
    [Fact]
    public void SecondTransportOnOneBusPointsToNamedBuses()
    {
        var services = NewServices();
        var error = Assert.Throws<InvalidOperationException>(() => services.AddGocgunBus(bus =>
        {
            bus.UseInMemory();
            bus.UseRabbitMq(options => Rabbit(options, "broker", "svc"));
        }));
        Assert.Contains("AddGocgunBus(name", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Names that would break connection or health check names are rejected.
    /// </summary>
    /// <param name="name">Bus name.</param>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("has space")]
    [InlineData("slash/name")]
    [InlineData("çğ")]
    public void InvalidNameThrows(string name)
    {
        var services = NewServices();
        Assert.ThrowsAny<ArgumentException>(() => services.AddGocgunBus(name, bus => bus.UseInMemory()));
    }

    /// <summary>
    /// Without a bus named default, no unkeyed <see cref="IMessageBus"/> is registered, so a wrong bus is never picked silently.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task NamedBusesOnlyRegisterNoUnkeyedBus()
    {
        var services = NewServices();
        services.AddGocgunBus("local", bus => bus.UseInMemory());
        await using var provider = services.BuildServiceProvider();
        Assert.Null(provider.GetService<IMessageBus>());
        Assert.NotNull(provider.GetRequiredKeyedService<IMessageBus>("local"));
        Assert.NotNull(provider.GetService<IInMemoryDeadLetters>());
    }

    /// <summary>
    /// A publish on one bus reaches only that bus's handlers and runs only that bus's publish filters.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task PublishFiltersAndHandlersStayOnTheirBus()
    {
        var services = NewServices();
        services.AddSingleton<CallLog>();
        services.AddGocgunBus("local", bus => bus.UseInMemory().AddHandler<Ping, PingHandler>());
        services.AddGocgunBus("events", bus => bus
            .UseRabbitMq(options => Rabbit(options, "broker", "events"))
            .AddHandler<Ping, PingCopyHandler>()
            .AddPublishFilter<StampingFilter>());
        await using var provider = services.BuildServiceProvider();
        var memory = provider.GetRequiredService<InMemoryMessageBus>();
        await memory.StartAsync(TestContext.Current.CancellationToken);

        await provider.GetRequiredKeyedService<IMessageBus>("local").PublishAsync(new Ping { Text = "local" }, cancellationToken: TestContext.Current.CancellationToken);
        var log = provider.GetRequiredService<CallLog>();
        await log.First.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await memory.StopAsync(TestContext.Current.CancellationToken);

        var call = Assert.Single(log.Snapshot());
        Assert.Equal(nameof(PingHandler), call.Handler);
        Assert.Null(call.Context.PublisherId);
        Assert.False(call.Context.Headers.ContainsKey(StampingFilter.Header));
    }

    private static ServiceCollection NewServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return services;
    }

    private static void Rabbit(RabbitMqOptions options, string host, string service)
    {
        options.HostName = host;
        options.UserName = "user";
        options.Password = "secret";
        options.ServiceName = service;
    }

    /// <summary>
    /// Publish filter that marks every message it sees.
    /// </summary>
    private sealed class StampingFilter : IPublishFilter
    {
        /// <summary>Header the filter adds.</summary>
        public const string Header = "stamped-by";

        /// <inheritdoc />
        public ValueTask OnPublishAsync(OutgoingMessage message, CancellationToken cancellationToken)
        {
            message.Headers[Header] = "events";
            message.PublisherId = "events-user";
            return ValueTask.CompletedTask;
        }
    }
}
