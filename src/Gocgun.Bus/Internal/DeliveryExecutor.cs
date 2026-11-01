// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus.Internal;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using V1 = Gocgun.Bus.V1;

/// <summary>
/// Shared consume rules for both transports.
/// </summary>
internal static class DeliveryExecutor
{
    /// <summary>
    /// Parses a body, checks headers and size, then runs one binding with retries.
    /// </summary>
    /// <param name="body">Envelope bytes.</param>
    /// <param name="binding">Handler to run, or null when the type is unknown.</param>
    /// <param name="publisherId">Broker user id. Envelope headers cannot set this.</param>
    /// <param name="services">Root provider.</param>
    /// <param name="options">Retry and size limits.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The delivery result.</returns>
    public static async ValueTask<DeliveryResult> ExecuteAsync(
        ReadOnlyMemory<byte> body,
        IHandlerBinding? binding,
        string? publisherId,
        IServiceProvider services,
        BusOptions options,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        if (body.Length > options.MaxPayloadBytes)
        {
            return Dead("payload-too-large", null, null, null, logger);
        }

        V1.Envelope envelope;
        try
        {
            envelope = ProtoCodec.Deserialize<V1.Envelope>(body);
        }
        catch (InvalidDataException)
        {
            return Dead("invalid-envelope", null, null, null, logger);
        }

        var headers = new Dictionary<string, string>(envelope.Headers.Count, StringComparer.Ordinal);
        foreach (var pair in envelope.Headers)
        {
            headers[pair.Key] = pair.Value;
        }

        if (!HeaderRules.IsSafe(headers, options))
        {
            return Dead("invalid-headers", envelope.MessageId, envelope.Payload?.TypeUrl, null, logger);
        }

        if (binding is null || envelope.Payload is null || !string.Equals(binding.TypeUrl, envelope.Payload.TypeUrl, StringComparison.Ordinal))
        {
            return Dead("unknown-type", envelope.MessageId, envelope.Payload?.TypeUrl, null, logger);
        }

        return await RunAttempts(binding, envelope, publisherId, services, options, logger, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<DeliveryResult> RunAttempts(
        IHandlerBinding binding,
        V1.Envelope envelope,
        string? publisherId,
        IServiceProvider services,
        BusOptions options,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var attempts = options.MaxImmediateRetries < 1 ? 1 : options.MaxImmediateRetries;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            var result = await OneAttempt(binding, envelope, publisherId, services, attempt, attempts, logger, cancellationToken).ConfigureAwait(false);
            if (result is not null)
            {
                return result.Value;
            }
        }

        return Dead("retries-exhausted", envelope.MessageId, envelope.Payload?.TypeUrl, null, logger);
    }

    private static async ValueTask<DeliveryResult?> OneAttempt(
        IHandlerBinding binding,
        V1.Envelope envelope,
        string? publisherId,
        IServiceProvider services,
        int attempt,
        int attempts,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var scope = services.CreateAsyncScope();
            try
            {
                var decision = await binding
                    .DispatchAsync(scope.ServiceProvider, envelope, publisherId, attempt, cancellationToken)
                    .ConfigureAwait(false);
                if (decision == ConsumeDecision.DeadLetter)
                {
                    return Dead("filter", envelope.MessageId, envelope.Payload?.TypeUrl, null, logger);
                }

                return new DeliveryResult(false, "ack", null, envelope.MessageId, envelope.Payload?.TypeUrl);
            }
            finally
            {
                await scope.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (InvalidDataException)
        {
            return Dead("invalid-payload", envelope.MessageId, envelope.Payload?.TypeUrl, null, logger);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            BusLog.AttemptFailed(logger, envelope.MessageId, attempt, ex.GetType().FullName);
            if (attempt == attempts)
            {
                return Dead("retries-exhausted", envelope.MessageId, envelope.Payload?.TypeUrl, ex.GetType().FullName, logger);
            }

            return null;
        }
    }

    private static DeliveryResult Dead(
        string reason,
        string? messageId,
        string? typeUrl,
        string? exceptionType,
        ILogger logger)
    {
        BusLog.DeadLetter(logger, messageId, reason, typeUrl, exceptionType);
        return new DeliveryResult(true, reason, exceptionType, messageId, typeUrl);
    }
}
