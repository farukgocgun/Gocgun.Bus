// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus.Internal;

using System.Diagnostics;
using V1 = Gocgun.Bus.V1;

/// <summary>
/// Builds and reads <see cref="V1.Envelope"/>.
/// </summary>
internal static class EnvelopeFactory
{
    /// <summary>
    /// Builds an envelope and returns the publisher id chosen by filters.
    /// </summary>
    /// <typeparam name="TMessage">Payload type.</typeparam>
    /// <param name="message">Payload.</param>
    /// <param name="payload">Serialized payload.</param>
    /// <param name="messageId">Requested id.</param>
    /// <param name="correlationId">Requested correlation id.</param>
    /// <param name="headers">Requested headers.</param>
    /// <param name="filters">Publish filters.</param>
    /// <param name="clock">Clock for <c>sent_at</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The envelope, its bytes, and the broker user id.</returns>
    public static async ValueTask<(V1.Envelope Envelope, byte[] Body, string? PublisherId)> CreateAsync<TMessage>(
        TMessage message,
        byte[] payload,
        Guid? messageId,
        string? correlationId,
        IReadOnlyDictionary<string, string>? headers,
        IReadOnlyList<IPublishFilter> filters,
        TimeProvider clock,
        CancellationToken cancellationToken)
        where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(filters);
        ArgumentNullException.ThrowIfNull(clock);

        using var activity = BusActivitySource.Source.StartActivity("Gocgun.Bus.Publish", ActivityKind.Producer);
        var id = messageId ?? Guid.CreateVersion7();
        var outgoing = new OutgoingMessage
        {
            MessageId = id,
            TypeUrl = MapMessageType.TypeUrl<TMessage>(),
            CorrelationId = correlationId,
        };
        if (headers is not null)
        {
            foreach (var pair in headers)
            {
                outgoing.Headers[pair.Key] = pair.Value;
            }
        }

        var traceparent = activity?.Id;
        if (!string.IsNullOrEmpty(traceparent))
        {
            outgoing.Headers["traceparent"] = traceparent;
        }

        foreach (var filter in filters)
        {
            await filter.OnPublishAsync(outgoing, cancellationToken).ConfigureAwait(false);
        }

        activity?.SetTag("messaging.message.id", id.ToString("D"));
        activity?.SetTag("messaging.message.type", outgoing.TypeUrl);

        var envelope = new V1.Envelope
        {
            MessageId = id.ToString("D"),
            CorrelationId = outgoing.CorrelationId ?? string.Empty,
            Traceparent = outgoing.Headers.TryGetValue("traceparent", out var headerTrace) ? headerTrace : traceparent ?? string.Empty,
            SentAt = clock.GetUtcNow().UtcDateTime,
            Payload = new V1.EnvelopePayload { TypeUrl = outgoing.TypeUrl, Value = payload },
        };
        foreach (var pair in outgoing.Headers)
        {
            envelope.Headers[pair.Key] = pair.Value;
        }

        var body = ProtoCodec.Serialize(envelope);
        var roundTrip = ProtoCodec.Deserialize<V1.Envelope>(body);
        return (roundTrip, body, outgoing.PublisherId);
    }

    /// <summary>
    /// Gets the protobuf full name from a type URL.
    /// </summary>
    /// <param name="typeUrl">Any type URL.</param>
    /// <returns>The wire name.</returns>
    public static string WireName(string typeUrl)
    {
        ArgumentNullException.ThrowIfNull(typeUrl);
        return typeUrl.StartsWith(MapMessageType.TypeUrlPrefix, StringComparison.Ordinal) ? typeUrl[MapMessageType.TypeUrlPrefix.Length..] : typeUrl;
    }

    /// <summary>
    /// Builds the durable queue name for a handler.
    /// </summary>
    /// <param name="options">Broker options.</param>
    /// <param name="binding">Handler binding.</param>
    /// <returns>The queue name.</returns>
    public static string QueueName(RabbitMqOptions options, IHandlerBinding binding)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(binding);
        return options.ServiceName + "." + binding.HandlerName;
    }

    /// <summary>
    /// Reads a payload type URL when the body is an envelope.
    /// </summary>
    /// <param name="body">Raw bytes.</param>
    /// <returns>The type URL, or null when the body is not an envelope.</returns>
    public static string? TryTypeUrl(byte[] body)
    {
        return ProtoCodec.TryDeserialize<V1.Envelope>(body, out var envelope) ? envelope?.Payload?.TypeUrl : null;
    }

    /// <summary>
    /// Maps an envelope to handler context.
    /// </summary>
    /// <param name="envelope">Envelope.</param>
    /// <param name="publisherId">Broker user id.</param>
    /// <param name="deliveryCount">Attempt number.</param>
    /// <returns>The context.</returns>
    public static MessageContext ToContext(V1.Envelope envelope, string? publisherId, int deliveryCount)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var id = Guid.TryParse(envelope.MessageId, out var parsed) ? parsed : Guid.Empty;
        var sent = envelope.SentAt is { } at
            ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc))
            : DateTimeOffset.UnixEpoch;
        var headers = new Dictionary<string, string>(envelope.Headers.Count, StringComparer.Ordinal);
        foreach (var pair in envelope.Headers)
        {
            headers[pair.Key] = pair.Value;
        }

        var correlation = string.IsNullOrEmpty(envelope.CorrelationId) ? null : envelope.CorrelationId;
        return new MessageContext(id, correlation, sent, deliveryCount, headers, publisherId);
    }
}
