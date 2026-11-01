// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus.Internal;

using System.Threading.Channels;

/// <summary>
/// Registration-time checks for bus options.
/// </summary>
internal static class OptionGuards
{
    /// <summary>
    /// Throws when shared limits cannot be honored.
    /// </summary>
    /// <param name="options">Shared options.</param>
    public static void ThrowIfBusInvalid(BusOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxImmediateRetries < 1)
        {
            throw new InvalidOperationException("MaxImmediateRetries must be at least 1.");
        }

        if (options.MaxConcurrency < 1)
        {
            throw new InvalidOperationException("MaxConcurrency must be at least 1.");
        }

        if (options.MaxPayloadBytes < 1 || options.MaxHeaderCount < 1 || options.MaxHeaderValueLength < 1)
        {
            throw new InvalidOperationException("Payload and header limits must be at least 1.");
        }
    }

    /// <summary>
    /// Throws when the in-memory channel would drop messages without an explicit opt-in.
    /// </summary>
    /// <param name="options">Channel options.</param>
    public static void ThrowIfMemoryInvalid(InMemoryBusOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Capacity < 1)
        {
            throw new InvalidOperationException("In-memory capacity must be at least 1.");
        }

        if (options.DrainTimeout < TimeSpan.Zero)
        {
            throw new InvalidOperationException("DrainTimeout cannot be negative.");
        }

        var lossy = options.FullMode is BoundedChannelFullMode.DropWrite
            or BoundedChannelFullMode.DropOldest
            or BoundedChannelFullMode.DropNewest;
        if (lossy && !options.AllowLossyFullMode)
        {
            throw new InvalidOperationException("Lossy full mode requires AllowLossyFullMode.");
        }
    }
}
