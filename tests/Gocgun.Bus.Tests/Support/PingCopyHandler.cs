// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus.Tests.Support;

using System.Collections.Concurrent;
using Gocgun.Bus.Tests.Messages;
using Microsoft.Extensions.Logging;

/// <summary>
/// Second event handler used for fan-out.
/// </summary>
public sealed class PingCopyHandler : IMessageHandler<Ping>
{
    private readonly CallLog log;

    /// <summary>
    /// Initializes a new instance of the <see cref="PingCopyHandler"/> class.
    /// </summary>
    /// <param name="log">Shared log.</param>
    public PingCopyHandler(CallLog log)
    {
        this.log = log;
    }

    /// <inheritdoc />
    public ValueTask HandleAsync(Ping message, MessageContext context, CancellationToken cancellationToken)
    {
        this.log.Add(new Call(nameof(PingCopyHandler), message.Text, context));
        return ValueTask.CompletedTask;
    }
}
