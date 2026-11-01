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
/// Starts a RabbitMQ 4 container.
/// </summary>
public sealed class RabbitFixture : IAsyncLifetime
{
    /// <summary>
    /// Gets the container.
    /// </summary>
    public RabbitMqContainer Container { get; } = new RabbitMqBuilder("rabbitmq:4-alpine")
        .WithUsername("guest")
        .WithPassword("guest")
        .WithEnvironment("RABBITMQ_SERVER_ADDITIONAL_ERL_ARGS", "-rabbit loopback_users []")
        .Build();

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        await this.Container.StartAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        return this.Container.DisposeAsync();
    }
}
