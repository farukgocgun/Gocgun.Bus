// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus.Tests.Support;

using System.Collections.Concurrent;
using Gocgun.Bus.Tests.Messages;
using Microsoft.Extensions.Logging;

/// <summary>
/// Records <see cref="Messages.Ping"/> deliveries.
/// </summary>
public sealed class PingHandler : IMessageHandler<Ping>
{
    private readonly CallLog log;

    /// <summary>
    /// Initializes a new instance of the <see cref="PingHandler"/> class.
    /// </summary>
    /// <param name="log">Shared log.</param>
    public PingHandler(CallLog log)
    {
        this.log = log;
    }

    /// <inheritdoc />
    public ValueTask HandleAsync(Ping message, MessageContext context, CancellationToken cancellationToken)
    {
        this.log.Add(new Call(nameof(PingHandler), message.Text, context));
        return ValueTask.CompletedTask;
    }
}
