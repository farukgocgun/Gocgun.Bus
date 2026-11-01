// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus.Internal;

/// <summary>
/// Rejects header shapes that can inject extra lines or oversized metadata.
/// </summary>
internal static class HeaderRules
{
    /// <summary>
    /// Throws when caller-supplied headers are not safe to copy.
    /// </summary>
    /// <param name="headers">Caller headers.</param>
    /// <param name="options">Limits.</param>
    public static void ThrowIfUnsafe(IReadOnlyDictionary<string, string>? headers, BusOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (headers is null)
        {
            return;
        }

        if (!IsSafe(headers, options))
        {
            throw new ArgumentException("Headers are missing, oversized, or contain control characters.", nameof(headers));
        }
    }

    /// <summary>
    /// Returns false when a received header map must be dead-lettered.
    /// </summary>
    /// <param name="headers">Envelope headers.</param>
    /// <param name="options">Limits.</param>
    /// <returns>True when every header is safe.</returns>
    public static bool IsSafe(IReadOnlyDictionary<string, string> headers, BusOptions options)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(options);
        if (headers.Count > options.MaxHeaderCount)
        {
            return false;
        }

        foreach (var pair in headers)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > 128 || pair.Value.Length > options.MaxHeaderValueLength)
            {
                return false;
            }

            if (HasControl(pair.Key) || HasControl(pair.Value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasControl(string text)
    {
        return text.Any(character => character is '\r' or '\n' or '\0' or ':');
    }
}
