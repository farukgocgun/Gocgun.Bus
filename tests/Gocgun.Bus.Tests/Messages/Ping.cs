// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus.Tests.Messages;

using ProtoBuf;

/// <summary>
/// Test event.
/// </summary>
[ProtoContract(Name = "gocgun.bus.tests.v1.Ping")]
public sealed class Ping : IIntegrationEvent
{
    /// <summary>
    /// Gets or sets the text.
    /// </summary>
    [ProtoMember(1)]
    public string Text { get; set; } = string.Empty;
}
