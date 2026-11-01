// Copyright (c) Gocgun. All rights reserved.

namespace Gocgun.Bus.Tests;

using Gocgun.Bus.Internal;
using Gocgun.Bus.Tests.Messages;
using Gocgun.Bus.V1;

/// <summary>
/// Envelope bytes stay stable and keep unknown fields.
/// </summary>
public sealed class EnvelopeTests
{
    /// <summary>
    /// A payload round-trips through the envelope.
    /// </summary>
    [Fact]
    public void RoundTripKeepsPayload()
    {
        var envelope = new Envelope
        {
            MessageId = "6f0b1e0a-1a2b-4c3d-9e8f-112233445566",
            CorrelationId = "corr",
            SentAt = new DateTime(2026, 10, 24, 12, 30, 15, 250, DateTimeKind.Utc),
            Payload = new EnvelopePayload { TypeUrl = MapMessageType.TypeUrl<Ping>(), Value = ProtoCodec.Serialize(new Ping { Text = "hi" }) },
        };
        envelope.Headers["source"] = "test";
        var parsed = ProtoCodec.Deserialize<Envelope>(ProtoCodec.Serialize(envelope));
        Assert.NotNull(parsed.Payload);
        Assert.Equal("hi", ProtoCodec.Deserialize<Ping>(parsed.Payload.Value).Text);
        Assert.Equal("test", parsed.Headers["source"]);
        Assert.Equal(envelope.SentAt, parsed.SentAt);
        Assert.Equal("type.googleapis.com/gocgun.bus.tests.v1.Ping", parsed.Payload.TypeUrl);
        Assert.Equal("gocgun.bus.tests.v1.Ping", MapMessageType.WireName<Ping>());
    }

    /// <summary>
    /// The envelope keeps the proto3 layout of the former generated message: field numbers, wire types,
    /// Timestamp for <c>sent_at</c>, map entries for headers, and the Any layout for the payload.
    /// </summary>
    [Fact]
    public void WireLayoutMatchesProto3Envelope()
    {
        byte[] value = [0x08, 0x01];
        byte[] expected =
        [
            0x0A, 0x01, (byte)'m', // 1: message_id
            0x2A, 0x02, 0x08, 0x01, // 5: sent_at { seconds: 1 }
            0x32, 0x06, 0x0A, 0x01, (byte)'k', 0x12, 0x01, (byte)'v', // 6: headers entry
            0x3A, 0x07, 0x0A, 0x01, (byte)'t', 0x12, 0x02, 0x08, 0x01, // 7: payload { type_url, value }
        ];
        var parsed = ProtoCodec.Deserialize<Envelope>(expected);
        Assert.Equal("m", parsed.MessageId);
        Assert.Equal(DateTime.UnixEpoch.AddSeconds(1), parsed.SentAt);
        Assert.Equal("v", parsed.Headers["k"]);
        Assert.Equal("t", parsed.Payload?.TypeUrl);
        Assert.Equal(value, parsed.Payload?.Value);
    }

    /// <summary>
    /// Unknown field numbers survive a parse and write.
    /// </summary>
    [Fact]
    public void UnknownFieldsArePreserved()
    {
        var known = ProtoCodec.Serialize(new Envelope { MessageId = "m" });
        var extra = new byte[known.Length + 3];
        known.CopyTo(extra, 0);
        extra[^3] = 0xA0;
        extra[^2] = 0x01;
        extra[^1] = 0x01;
        var parsed = ProtoCodec.Deserialize<Envelope>(extra);
        Assert.Equal("m", parsed.MessageId);
        Assert.True(ProtoCodec.Serialize(parsed).Length > known.Length);
    }

    /// <summary>
    /// Malformed bytes surface as <see cref="InvalidDataException"/>, never as a protobuf-net exception.
    /// </summary>
    [Fact]
    public void MalformedBytesThrowInvalidData()
    {
        Assert.Throws<InvalidDataException>(() => ProtoCodec.Deserialize<Ping>("not-protobuf"u8.ToArray()));
        byte[] truncated = [0x0A, 0x05, 0x01];
        Assert.False(ProtoCodec.TryDeserialize<Envelope>(truncated, out _));
    }

    /// <summary>
    /// A message type without a contract name cannot be mapped, so wire names never follow C# renames.
    /// </summary>
    [Fact]
    public void ContractNameIsRequired()
    {
        Assert.Throws<InvalidOperationException>(() => MapMessageType.WireName(typeof(string)));
        Assert.Throws<InvalidOperationException>(() => MapMessageType.WireName(typeof(UnnamedContract)));
    }

    /// <summary>
    /// Contract without a name.
    /// </summary>
    [ProtoBuf.ProtoContract]
    private sealed class UnnamedContract
    {
        /// <summary>Gets or sets a value.</summary>
        [ProtoBuf.ProtoMember(1)]
        public int Value { get; set; }
    }
}
