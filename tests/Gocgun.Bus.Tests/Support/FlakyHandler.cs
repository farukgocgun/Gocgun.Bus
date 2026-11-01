// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus.Tests.Support;

using System.Collections.Concurrent;
using Gocgun.Bus.Tests.Messages;
using Microsoft.Extensions.Logging;

/// <summary>
/// Throws until <see cref="FailUntil"/> attempts have been seen.
/// </summary>
public sealed class FlakyHandler : IMessageHandler<Ping>
{
    private int attempts;

    /// <summary>
    /// Gets or sets how many attempts throw. The next one succeeds.
    /// </summary>
    public int FailUntil { get; set; } = 2;

    /// <summary>
    /// Gets how many times the handler ran.
    /// </summary>
    public int Attempts => Volatile.Read(ref this.attempts);

    /// <inheritdoc />
    public ValueTask HandleAsync(Ping message, MessageContext context, CancellationToken cancellationToken)
    {
        var attempt = Interlocked.Increment(ref this.attempts);
        if (attempt <= this.FailUntil)
        {
            throw new InvalidOperationException("secret payload " + message.Text);
        }

        return ValueTask.CompletedTask;
    }
}
