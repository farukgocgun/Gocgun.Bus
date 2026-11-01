// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus;

using Gocgun.Bus.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProtoBuf;

/// <summary>
/// Registers one named bus: its transport, handlers, filters and idempotency store.
/// A host can register many buses (several RabbitMQ brokers, at most one in-memory bus) by calling
/// <see cref="BusServiceCollectionExtensions.AddGocgunBus(IServiceCollection, string, Action{BusBuilder})"/> once per bus.
/// </summary>
public sealed class BusBuilder
{
    private readonly IServiceCollection services;
    private readonly List<IHandlerBinding> bindings = new();
    private readonly BusOptions busOptions = new();
    private InMemoryBusOptions? memory;
    private RabbitMqOptions? rabbit;

    /// <summary>
    /// Initializes a new instance of the <see cref="BusBuilder"/> class.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="name">Bus name.</param>
    internal BusBuilder(IServiceCollection services, string name)
    {
        this.services = services;
        this.Name = name;
    }

    /// <summary>
    /// Gets the bus name. Keyed services of this bus use it as the key.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Sets shared limits. Call this before <see cref="UseRabbitMq"/> when prefetch should be copied.
    /// </summary>
    /// <param name="configure">Mutator.</param>
    /// <returns>This builder.</returns>
    public BusBuilder Configure(Action<BusOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(this.busOptions);
        return this;
    }

    /// <summary>
    /// Selects the in-memory transport for this bus. Only one bus per service collection can be in-memory.
    /// </summary>
    /// <param name="configure">Optional channel limits.</param>
    /// <returns>This builder.</returns>
    public BusBuilder UseInMemory(Action<InMemoryBusOptions>? configure = null)
    {
        this.ThrowIfTransportChosen();

        var options = new InMemoryBusOptions();
        configure?.Invoke(options);
        this.memory = options;
        return this;
    }

    /// <summary>
    /// Selects the RabbitMQ transport for this bus. Each RabbitMQ bus has its own connections and options.
    /// </summary>
    /// <param name="configure">Connection and topology settings. Credentials have no defaults.</param>
    /// <returns>This builder.</returns>
    public BusBuilder UseRabbitMq(Action<RabbitMqOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        this.ThrowIfTransportChosen();

        var options = new RabbitMqOptions
        {
            PrefetchCount = this.busOptions.PrefetchCount,
            MaxConcurrency = this.busOptions.MaxConcurrency,
        };
        configure(options);
        this.rabbit = options;
        return this;
    }

    /// <summary>
    /// Registers a handler for one protobuf-net message on this bus. The handler consumes only from this bus;
    /// add it to another bus as well to consume there too.
    /// </summary>
    /// <typeparam name="TMessage">Event or command contract, marked with <see cref="ProtoContractAttribute"/> and a wire name.</typeparam>
    /// <typeparam name="THandler">Handler implementation.</typeparam>
    /// <returns>This builder.</returns>
    public BusBuilder AddHandler<TMessage, THandler>()
        where TMessage : class
        where THandler : class, IMessageHandler<TMessage>
    {
        var isCommand = typeof(IIntegrationCommand).IsAssignableFrom(typeof(TMessage));
        var isEvent = typeof(IIntegrationEvent).IsAssignableFrom(typeof(TMessage));
        if (isCommand == isEvent)
        {
            throw new InvalidOperationException("The message must be exactly one of IIntegrationEvent or IIntegrationCommand.");
        }

        // Throws early when the contract attribute or its wire name is missing.
        _ = MapMessageType.WireName<TMessage>();
        this.services.TryAddScoped<THandler>();
        this.bindings.Add(new HandlerBinding<TMessage>(this.Name, typeof(THandler), isCommand));
        return this;
    }

    /// <summary>
    /// Registers a publish filter for this bus, resolved in its own scope per publish.
    /// </summary>
    /// <typeparam name="TFilter">Filter type.</typeparam>
    /// <returns>This builder.</returns>
    public BusBuilder AddPublishFilter<TFilter>()
        where TFilter : class, IPublishFilter
    {
        this.services.AddKeyedScoped<IPublishFilter, TFilter>(this.Name);
        return this;
    }

    /// <summary>
    /// Registers a consume filter for this bus, resolved in the handler scope.
    /// </summary>
    /// <typeparam name="TFilter">Filter type.</typeparam>
    /// <returns>This builder.</returns>
    public BusBuilder AddConsumeFilter<TFilter>()
        where TFilter : class, IConsumeFilter
    {
        this.services.AddKeyedScoped<IConsumeFilter, TFilter>(this.Name);
        return this;
    }

