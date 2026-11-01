// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus.V1;

using System.ComponentModel;
using ProtoBuf;

/// <summary>
/// Typed payload with the same field layout as <c>google.protobuf.Any</c>: type URL, then the message bytes.
/// </summary>
[ProtoContract(Name = "gocgun.bus.v1.EnvelopePayload")]
public sealed class EnvelopePayload
{
    /// <summary>Gets or sets the type URL, <c>type.googleapis.com/</c> plus the wire name.</summary>
    [ProtoMember(1, Name = "type_url")]
    [DefaultValue("")]
    public string TypeUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets the serialized message.</summary>
    [ProtoMember(2, Name = "value")]
    public byte[] Value { get; set; } = [];
}
