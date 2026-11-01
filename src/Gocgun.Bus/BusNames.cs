// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus;

/// <summary>
/// Bus name rules.
/// </summary>
public static class BusNames
{
    /// <summary>
    /// Name of the bus registered by <see cref="BusServiceCollectionExtensions.AddGocgunBus(Microsoft.Extensions.DependencyInjection.IServiceCollection, Action{BusBuilder})"/>.
    /// Only this bus is also resolvable as an unkeyed <see cref="IMessageBus"/>.
    /// </summary>
    public const string Default = "default";

    /// <summary>
    /// Longest accepted bus name.
    /// </summary>
    public const int MaxLength = 64;

    /// <summary>
    /// Throws when a bus name is empty, too long, or has characters outside ASCII letters, digits, '.', '-' and '_'.
    /// The name ends up in connection names and health check names, so it stays plain.
    /// </summary>
    /// <param name="name">Bus name.</param>
    /// <exception cref="ArgumentException">The name is not valid.</exception>
    public static void ThrowIfInvalid(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > MaxLength)
        {
            throw new ArgumentException("Bus name is longer than " + MaxLength.ToString(System.Globalization.CultureInfo.InvariantCulture) + " characters.", nameof(name));
        }

        if (!name.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_'))
        {
            throw new ArgumentException("Bus name may contain only ASCII letters, digits, '.', '-' and '_'.", nameof(name));
        }
    }
}
