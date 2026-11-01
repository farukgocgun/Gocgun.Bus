// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus.Tests.Support;

using System.Collections.Concurrent;
using Gocgun.Bus.Tests.Messages;
using Microsoft.Extensions.Logging;

/// <summary>
/// Copies a publisher id onto the outgoing message.
/// </summary>
public sealed class PublisherFilter : IPublishFilter
{
    /// <summary>
    /// Gets or sets the broker user id to stamp.
    /// </summary>
    public string? PublisherId { get; set; }

    /// <inheritdoc />
    public ValueTask OnPublishAsync(OutgoingMessage message, CancellationToken cancellationToken)
    {
        message.PublisherId = this.PublisherId;
        return ValueTask.CompletedTask;
    }
}
