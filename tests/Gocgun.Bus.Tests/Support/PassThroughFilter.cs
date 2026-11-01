// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus.Tests.Support;

/// <summary>
/// Calls next and returns Continue regardless of the inner decision.
/// </summary>
public sealed class PassThroughFilter : IConsumeFilter
{
    /// <inheritdoc />
    public async ValueTask<ConsumeDecision> OnConsumeAsync(MessageContext context, Func<ValueTask<ConsumeDecision>> next, CancellationToken cancellationToken)
    {
        await next().ConfigureAwait(false);
        return ConsumeDecision.Continue;
    }
}
