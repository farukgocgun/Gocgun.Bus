// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus.Tests.Messages;

using ProtoBuf;

/// <summary>
/// Message with neither marker.
/// </summary>
[ProtoContract(Name = "gocgun.bus.tests.v1.Neutral")]
public sealed class Neutral
{
    /// <summary>
    /// Gets or sets the text.
    /// </summary>
    [ProtoMember(1)]
    public string Text { get; set; } = string.Empty;
}
