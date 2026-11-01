// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus;

/// <summary>
/// Publishes integration events and sends integration commands on one bus.
/// The default bus is resolved as <see cref="IMessageBus"/>; a named bus is resolved as a keyed service
/// (<c>[FromKeyedServices("name")] IMessageBus</c>).
/// </summary>
public interface IMessageBus
{
    /// <summary>
    /// Publishes an event to every handler on this bus registered for its wire name.
    /// </summary>
    /// <typeparam name="TEvent">protobuf-net event contract.</typeparam>
    /// <param name="message">Payload.</param>
    /// <param name="options">Optional ids and headers.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the transport accepts the message.</returns>
    ValueTask PublishAsync<TEvent>(
        TEvent message,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default)
        where TEvent : class, IIntegrationEvent;

    /// <summary>
    /// Sends a command to every handler on this bus registered for its wire name.
    /// </summary>
    /// <typeparam name="TCommand">protobuf-net command contract.</typeparam>
    /// <param name="message">Payload.</param>
    /// <param name="options">Optional ids and headers.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the transport accepts the message.</returns>
    ValueTask SendAsync<TCommand>(
        TCommand message,
        SendOptions? options = null,
        CancellationToken cancellationToken = default)
        where TCommand : class, IIntegrationCommand;
}
