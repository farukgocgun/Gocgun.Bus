// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus;

/// <summary>
/// Optional inbox. The package ships no store.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Returns false when this handler already completed the message. The delivery is acknowledged and skipped.
    /// </summary>
    /// <param name="messageId">Envelope message id.</param>
    /// <param name="handlerName">Handler name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the handler should run.</returns>
    ValueTask<bool> TryBeginAsync(Guid messageId, string handlerName, CancellationToken cancellationToken);

    /// <summary>
    /// Records a successful handle so a later delivery is skipped.
    /// </summary>
    /// <param name="messageId">Envelope message id.</param>
    /// <param name="handlerName">Handler name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the record is stored.</returns>
    ValueTask CompleteAsync(Guid messageId, string handlerName, CancellationToken cancellationToken);
}
