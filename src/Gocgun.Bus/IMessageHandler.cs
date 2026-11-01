// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus;

/// <summary>
/// Handles one registered protobuf-net message.
/// </summary>
/// <typeparam name="TMessage">Registered message type.</typeparam>
public interface IMessageHandler<in TMessage>
    where TMessage : class
{
    /// <summary>
    /// Handles one delivery.
    /// </summary>
    /// <param name="message">Parsed payload.</param>
    /// <param name="context">Delivery metadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when handling finishes.</returns>
    ValueTask HandleAsync(TMessage message, MessageContext context, CancellationToken cancellationToken);
}
