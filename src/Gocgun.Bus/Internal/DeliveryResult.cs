// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus.Internal;

/// <summary>
/// Outcome of one consume attempt sequence.
/// </summary>
/// <param name="DeadLetter">True when the body must not be acknowledged as success.</param>
/// <param name="Reason">Stable reason code.</param>
/// <param name="ExceptionType">Exception type name after the last retry.</param>
/// <param name="MessageId">Envelope message id when it parsed.</param>
/// <param name="TypeUrl">Payload type URL when it parsed.</param>
internal readonly record struct DeliveryResult(
    bool DeadLetter,
    string Reason,
    string? ExceptionType,
    string? MessageId,
    string? TypeUrl);
