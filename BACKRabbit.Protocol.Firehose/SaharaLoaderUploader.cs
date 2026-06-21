using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BACKRabbit.Protocol.Firehose;

public class SaharaLoaderUploader
{
    private readonly IDeviceTransport _transport;
    private readonly SaharaStateMachine _stateMachine;

    public SaharaLoaderUploader(IDeviceTransport transport, SaharaStateMachine stateMachine)
    {
        _transport = transport;
        _stateMachine = stateMachine;
    }

    public async Task UploadAsync(string loaderPath, CancellationToken ct = default)
    {
        if (!File.Exists(loaderPath)) throw new FileNotFoundException($"Loader not found: {loaderPath}");
        var loaderData = await File.ReadAllBytesAsync(loaderPath, ct);
        await UploadAsync(loaderData, ct);
    }

    public async Task UploadAsync(byte[] loaderData, CancellationToken ct = default)
    {
        _stateMachine.TransitionTo(SaharaState.ImageUploading);
        int imageId = 0;

        while (true)
        {
            var packet = await _transport.ReceivePacketAsync(ct);
            if (packet.Command == (uint)SaharaCommand.EndOfImageTransfer)
            { _stateMachine.TransitionTo(SaharaState.ImageUploadComplete); return; }
            if (packet.Command != (uint)SaharaCommand.ReadData)
                throw new SaharaProtocolException($"Expected READ_DATA, got 0x{packet.Command:X2}");

            var payload = packet.Payload.ToArray();
            if (payload.Length < 12) throw new SaharaProtocolException("READ_DATA payload too short");
            int reqOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(4, 4));
            int reqLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(8, 4));

            int available = loaderData.Length - reqOffset;
            if (available <= 0) { await SendEndOfTransfer(imageId, ct); _stateMachine.TransitionTo(SaharaState.ImageUploadComplete); return; }

            int sendLen = Math.Min(reqLength, available);
            var chunk = new byte[sendLen];
            Array.Copy(loaderData, reqOffset, chunk, 0, sendLen);
            await _transport.SendPacketAsync(new SaharaPacket((uint)SaharaCommand.ReadData, chunk), ct);
        }
    }

    private async Task SendEndOfTransfer(int imageId, CancellationToken ct)
    {
        var payload = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), (uint)imageId);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 0);
        await _transport.SendPacketAsync(new SaharaPacket((uint)SaharaCommand.EndOfImageTransfer, payload), ct);
    }
}
