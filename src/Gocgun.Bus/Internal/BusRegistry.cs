// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus.Internal;

using System.Globalization;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registration-time list of the buses in one service collection. It enforces unique names,
/// at most one in-memory bus, and one bus per RabbitMQ queue namespace.
/// </summary>
internal sealed class BusRegistry
{
    private readonly Dictionary<string, string> transports = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> rabbitEndpoints = new(StringComparer.Ordinal);
    private string? inMemoryBus;

    /// <summary>
    /// Returns the registry stored in the service collection, adding it on first use.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The shared registry.</returns>
    public static BusRegistry From(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == typeof(BusRegistry) && !descriptor.IsKeyedService && descriptor.ImplementationInstance is BusRegistry existing)
            {
                return existing;
            }
        }

        var registry = new BusRegistry();
        services.AddSingleton(registry);
        return registry;
    }

    /// <summary>
    /// Records an in-memory bus.
    /// </summary>
    /// <param name="name">Bus name.</param>
    public void AddInMemory(string name)
    {
        this.ThrowIfTaken(name);
        if (this.inMemoryBus is not null)
        {
            throw new InvalidOperationException(
                "Only one in-memory bus is allowed. Bus '" + this.inMemoryBus + "' already uses the in-memory transport.");
        }

        this.inMemoryBus = name;
        this.transports[name] = "in-memory";
    }

    /// <summary>
    /// Records a RabbitMQ bus.
    /// </summary>
    /// <param name="name">Bus name.</param>
    /// <param name="options">Validated broker options.</param>
    public void AddRabbitMq(string name, RabbitMqOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.ThrowIfTaken(name);

        // Two buses with the same broker, vhost, exchange and service name would declare and consume the same queues.
        var endpoint = string.Join(
            '|',
            options.HostName.ToUpperInvariant(),
            options.Port.ToString(CultureInfo.InvariantCulture),
            options.VirtualHost,
            options.Exchange,
            options.ServiceName);
        if (this.rabbitEndpoints.TryGetValue(endpoint, out var other))
        {
            throw new InvalidOperationException(
                "Bus '" + name + "' uses the same RabbitMQ host, virtual host, exchange and service name as bus '" + other + "'. Change ServiceName or Exchange.");
        }

        this.rabbitEndpoints[endpoint] = name;
        this.transports[name] = "rabbitmq";
    }

    private void ThrowIfTaken(string name)
    {
        if (this.transports.ContainsKey(name))
        {
            throw new InvalidOperationException("A bus named '" + name + "' is already registered.");
        }
    }
}
