using System;
using System.Buffers.Binary;

namespace BACKRabbit.Protocol.Firehose;

/// <summary>
/// Sahara protocol packet structure.
/// All Sahara packets have a fixed 8-byte header followed by command-specific payload.
/// </summary>
public readonly struct SaharaPacket
{
    public uint Command { get; }
    public uint Length { get; }
    public ReadOnlyMemory<byte> Payload { get; }

    public const int HeaderSize = 8;
    public const int MaxPacketSize = 0x40000; // 256KB

    public SaharaPacket(uint command, ReadOnlyMemory<byte> payload)
    {
        Command = command;
        Length = (uint)(HeaderSize + payload.Length);
        Payload = payload;
    }

    /// <summary>Parse a packet from raw bytes received from the device.</summary>
    public static SaharaPacket Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
            throw new SaharaProtocolException($"Packet too short: {data.Length} bytes (min {HeaderSize})");

        uint command = BinaryPrimitives.ReadUInt32LittleEndian(data[0..4]);
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(data[4..8]);

        if (length > MaxPacketSize)
            throw new SaharaProtocolException($"Packet length {length} exceeds max {MaxPacketSize}");

        int payloadLen = (int)length - HeaderSize;
        if (data.Length < length)
            throw new SaharaProtocolException($"Incomplete packet: got {data.Length}, expected {length}");

        var payload = data.Slice(HeaderSize, payloadLen).ToArray();
        return new SaharaPacket(command, payload);
    }

    /// <summary>Serialize to bytes for sending to device.</summary>
    public byte[] Serialize()
    {
        var buf = new byte[Length];
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0, 4), Command);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(4, 4), Length);
        Payload.Span.CopyTo(buf.AsSpan(HeaderSize));
        return buf;
    }

    public override string ToString() =>
        $"SaharaPacket(Cmd=0x{Command:X2}, Len={Length})";
}

public class SaharaProtocolException : Exception
{
    public SaharaProtocolException(string message) : base(message) { }
    public SaharaProtocolException(string message, Exception inner) : base(message, inner) { }
}