    /// <summary>
    /// Registers a singleton idempotency store for this bus. The package ships no store.
    /// </summary>
    /// <typeparam name="TStore">Store type.</typeparam>
    /// <returns>This builder.</returns>
    public BusBuilder UseIdempotencyStore<TStore>()
        where TStore : class, IIdempotencyStore
    {
        this.services.AddKeyedSingleton<IIdempotencyStore, TStore>(this.Name);
        return this;
    }

    /// <summary>
    /// Writes the registration into the service collection.
    /// </summary>
    internal void Build()
    {
        if (this.memory is null && this.rabbit is null)
        {
            throw new InvalidOperationException("Bus '" + this.Name + "': select UseInMemory or UseRabbitMq.");
        }

        OptionGuards.ThrowIfBusInvalid(this.busOptions);
        var registry = BusRegistry.From(this.services);
        this.services.TryAddSingleton(TimeProvider.System);
        IReadOnlyList<IHandlerBinding> handlers = this.bindings.ToArray();

        if (this.memory is not null)
        {
            OptionGuards.ThrowIfMemoryInvalid(this.memory);
            registry.AddInMemory(this.Name);
            this.BuildInMemory(this.memory, handlers);
        }
        else
        {
            var rabbitOptions = this.rabbit ?? throw new InvalidOperationException("RabbitMQ options are missing.");
            var validated = new RabbitMqOptionsValidator().Validate(this.Name, rabbitOptions);
            if (validated.Failed)
            {
                throw new InvalidOperationException("Bus '" + this.Name + "': " + validated.FailureMessage);
            }

            registry.AddRabbitMq(this.Name, rabbitOptions);
            this.BuildRabbitMq(rabbitOptions, handlers);
        }

        if (string.Equals(this.Name, BusNames.Default, StringComparison.Ordinal))
        {
            this.services.AddSingleton<IMessageBus>(provider => provider.GetRequiredKeyedService<IMessageBus>(BusNames.Default));
        }
    }

    private void BuildInMemory(InMemoryBusOptions options, IReadOnlyList<IHandlerBinding> handlers)
    {
        var name = this.Name;
        var shared = this.busOptions;
        this.services.AddKeyedSingleton(name, options);
        this.services.AddKeyedSingleton(name, handlers);

        // At most one in-memory bus exists, so the transport and its dead letters are also registered without a key.
        this.services.AddSingleton(provider => new InMemoryMessageBus(
            provider,
            name,
            handlers,
            shared,
            options,
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<InMemoryMessageBus>>()));
        this.services.AddKeyedSingleton<IMessageBus>(name, (provider, _) => provider.GetRequiredService<InMemoryMessageBus>());
        this.services.AddKeyedSingleton<IInMemoryDeadLetters>(name, (provider, _) => provider.GetRequiredService<InMemoryMessageBus>());
        this.services.AddSingleton<IInMemoryDeadLetters>(provider => provider.GetRequiredService<InMemoryMessageBus>());
        this.services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<InMemoryMessageBus>());
    }

    private void BuildRabbitMq(RabbitMqOptions options, IReadOnlyList<IHandlerBinding> handlers)
    {
        var name = this.Name;
        var shared = this.busOptions;
        this.services.AddKeyedSingleton(name, options);
        this.services.AddKeyedSingleton(name, handlers);
        this.services.AddKeyedSingleton(name, (provider, _) => new RabbitMqMessageBus(
            provider,
            name,
            handlers,
            shared,
            options,
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<RabbitMqMessageBus>>()));
        this.services.AddKeyedSingleton<IMessageBus>(name, (provider, _) => provider.GetRequiredKeyedService<RabbitMqMessageBus>(name));
        this.services.AddSingleton<IHostedService>(provider => provider.GetRequiredKeyedService<RabbitMqMessageBus>(name));
        this.services.AddKeyedSingleton(name, (provider, _) => new RabbitMqHealthCheck(provider.GetRequiredKeyedService<RabbitMqMessageBus>(name)));

        // The default bus keeps the original check name; other buses add their name.
        var checkName = string.Equals(name, BusNames.Default, StringComparison.Ordinal)
            ? "gocgun-bus-rabbitmq"
            : "gocgun-bus-rabbitmq-" + name;
        this.services.AddHealthChecks().Add(new HealthCheckRegistration(
            checkName,
            provider => provider.GetRequiredKeyedService<RabbitMqHealthCheck>(name),
            failureStatus: null,
            tags: null));
    }

    private void ThrowIfTransportChosen()
    {
        if (this.memory is not null || this.rabbit is not null)
        {
            throw new InvalidOperationException(
                "Bus '" + this.Name + "' already has a transport. Register each additional bus with its own AddGocgunBus(name, ...) call.");
        }
    }
}
