// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus.Internal;

using Microsoft.Extensions.Diagnostics.HealthChecks;

/// <summary>
/// Reports whether the publisher and consumer connections are open and not blocked.
/// </summary>
internal sealed class RabbitMqHealthCheck : IHealthCheck
{
    private readonly RabbitMqMessageBus bus;

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMqHealthCheck"/> class.
    /// </summary>
    /// <param name="bus">RabbitMQ transport.</param>
    public RabbitMqHealthCheck(RabbitMqMessageBus bus)
    {
        this.bus = bus;
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (this.bus.ConnectionsHealthy)
        {
            return Task.FromResult(HealthCheckResult.Healthy("RabbitMQ connections are open."));
        }

        return Task.FromResult(HealthCheckResult.Unhealthy("RabbitMQ connection is closed or blocked."));
    }
}
