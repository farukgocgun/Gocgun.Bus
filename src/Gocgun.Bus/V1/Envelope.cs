// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus.V1;

using System.ComponentModel;
using ProtoBuf;

/// <summary>
/// Broker message body. No tenant field. Payload type URL is the wire name.
/// Field numbers and types match the former <c>gocgun.bus.v1.Envelope</c> proto3 message, so either side reads the other's bytes.
/// Empty strings are skipped on write, as proto3 does.
/// Unknown fields are kept through <see cref="Extensible"/>.
/// </summary>
[ProtoContract(Name = "gocgun.bus.v1.Envelope")]
[CompatibilityLevel(CompatibilityLevel.Level300)]
public sealed class Envelope : Extensible
{
    /// <summary>Gets or sets the message id, a hyphenated guid.</summary>
    [ProtoMember(1, Name = "message_id")]
    [DefaultValue("")]
    public string MessageId { get; set; } = string.Empty;

    /// <summary>Gets or sets the correlation id. Empty when not set.</summary>
    [ProtoMember(2, Name = "correlation_id")]
    [DefaultValue("")]
    public string CorrelationId { get; set; } = string.Empty;

    /// <summary>Gets or sets the causation id. Empty when not set.</summary>
    [ProtoMember(3, Name = "causation_id")]
    [DefaultValue("")]
    public string CausationId { get; set; } = string.Empty;

    /// <summary>Gets or sets the W3C traceparent. Empty when not set.</summary>
    [ProtoMember(4, Name = "traceparent")]
    [DefaultValue("")]
    public string Traceparent { get; set; } = string.Empty;

    /// <summary>Gets or sets the sent time, UTC, written as <c>google.protobuf.Timestamp</c>.</summary>
    [ProtoMember(5, Name = "sent_at")]
    public DateTime? SentAt { get; set; }

    /// <summary>Gets the headers, written as a proto3 <c>map&lt;string, string&gt;</c>.</summary>
    [ProtoMember(6, Name = "headers")]
    public IDictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets or sets the payload, written with the <c>google.protobuf.Any</c> layout.</summary>
    [ProtoMember(7, Name = "payload")]
    public EnvelopePayload? Payload { get; set; }
}
