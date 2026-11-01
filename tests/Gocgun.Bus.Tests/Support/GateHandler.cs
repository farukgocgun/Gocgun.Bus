// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus.Tests.Support;

using System.Collections.Concurrent;
using Gocgun.Bus.Tests.Messages;
using Microsoft.Extensions.Logging;

/// <summary>
/// Holds the reader until released or cancelled.
/// </summary>
public sealed class GateHandler : IMessageHandler<Ping>
{
    private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Gets a task that completes when the handler starts.
    /// </summary>
    public Task Entered => this.entered.Task;

    /// <summary>
    /// Lets the handler finish.
    /// </summary>
    public void Release()
    {
        this.release.TrySetResult();
    }

    /// <inheritdoc />
    public async ValueTask HandleAsync(Ping message, MessageContext context, CancellationToken cancellationToken)
    {
        this.entered.TrySetResult();
        await this.release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}
