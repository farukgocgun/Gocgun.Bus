// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus;

/// <summary>
/// Optional metadata for <see cref="IMessageBus.PublishAsync{TEvent}"/>.
/// </summary>
/// <param name="MessageId">Stable id. A null value gets a new id.</param>
/// <param name="CorrelationId">Caller correlation id.</param>
/// <param name="Headers">Opaque headers copied onto the envelope.</param>
public sealed record PublishOptions(
    Guid? MessageId = null,
    string? CorrelationId = null,
    IReadOnlyDictionary<string, string>? Headers = null);
