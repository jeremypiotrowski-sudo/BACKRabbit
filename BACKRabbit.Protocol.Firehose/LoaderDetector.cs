using System;
using System.Threading;
using System.Threading.Tasks;

namespace BACKRabbit.Protocol.Firehose;

public class DetectionResult
{
    public SaharaChipInfo? ChipInfo { get; init; }
    public LoaderEntry? Loader { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public override string ToString() => Success ? $"DetectionResult(OK: {ChipInfo})" : $"DetectionResult(FAIL: {ErrorMessage})";
}

public class LoaderDetector
{
    private readonly IDeviceTransport _transport;
    private readonly LoaderDatabase _database;

    public LoaderDetector(IDeviceTransport transport, LoaderDatabase database)
    {
        _transport = transport;
        _database = database;
    }

    public async Task<DetectionResult> DetectAsync(CancellationToken ct = default)
    {
        await _transport.ConnectAsync(ct);
        var helloReq = await _transport.ReceivePacketAsync(ct);
        if (helloReq.Command != (uint)SaharaCommand.HelloReq)
            throw new SaharaProtocolException($"Expected HELLO_REQ, got 0x{helloReq.Command:X2}");

        var chipInfo = SaharaChipInfo.FromHelloRequest(helloReq);
        var helloRsp = new SaharaPacket((uint)SaharaCommand.HelloRsp, chipInfo.BuildHelloResponse());
        await _transport.SendPacketAsync(helloRsp, ct);

        var loader = _database.FindLoader(chipInfo);
        return new DetectionResult { ChipInfo = chipInfo, Loader = loader, Success = loader != null };
    }
}
