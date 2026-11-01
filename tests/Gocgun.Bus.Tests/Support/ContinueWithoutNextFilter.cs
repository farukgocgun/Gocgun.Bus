// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus.Tests.Support;

/// <summary>
/// Returns Continue without calling next.
/// </summary>
public sealed class ContinueWithoutNextFilter : IConsumeFilter
{
    /// <inheritdoc />
    public ValueTask<ConsumeDecision> OnConsumeAsync(MessageContext context, Func<ValueTask<ConsumeDecision>> next, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(ConsumeDecision.Continue);
    }
}
