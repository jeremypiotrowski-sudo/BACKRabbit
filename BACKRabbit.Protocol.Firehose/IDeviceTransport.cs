using System;
using System.Threading;
using System.Threading.Tasks;

namespace BACKRabbit.Protocol.Firehose;

public interface IDeviceTransport
{
    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync();
    Task SendPacketAsync(SaharaPacket packet, CancellationToken ct = default);
    Task<SaharaPacket> ReceivePacketAsync(CancellationToken ct = default);
    Task SendRawAsync(byte[] data, CancellationToken ct = default);
    Task<byte[]> ReceiveRawAsync(int maxLength = 65536, CancellationToken ct = default);
    bool IsConnected { get; }
    string DevicePath { get; }
}
