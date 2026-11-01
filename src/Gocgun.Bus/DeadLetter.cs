// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus;

/// <summary>
/// One in-memory dead letter. The body is the envelope bytes. Logs do not include the body.
/// </summary>
/// <param name="Body">Serialized envelope.</param>
/// <param name="Reason">Stable reason code.</param>
/// <param name="TypeUrl">Payload type URL when the envelope parsed.</param>
/// <param name="MessageId">Message id when it parsed as a guid.</param>
/// <param name="ExceptionType">Exception type name after the last retry. The exception message is not stored.</param>
public sealed record DeadLetter(
    byte[] Body,
    string Reason,
    string? TypeUrl,
    Guid? MessageId,
    string? ExceptionType);
