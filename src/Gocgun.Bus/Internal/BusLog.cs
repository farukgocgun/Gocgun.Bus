// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus.Internal;

using Microsoft.Extensions.Logging;

/// <summary>
/// Log lines for the bus. The payload and exception message are never written.
/// </summary>
internal static partial class BusLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Dead-lettered message {MessageId} reason {Reason} type {TypeUrl} exception {ExceptionType}.")]
    internal static partial void DeadLetter(
        ILogger logger,
        string? messageId,
        string reason,
        string? typeUrl,
        string? exceptionType);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "In-memory deliveries lost on shutdown: {Count}.")]
    internal static partial void ShutdownLost(ILogger logger, int count);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "Handler failed for {MessageId} attempt {Attempt} exception {ExceptionType}.")]
    internal static partial void AttemptFailed(ILogger logger, string? messageId, int attempt, string? exceptionType);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Warning,
        Message = "Broker returned message {MessageId} as unroutable for routing key {RoutingKey}; no queue is bound.")]
    internal static partial void Unroutable(ILogger logger, string? messageId, string routingKey);
}
