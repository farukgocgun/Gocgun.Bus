// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus.Tests.Support;

using System.Collections.Concurrent;
using Gocgun.Bus.Tests.Messages;
using Microsoft.Extensions.Logging;

/// <summary>
/// Dead-letters without running the handler.
/// </summary>
public sealed class DeadLetterFilter : IConsumeFilter
{
    /// <inheritdoc />
    public ValueTask<ConsumeDecision> OnConsumeAsync(MessageContext context, Func<ValueTask<ConsumeDecision>> next, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(ConsumeDecision.DeadLetter);
    }
}
