// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus.Internal;

using V1 = Gocgun.Bus.V1;

/// <summary>
/// One registered handler and its parser.
/// </summary>
internal interface IHandlerBinding
{
    /// <summary>Gets the name of the bus that owns the handler.</summary>
    string BusName { get; }

    /// <summary>Gets the payload type URL.</summary>
    string TypeUrl { get; }

    /// <summary>Gets the protobuf full name.</summary>
    string WireName { get; }

    /// <summary>Gets the handler name used for queues and the idempotency key.</summary>
    string HandlerName { get; }

    /// <summary>Gets the in-memory channel key.</summary>
    string ChannelKey { get; }

    /// <summary>Gets a value indicating whether the message is an integration command.</summary>
    bool IsCommand { get; }

    /// <summary>
    /// Runs filters, the idempotency check, and the handler.
    /// </summary>
    /// <param name="scope">Handler scope.</param>
    /// <param name="envelope">Parsed envelope.</param>
    /// <param name="publisherId">Broker user id.</param>
    /// <param name="deliveryCount">Attempt number, starting at 1.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The consume decision.</returns>
    ValueTask<ConsumeDecision> DispatchAsync(
        IServiceProvider scope,
        V1.Envelope envelope,
        string? publisherId,
        int deliveryCount,
        CancellationToken cancellationToken);
}
