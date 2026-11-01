// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus;

/// <summary>
/// Mutable publish metadata a filter can add headers to before the envelope is built.
/// </summary>
public sealed class OutgoingMessage
{
    /// <summary>
    /// Gets the message id.
    /// </summary>
    public required Guid MessageId { get; init; }

    /// <summary>
    /// Gets the payload type URL.
    /// </summary>
    public required string TypeUrl { get; init; }

    /// <summary>
    /// Gets or sets the correlation id.
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Gets or sets the broker user id the RabbitMQ transport copies to <c>user_id</c>.
    /// </summary>
    public string? PublisherId { get; set; }

    /// <summary>
    /// Gets headers copied onto the envelope.
    /// </summary>
    public IDictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
}
