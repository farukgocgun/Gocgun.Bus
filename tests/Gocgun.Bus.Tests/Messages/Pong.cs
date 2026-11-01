// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus.Tests.Messages;

using ProtoBuf;

/// <summary>
/// Test command.
/// </summary>
[ProtoContract(Name = "gocgun.bus.tests.v1.Pong")]
public sealed class Pong : IIntegrationCommand
{
    /// <summary>
    /// Gets or sets the text.
    /// </summary>
    [ProtoMember(1)]
    public string Text { get; set; } = string.Empty;
}
