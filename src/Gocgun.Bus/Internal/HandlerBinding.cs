// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus.Internal;

using Microsoft.Extensions.DependencyInjection;
using V1 = Gocgun.Bus.V1;

/// <summary>
/// Closed binding for one message and handler pair.
/// </summary>
/// <typeparam name="TMessage">Protobuf message.</typeparam>
internal sealed class HandlerBinding<TMessage> : IHandlerBinding
    where TMessage : class
{
    private readonly Type handlerType;

    /// <summary>
    /// Initializes a new instance of the <see cref="HandlerBinding{TMessage}"/> class.
    /// </summary>
    /// <param name="busName">Name of the bus that owns the handler. Filters and the idempotency store are keyed by it.</param>
    /// <param name="handlerType">Handler implementation.</param>
    /// <param name="isCommand">True when the message is an integration command.</param>
    public HandlerBinding(string busName, Type handlerType, bool isCommand)
    {
        ArgumentNullException.ThrowIfNull(busName);
        ArgumentNullException.ThrowIfNull(handlerType);
        this.BusName = busName;
        this.handlerType = handlerType;
        this.IsCommand = isCommand;
        this.WireName = MapMessageType.WireName<TMessage>();
        this.TypeUrl = MapMessageType.TypeUrl<TMessage>();
        this.HandlerName = (handlerType.FullName ?? handlerType.Name).Replace('+', '.');
        this.ChannelKey = this.TypeUrl + "|" + this.HandlerName;
    }

    /// <inheritdoc />
    public string BusName { get; }

    /// <inheritdoc />
    public string TypeUrl { get; }

    /// <inheritdoc />
    public string WireName { get; }

    /// <inheritdoc />
    public string HandlerName { get; }

    /// <inheritdoc />
    public string ChannelKey { get; }

    /// <inheritdoc />
    public bool IsCommand { get; }

    /// <inheritdoc />
    public async ValueTask<ConsumeDecision> DispatchAsync(
        IServiceProvider scope,
        V1.Envelope envelope,
        string? publisherId,
        int deliveryCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.Payload is null || !string.Equals(envelope.Payload.TypeUrl, this.TypeUrl, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Payload type URL does not match the handler.");
        }

        var message = ProtoCodec.Deserialize<TMessage>(envelope.Payload.Value);
        var context = EnvelopeFactory.ToContext(envelope, publisherId, deliveryCount);
        var filters = scope.GetKeyedServices<IConsumeFilter>(this.BusName).ToArray();
        if (filters.Length == 0)
        {
            await this.RunCoreAsync(scope, message, context, cancellationToken).ConfigureAwait(false);
            return ConsumeDecision.Continue;
        }

        var handlerRan = false;
        var decision = await ChainAsync(0).ConfigureAwait(false);
        if (decision == ConsumeDecision.Continue && !handlerRan)
        {
            // Continue means "run the handler when it has not run yet".
            await this.RunCoreAsync(scope, message, context, cancellationToken).ConfigureAwait(false);
        }

        return decision;

        async ValueTask<ConsumeDecision> ChainAsync(int index)
        {
            if (index >= filters.Length)
            {
                handlerRan = true;
                await this.RunCoreAsync(scope, message, context, cancellationToken).ConfigureAwait(false);
                return ConsumeDecision.Continue;
            }

            var inner = ConsumeDecision.Continue;
            var own = await filters[index]
                .OnConsumeAsync(
                    context,
                    async () =>
                    {
                        inner = await ChainAsync(index + 1).ConfigureAwait(false);
                        return inner;
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            // An outer filter that returns Continue does not erase an inner filter's decision.
            return own == ConsumeDecision.Continue ? inner : own;
        }
    }

    private async ValueTask RunCoreAsync(
        IServiceProvider scope,
        TMessage message,
        MessageContext context,
        CancellationToken cancellationToken)
    {
        var store = scope.GetKeyedService<IIdempotencyStore>(this.BusName);
        if (store is not null)
        {
            var begin = await store.TryBeginAsync(context.MessageId, this.HandlerName, cancellationToken).ConfigureAwait(false);
            if (!begin)
            {
                return;
            }
        }

        var handler = (IMessageHandler<TMessage>)scope.GetRequiredService(this.handlerType);
        await handler.HandleAsync(message, context, cancellationToken).ConfigureAwait(false);
        if (store is not null)
        {
            await store.CompleteAsync(context.MessageId, this.HandlerName, cancellationToken).ConfigureAwait(false);
        }
    }
}
