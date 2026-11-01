// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus.Tests.Support;

/// <summary>
/// One handler invocation.
/// </summary>
/// <param name="Handler">Handler name.</param>
/// <param name="Text">Payload text.</param>
/// <param name="Context">Delivery context.</param>
public sealed record Call(string Handler, string Text, MessageContext Context);
