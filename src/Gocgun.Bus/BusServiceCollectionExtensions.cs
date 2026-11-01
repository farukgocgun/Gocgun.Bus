// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus;

using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers <see cref="IMessageBus"/> instances. Call once per bus; each bus has one transport.
/// Any number of RabbitMQ buses can live side by side, plus at most one in-memory bus.
/// </summary>
public static class BusServiceCollectionExtensions
{
    /// <summary>
    /// Adds the default bus (<see cref="BusNames.Default"/>). It is resolvable as <see cref="IMessageBus"/>
    /// and as the keyed service <c>"default"</c>.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configure">Transport and handlers.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddGocgunBus(this IServiceCollection services, Action<BusBuilder> configure)
    {
        return services.AddGocgunBus(BusNames.Default, configure);
    }

    /// <summary>
    /// Adds a named bus. Resolve it with <c>[FromKeyedServices("name")] IMessageBus</c> or
    /// <c>GetRequiredKeyedService&lt;IMessageBus&gt;("name")</c>.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="name">Unique bus name. See <see cref="BusNames.ThrowIfInvalid"/>.</param>
    /// <param name="configure">Transport and handlers.</param>
    /// <returns>The same service collection.</returns>
    /// <exception cref="InvalidOperationException">The name is taken, a second in-memory bus is added, or the bus is not valid.</exception>
    public static IServiceCollection AddGocgunBus(this IServiceCollection services, string name, Action<BusBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        BusNames.ThrowIfInvalid(name);
        var builder = new BusBuilder(services, name);
        configure(builder);
        builder.Build();
        return services;
    }
}
