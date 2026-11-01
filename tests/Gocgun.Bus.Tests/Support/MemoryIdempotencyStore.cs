// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus.Tests.Support;

using System.Collections.Concurrent;
using Gocgun.Bus.Tests.Messages;
using Microsoft.Extensions.Logging;

/// <summary>
/// Completes a message only after the handler returns.
/// </summary>
public sealed class MemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, byte> done = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public ValueTask<bool> TryBeginAsync(Guid messageId, string handlerName, CancellationToken cancellationToken)
    {
        var alreadyDone = this.done.ContainsKey(Key(messageId, handlerName));
        return ValueTask.FromResult(!alreadyDone);
    }

    /// <inheritdoc />
    public ValueTask CompleteAsync(Guid messageId, string handlerName, CancellationToken cancellationToken)
    {
        this.done[Key(messageId, handlerName)] = 1;
        return ValueTask.CompletedTask;
    }

    private static string Key(Guid messageId, string handlerName)
    {
        return messageId.ToString("D") + "|" + handlerName;
    }
}
