// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus;

/// <summary>
/// Host hook around a handler invocation.
/// </summary>
public interface IConsumeFilter
{
    /// <summary>
    /// Wraps the rest of the consume pipeline.
    /// </summary>
    /// <param name="context">Delivery metadata.</param>
    /// <param name="next">The remainder of the pipeline. Returns the decision of the inner filters, or Continue once the handler ran.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The decision for this delivery.</returns>
    ValueTask<ConsumeDecision> OnConsumeAsync(
        MessageContext context,
        Func<ValueTask<ConsumeDecision>> next,
        CancellationToken cancellationToken);
}
