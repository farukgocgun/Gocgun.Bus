// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus.Tests.Support;

using System.Collections.Concurrent;
using Gocgun.Bus.Tests.Messages;
using Microsoft.Extensions.Logging;

/// <summary>
/// Handler for a protobuf message that is not an event or a command.
/// </summary>
public sealed class NeutralHandler : IMessageHandler<Neutral>
{
    /// <inheritdoc />
    public ValueTask HandleAsync(Neutral message, MessageContext context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}
