// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus.Internal;

using System.Buffers;
using ProtoBuf;

/// <summary>
/// protobuf-net reads and writes. Malformed bytes always surface as <see cref="InvalidDataException"/>.
/// </summary>
internal static class ProtoCodec
{
    /// <summary>
    /// Serializes a contract to a new array.
    /// </summary>
    /// <typeparam name="T">protobuf-net contract.</typeparam>
    /// <param name="value">Value to write.</param>
    /// <returns>The protobuf bytes.</returns>
    public static byte[] Serialize<T>(T value)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        var writer = new ArrayBufferWriter<byte>();
        Serializer.Serialize(writer, value);
        return writer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Parses a contract.
    /// </summary>
    /// <typeparam name="T">protobuf-net contract.</typeparam>
    /// <param name="bytes">Protobuf bytes.</param>
    /// <returns>The parsed value.</returns>
    /// <exception cref="InvalidDataException">The bytes are not a valid message.</exception>
    public static T Deserialize<T>(ReadOnlyMemory<byte> bytes)
        where T : class
    {
        try
        {
            return Serializer.Deserialize<T>(bytes);
        }
        catch (Exception ex) when (ex is ProtoException or EndOfStreamException or OverflowException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            throw new InvalidDataException("The bytes are not a valid protobuf message.", ex);
        }
    }

    /// <summary>
    /// Parses a contract without throwing.
    /// </summary>
    /// <typeparam name="T">protobuf-net contract.</typeparam>
    /// <param name="bytes">Protobuf bytes.</param>
    /// <param name="value">The parsed value, or null.</param>
    /// <returns>True when the bytes parsed.</returns>
    public static bool TryDeserialize<T>(ReadOnlyMemory<byte> bytes, out T? value)
        where T : class
    {
        try
        {
            value = Deserialize<T>(bytes);
            return true;
        }
        catch (InvalidDataException)
        {
            value = null;
            return false;
        }
    }
}
