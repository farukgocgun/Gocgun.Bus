// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus.Tests.Support;

using System.Collections.Concurrent;

/// <summary>
/// Records handler invocations.
/// </summary>
public sealed class CallLog
{
    private readonly ConcurrentQueue<Call> calls = new();

    /// <summary>
    /// Gets a signal completed by the first call.
    /// </summary>
    public TaskCompletionSource<MessageContext> First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Gets the recorded calls.
    /// </summary>
    /// <returns>Calls in arrival order.</returns>
    public IReadOnlyList<Call> Snapshot()
    {
        return this.calls.ToArray();
    }

    /// <summary>
    /// Records one call.
    /// </summary>
    /// <param name="call">Call details.</param>
    public void Add(Call call)
    {
        this.calls.Enqueue(call);
        this.First.TrySetResult(call.Context);
    }
}
