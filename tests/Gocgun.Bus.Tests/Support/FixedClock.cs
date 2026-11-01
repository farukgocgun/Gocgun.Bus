// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus.Tests.Support;

using System.Collections.Concurrent;
using Gocgun.Bus.Tests.Messages;
using Microsoft.Extensions.Logging;

/// <summary>
/// Fixed UTC clock.
/// </summary>
public sealed class FixedClock : TimeProvider
{
    /// <summary>
    /// Gets the instant returned by the clock.
    /// </summary>
    public static DateTimeOffset Instant { get; } = new(2026, 10, 20, 18, 0, 0, TimeSpan.Zero);

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow()
    {
        return Instant;
    }
}
