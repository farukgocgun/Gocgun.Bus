// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus.Tests.Support;

using System.Collections.Concurrent;
using Gocgun.Bus.Tests.Messages;
using Microsoft.Extensions.Logging;

/// <summary>
/// Records commands.
/// </summary>
public sealed class PongHandler : IMessageHandler<Pong>
{
    private readonly CallLog log;

    /// <summary>
    /// Initializes a new instance of the <see cref="PongHandler"/> class.
    /// </summary>
    /// <param name="log">Shared log.</param>
    public PongHandler(CallLog log)
    {
        this.log = log;
    }

    /// <inheritdoc />
    public ValueTask HandleAsync(Pong message, MessageContext context, CancellationToken cancellationToken)
    {
        this.log.Add(new Call(nameof(PongHandler), message.Text, context));
        return ValueTask.CompletedTask;
    }
}
