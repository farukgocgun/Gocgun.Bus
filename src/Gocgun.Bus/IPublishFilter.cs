// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus;

/// <summary>
/// Host hook that runs before a message is serialized.
/// </summary>
public interface IPublishFilter
{
    /// <summary>
    /// Adds headers or a publisher id.
    /// </summary>
    /// <param name="message">Outgoing metadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the filter finishes.</returns>
    ValueTask OnPublishAsync(OutgoingMessage message, CancellationToken cancellationToken);
}
