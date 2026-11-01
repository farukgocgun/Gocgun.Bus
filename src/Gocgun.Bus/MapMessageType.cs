// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus;

using System.Reflection;
using ProtoBuf;

/// <summary>
/// Maps a protobuf-net contract to its wire name. The wire name is the <see cref="ProtoContractAttribute.Name"/>,
/// so renaming or moving the C# type does not change routing.
/// </summary>
public static class MapMessageType
{
    /// <summary>
    /// Type URL prefix, the same one <c>google.protobuf.Any</c> uses.
    /// </summary>
    public const string TypeUrlPrefix = "type.googleapis.com/";

    /// <summary>
    /// Gets the type URL for the message.
    /// </summary>
    /// <typeparam name="TMessage">protobuf-net contract.</typeparam>
    /// <returns>The type URL.</returns>
    public static string TypeUrl<TMessage>()
        where TMessage : class
    {
        return TypeUrlPrefix + WireName(typeof(TMessage));
    }

    /// <summary>
    /// Gets the wire name used as the RabbitMQ routing key.
    /// </summary>
    /// <typeparam name="TMessage">protobuf-net contract.</typeparam>
    /// <returns>The wire name.</returns>
    public static string WireName<TMessage>()
        where TMessage : class
    {
        return WireName(typeof(TMessage));
    }

    /// <summary>
    /// Gets the wire name of a contract type.
    /// </summary>
    /// <param name="type">Type marked with <see cref="ProtoContractAttribute"/> and a non-empty name.</param>
    /// <returns>The wire name without a leading dot.</returns>
    /// <exception cref="InvalidOperationException">The type has no contract or no contract name.</exception>
    public static string WireName(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var contract = type.GetCustomAttribute<ProtoContractAttribute>(inherit: false)
            ?? throw new InvalidOperationException("The message type must be marked with [ProtoContract].");
        var name = contract.Name?.TrimStart('.');
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("The message contract needs a stable wire name: [ProtoContract(Name = \"package.v1.Message\")].");
        }

        return name;
    }
}
