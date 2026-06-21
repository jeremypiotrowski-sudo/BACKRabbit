using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace BACKRabbit.Protocol.Firehose;

public class FirehoseException : Exception
{
    public FirehoseException(string message) : base(message) { }
    public FirehoseException(string message, Exception inner) : base(message, inner) { }
}

public class FirehoseClient
{
    private readonly IDeviceTransport _transport;
    private readonly SaharaStateMachine _stateMachine;
    private readonly SaharaLoaderUploader _uploader;

    public FirehoseClient(IDeviceTransport transport, SaharaStateMachine stateMachine)
    {
        _transport = transport;
        _stateMachine = stateMachine;
        _uploader = new SaharaLoaderUploader(transport, stateMachine);
    }

    public async Task InitializeAsync(string loaderPath, CancellationToken ct = default)
    {
        var helloReq = await _transport.ReceivePacketAsync(ct);
        if (helloReq.Command != (uint)SaharaCommand.HelloReq)
            throw new SaharaProtocolException("Device did not send HELLO_REQ");

        var chipInfo = SaharaChipInfo.FromHelloRequest(helloReq);
        _stateMachine.SetChipInfo(chipInfo);

        var helloRsp = new SaharaPacket((uint)SaharaCommand.HelloRsp, chipInfo.BuildHelloResponse());
        await _transport.SendPacketAsync(helloRsp, ct);
        _stateMachine.TransitionTo(SaharaState.HelloSent);

        await _uploader.UploadAsync(loaderPath, ct);
        _stateMachine.TransitionTo(SaharaState.CommandMode);
    }

    public async Task<XDocument> SendCommandAsync(string xmlCommand, CancellationToken ct = default)
    {
        var xmlBytes = Encoding.UTF8.GetBytes(xmlCommand);
        await _transport.SendRawAsync(xmlBytes, ct);
        var responseBytes = await _transport.ReceiveRawAsync(65536, ct);
        var responseXml = Encoding.UTF8.GetString(responseBytes).Trim('\0', '\r', '\n', ' ');

        if (responseXml.StartsWith("<?xml") || responseXml.StartsWith("<"))
        {
            try { return XDocument.Parse(responseXml); }
            catch { return new XDocument(new XElement("log", new XAttribute("value", responseXml))); }
        }
        return new XDocument(new XElement("response", new XAttribute("value", "RAW"), responseXml));
    }

    public async Task<bool> NopAsync(CancellationToken ct = default)
    {
        var cmd = "<?xml version=\"1.0\" encoding=\"UTF-8\" ?><data><nop /></data>";
        var response = await SendCommandAsync(cmd, ct);
        return response.Root?.Attribute("value")?.Value == "ACK";
    }

    public async Task<XDocument> PrintGptAsync(int lun = 0, CancellationToken ct = default)
    {
        var cmd = $@"<?xml version=""1.0"" encoding=""UTF-8"" ?><data><gpt num_partition_entries=""0"" physical_partition_number=""{lun}"" /></data>";
        return await SendCommandAsync(cmd, ct);
    }

    public async Task ResetAsync(string mode = "system", CancellationToken ct = default)
    {
        var cmd = $@"<?xml version=""1.0"" encoding=""UTF-8"" ?><data><power value=""{mode}"" /></data>";
        await SendCommandAsync(cmd, ct);
    }

    public async Task<byte[]> PeekAsync(uint address, uint size, CancellationToken ct = default)
    {
        var cmd = $@"<?xml version=""1.0"" encoding=""UTF-8"" ?><data><peek address=""{address}"" size_in_bytes=""{size}"" /></data>";
        var response = await SendCommandAsync(cmd, ct);
        if (response.Root?.Attribute("value")?.Value != "ACK")
            throw new FirehoseException($"Peek failed");
        return await _transport.ReceiveRawAsync((int)size, ct);
    }

    public async Task DisconnectAsync()
    {
        await _transport.DisconnectAsync();
        _stateMachine.TransitionTo(SaharaState.Disconnected);
    }
}
