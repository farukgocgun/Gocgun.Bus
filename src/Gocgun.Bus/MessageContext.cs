// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus;

/// <summary>
/// Delivery metadata for one handler invocation. <paramref name="PublisherId"/> comes from the broker user id, never from the envelope.
/// </summary>
/// <param name="MessageId">Envelope message id.</param>
/// <param name="CorrelationId">Envelope correlation id.</param>
/// <param name="SentAt">Envelope sent time.</param>
/// <param name="DeliveryCount">Attempt count, starting at 1.</param>
/// <param name="Headers">Envelope headers. The package does not interpret them.</param>
/// <param name="PublisherId">Broker-validated user id when the transport has one.</param>
public sealed record MessageContext(
    Guid MessageId,
    string? CorrelationId,
    DateTimeOffset SentAt,
    int DeliveryCount,
    IReadOnlyDictionary<string, string> Headers,
    string? PublisherId);
