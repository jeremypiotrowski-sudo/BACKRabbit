using System;
using System.Buffers.Binary;
using System.Text;

namespace BACKRabbit.Protocol.Firehose;

public class SaharaChipInfo
{
    public uint Version { get; init; }
    public uint MinVersion { get; init; }
    public uint MaxPacketSize { get; init; }
    public SaharaMode Mode { get; init; }
    public uint MsmId { get; init; }
    public byte[] PkHash { get; init; } = Array.Empty<byte>();
    public bool IsFused => PkHash is { Length: > 0 } && !IsAllZero(PkHash);
    public string? SerialNumber { get; init; }

    public static SaharaChipInfo FromHelloRequest(SaharaPacket packet)
    {
        if (packet.Command != (uint)SaharaCommand.HelloReq)
            throw new SaharaProtocolException($"Expected HELLO_REQ, got 0x{packet.Command:X2}");

        var p = packet.Payload.Span;
        uint version = BinaryPrimitives.ReadUInt32LittleEndian(p[0..4]);
        uint minVer = BinaryPrimitives.ReadUInt32LittleEndian(p[4..8]);
        uint maxPkt = BinaryPrimitives.ReadUInt32LittleEndian(p[8..12]);
        var mode = (SaharaMode)BinaryPrimitives.ReadUInt32LittleEndian(p[12..16]);

        uint msmId = 0;
        byte[] pkHash = Array.Empty<byte>();
        string? serial = null;

        int offset = 16;
        if (p.Length >= offset + 4) { msmId = BinaryPrimitives.ReadUInt32LittleEndian(p[offset..(offset+4)]); offset += 4; }
        if (p.Length >= offset + 8) { pkHash = p.Slice(offset, 8).ToArray(); offset += 8; }
        if (p.Length > offset) { int n = p[offset..].IndexOf((byte)0); if (n >= 0) serial = Encoding.ASCII.GetString(p.Slice(offset, n)); }

        return new SaharaChipInfo { Version = version, MinVersion = minVer, MaxPacketSize = maxPkt, Mode = mode, MsmId = msmId, PkHash = pkHash, SerialNumber = serial };
    }

    public byte[] BuildHelloResponse(uint hostVersion = 2, SaharaMode responseMode = SaharaMode.ImageTxPending)
    {
        var buf = new byte[48];
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0, 4), hostVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(4, 4), MinVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(8, 4), MaxPacketSize);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(12, 4), (uint)responseMode);
        return buf;
    }

    private static bool IsAllZero(byte[] data) { foreach (var b in data) if (b != 0) return false; return true; }
    public override string ToString() => $"ChipInfo(Ver={Version}, MSM=0x{MsmId:X8}, Fused={IsFused})";
}
