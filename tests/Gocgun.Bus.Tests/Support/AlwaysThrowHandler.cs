// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus.Tests.Support;

using System.Collections.Concurrent;
using Gocgun.Bus.Tests.Messages;
using Microsoft.Extensions.Logging;

/// <summary>
/// Always throws. The message text must not reach logs.
/// </summary>
public sealed class AlwaysThrowHandler : IMessageHandler<Ping>
{
    private int attempts;

    /// <summary>
    /// Gets how many times the handler ran.
    /// </summary>
    public int Attempts => Volatile.Read(ref this.attempts);

    /// <inheritdoc />
    public ValueTask HandleAsync(Ping message, MessageContext context, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref this.attempts);
        throw new InvalidOperationException("secret payload " + message.Text);
    }
}
