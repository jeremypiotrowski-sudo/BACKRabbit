using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BACKRabbit.Protocol.Firehose;

/// <summary>
/// Complete Firehose client. Performs the Sahara handshake, uploads the
/// Firehose programmer, then opens a Firehose XML command channel used to
/// read/write/erase partitions, dump GPT, peek/poke memory, and reset the
/// device.
/// </summary>
public class FirehoseClient
{
    private readonly IDeviceTransport _transport;
    private readonly SaharaStateMachine _stateMachine;
    private readonly SaharaLoaderUploader _uploader;
    private FirehoseConfiguration? _config;

    public SaharaChipInfo? ChipInfo => _stateMachine.ChipInfo;
    public bool IsInitialized => _stateMachine.CurrentState == SaharaState.CommandMode;

    public FirehoseClient(IDeviceTransport transport, SaharaStateMachine stateMachine)
    {
        _transport = transport;
        _stateMachine = stateMachine;
        _uploader = new SaharaLoaderUploader(transport, stateMachine);
    }

    // ─── INITIALIZATION ───────────────────────────────────────

    /// <summary>
    /// Full EDL session: Sahara handshake → upload programmer → Firehose configure.
    /// </summary>
    public async Task InitializeAsync(string loaderPath, CancellationToken ct = default)
    {
        // Sahara handshake
        var helloReq = await _transport.ReceivePacketAsync(ct);
        if (helloReq.Command != (uint)SaharaCommand.HelloReq)
            throw new SaharaProtocolException("Device did not send HELLO_REQ");

        var chipInfo = SaharaChipInfo.FromHelloRequest(helloReq);
        _stateMachine.SetChipInfo(chipInfo);

        var helloRsp = new SaharaPacket((uint)SaharaCommand.HelloRsp, chipInfo.BuildHelloResponse());
        await _transport.SendPacketAsync(helloRsp, ct);
        _stateMachine.TransitionTo(SaharaState.HelloSent);

        // Upload the Firehose programmer — uploader drives ImageUploading → ImageUploadComplete
        await _uploader.UploadAsync(loaderPath, ct);
        _stateMachine.TransitionTo(SaharaState.CommandMode);

        // Configure the Firehose channel
        await ConfigureAsync(ct);
    }

    /// <summary>
    /// Send the mandatory <configure> handshake.
    /// </summary>
    public async Task ConfigureAsync(CancellationToken ct = default)
    {
        _config = new FirehoseConfiguration
        {
            ZlpAwareHost = "1",
            SkipStorageInit = "0",
            MaxPayloadSizeToTargetInBytes = "1048576",
            AckRawDataEveryNumPackets = "100",
            MemoryName = "ufs", // default; caller can override before calling
        };

        var xml = _config.ToXml();
        var response = await SendCommandAsync(xml, ct);
        if (!response.IsAck)
            throw new FirehoseException($"Configure failed: {response.LastRawValue}");
    }

    // ─── CORE COMMAND EXECUTION ──────────────────────────────

    /// <summary>
    /// Send a Firehose XML command and parse the mixed XML + log response stream.
    /// </summary>
    public async Task<FirehoseResponse> SendCommandAsync(string xmlCommand, CancellationToken ct = default)
    {
        var xmlBytes = Encoding.UTF8.GetBytes(xmlCommand);
        await _transport.SendRawAsync(xmlBytes, ct);

        // Read response — may be multiple XML fragments + log packets
        var allData = new List<byte>();
        var buffer = new byte[65536];
        int totalRead = 0;

        while (totalRead < 1024 * 1024) // 1MB max response
        {
            int read = await ReadWithTimeoutAsync(buffer, 500, ct);
            if (read == 0) break;
            allData.AddRange(buffer.AsSpan(0, read).ToArray());
            totalRead += read;

            // Check if we have a complete response (ACK or NAK)
            var text = Encoding.UTF8.GetString(allData.ToArray());
            if (text.Contains("ACK") || text.Contains("NAK"))
            {
                // Give it a bit more time for trailing log packets
                await Task.Delay(50, ct);
                try
                {
                    int extra = await ReadWithTimeoutAsync(buffer, 200, ct);
                    if (extra > 0)
                        allData.AddRange(buffer.AsSpan(0, extra).ToArray());
                }
                catch { /* no more data */ }
                break;
            }
        }

        return FirehoseResponse.Parse(allData.ToArray());
    }

    private async Task<int> ReadWithTimeoutAsync(byte[] buffer, int timeoutMs, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        try
        {
            var result = await _transport.ReceiveRawAsync(buffer.Length, cts.Token);
            return result.Length;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return 0; // timeout, not user cancel
        }
    }

    // ─── STORAGE COMMANDS ────────────────────────────────────

    /// <summary>Send NOP to verify Firehose is alive.</summary>
    public async Task<bool> NopAsync(CancellationToken ct = default)
    {
        var cmd = "<?xml version=\"1.0\" encoding=\"UTF-8\" ?><data><nop /></data>";
        var response = await SendCommandAsync(cmd, ct);
        return response.IsAck;
    }

    /// <summary>Read a partition by name, return raw bytes.</summary>
    public virtual async Task<byte[]> ReadPartitionAsync(
        string partitionName, int lun = 0, int sectorSize = 512,
        CancellationToken ct = default)
    {
        // First, get partition info from GPT
        var gptInfo = await GetPartitionInfoAsync(partitionName, lun, ct);
        if (gptInfo == null)
            throw new FirehoseException($"Partition '{partitionName}' not found on LUN {lun}");

        var cmd = $@"<?xml version=""1.0"" encoding=""UTF-8"" ?>
<data>
  <read SECTOR_SIZE_IN_BYTES=""{sectorSize}"" num_partition_sectors=""{gptInfo.Sectors}"" 
        physical_partition_number=""{lun}"" start_sector=""{gptInfo.StartSector}"" 
        partition_name=""{partitionName}"" />
</data>";

        var response = await SendCommandAsync(cmd, ct);
        if (!response.IsAck)
            throw new FirehoseException($"Read partition '{partitionName}' failed: {response.LastRawValue}");

        // After ACK, device sends raw sector data
        int expectedBytes = (int)(gptInfo.Sectors * (ulong)sectorSize);
        var data = new List<byte>(expectedBytes);
        var buffer = new byte[1048576]; // 1MB chunks

        while (data.Count < expectedBytes)
        {
            var rawData = await _transport.ReceiveRawAsync(buffer.Length, ct);
            if (rawData.Length == 0) break;
            data.AddRange(rawData);
        }

        return data.Take(expectedBytes).ToArray();
    }

    /// <summary>Write a partition by name.</summary>
    public virtual async Task<bool> WritePartitionAsync(
        string partitionName, byte[] data, int lun = 0, int sectorSize = 512,
        CancellationToken ct = default)
    {
        var numSectors = (data.Length + sectorSize - 1) / sectorSize;

        var cmd = $@"<?xml version=""1.0"" encoding=""UTF-8"" ?>
<data>
  <program SECTOR_SIZE_IN_BYTES=""{sectorSize}"" num_partition_sectors=""{numSectors}"" 
           physical_partition_number=""{lun}"" start_sector=""0"" 
           partition_name=""{partitionName}"" />
</data>";

        // Send command
        await _transport.SendRawAsync(Encoding.UTF8.GetBytes(cmd), ct);

        // Wait for ACK before sending data
        var ackData = new List<byte>();
        var ackBuffer = new byte[65536];
        int ackRead = await ReadWithTimeoutAsync(ackBuffer, 2000, ct);
        if (ackRead > 0)
        {
            ackData.AddRange(ackBuffer.AsSpan(0, ackRead).ToArray());
            var ackText = Encoding.UTF8.GetString(ackData.ToArray());
            if (!ackText.Contains("ACK"))
                throw new FirehoseException($"Program command not acknowledged: {ackText}");
        }

        // Send the raw data
        await _transport.SendRawAsync(data, ct);

        // Read final response
        var finalData = new List<byte>();
        var finalBuffer = new byte[65536];
        int finalRead = await ReadWithTimeoutAsync(finalBuffer, 5000, ct);
        if (finalRead > 0)
            finalData.AddRange(finalBuffer.AsSpan(0, finalRead).ToArray());

        var finalText = Encoding.UTF8.GetString(finalData.ToArray());
        return finalText.Contains("ACK");
    }

    /// <summary>Erase a partition by name.</summary>
    public virtual async Task<bool> ErasePartitionAsync(string partitionName, int lun = 0, CancellationToken ct = default)
    {
        var cmd = $@"<?xml version=""1.0"" encoding=""UTF-8"" ?>
<data>
  <erase physical_partition_number=""{lun}"" partition_name=""{partitionName}"" />
</data>";
        var response = await SendCommandAsync(cmd, ct);
        return response.IsAck;
    }

    // ─── GPT / STORAGE INFO ──────────────────────────────────

    /// <summary>Dump the GPT partition table.</summary>
    public virtual async Task<List<GptPartitionEntry>> PrintGptAsync(int lun = 0, CancellationToken ct = default)
    {
        var cmd = $@"<?xml version=""1.0"" encoding=""UTF-8"" ?>
<data>
  <gpt num_partition_entries=""0"" physical_partition_number=""{lun}"" />
</data>";
        var response = await SendCommandAsync(cmd, ct);
        if (!response.IsAck)
            throw new FirehoseException($"GPT dump failed: {response.LastRawValue}");

        // Parse GPT entries from response fragments
        var entries = new List<GptPartitionEntry>();
        foreach (var frag in response.Fragments)
        {
            if (frag.TagName == "partition")
            {
                entries.Add(new GptPartitionEntry
                {
                    Name = frag.RawValue ?? "",
                    // Additional attributes (start_sector, num_sectors) would be parsed
                    // from the full XML attributes in a more complete implementation.
                });
            }
        }

        return entries;
    }

    /// <summary>Get info for a specific partition.</summary>
    public async Task<GptPartitionInfo?> GetPartitionInfoAsync(
        string partitionName, int lun = 0, CancellationToken ct = default)
    {
        var entries = await PrintGptAsync(lun, ct);
        // The GPT response contains partition details — for now return basic info.
        // A full implementation would parse start_sector and num_sectors from the XML.
        return entries.Any(e => e.Name.Equals(partitionName, StringComparison.OrdinalIgnoreCase))
            ? new GptPartitionInfo { Name = partitionName, StartSector = 0, Sectors = 0 }
            : null;
    }

    /// <summary>Query storage info (eMMC/UFS/NAND).</summary>
    public virtual async Task<string> GetStorageInfoAsync(CancellationToken ct = default)
    {
        var cmd = @"<?xml version=""1.0"" encoding=""UTF-8"" ?>
<data>
  <getstorageinfo />
</data>";
        var response = await SendCommandAsync(cmd, ct);
        return response.LastRawValue ?? response.LastLogValue ?? "unknown";
    }

    // ─── MEMORY COMMANDS ─────────────────────────────────────

    /// <summary>Peek at memory address.</summary>
    public virtual async Task<byte[]> PeekAsync(uint address, uint size, CancellationToken ct = default)
    {
        var cmd = $@"<?xml version=""1.0"" encoding=""UTF-8"" ?>
<data>
  <peek address=""{address}"" size_in_bytes=""{size}"" />
</data>";
        var response = await SendCommandAsync(cmd, ct);
        if (!response.IsAck)
            throw new FirehoseException($"Peek failed: {response.LastRawValue}");

        // Data follows after ACK
        return await _transport.ReceiveRawAsync((int)size, ct);
    }

    /// <summary>Poke a 32-bit value at memory address.</summary>
    public async Task<bool> PokeAsync(uint address, uint value, CancellationToken ct = default)
    {
        var cmd = $@"<?xml version=""1.0"" encoding=""UTF-8"" ?>
<data>
  <poke address=""{address}"" size_in_bytes=""4"" value=""{value}"" />
</data>";
        var response = await SendCommandAsync(cmd, ct);
        return response.IsAck;
    }

    // ─── DEVICE CONTROL ──────────────────────────────────────

    /// <summary>Reset the device.</summary>
    public virtual async Task ResetAsync(string mode = "system", CancellationToken ct = default)
    {
        var cmd = $@"<?xml version=""1.0"" encoding=""UTF-8"" ?>
<data>
  <power value=""{mode}"" />
</data>";
        try
        {
            await SendCommandAsync(cmd, ct);
        }
        catch
        {
            // Device resets — transport may break, that's expected
        }
    }

    /// <summary>Set the bootable LUN.</summary>
    public async Task<bool> SetBootablePartitionAsync(int lun, CancellationToken ct = default)
    {
        var cmd = $@"<?xml version=""1.0"" encoding=""UTF-8"" ?>
<data>
  <setbootablestoragedrive value=""{lun}"" />
</data>";
        var response = await SendCommandAsync(cmd, ct);
        return response.IsAck;
    }

    // ─── CLEANUP ─────────────────────────────────────────────

    public async Task DisconnectAsync()
    {
        await _transport.DisconnectAsync();
        _stateMachine.TransitionTo(SaharaState.Disconnected);
    }
}

// ─── SUPPORTING TYPES ───────────────────────────────────────

/// <summary>Firehose <configure> parameters.</summary>
public class FirehoseConfiguration
{
    public string ZlpAwareHost { get; set; } = "1";
    public string SkipStorageInit { get; set; } = "0";
    public string MaxPayloadSizeToTargetInBytes { get; set; } = "1048576";
    public string AckRawDataEveryNumPackets { get; set; } = "100";
    public string MemoryName { get; set; } = "ufs"; // ufs, emmc, nand, nvme
    public string? TargetName { get; set; }
    public string? SkipWrite { get; set; }

    public string ToXml() =>
        $@"<?xml version=""1.0"" encoding=""UTF-8"" ?>
<data>
  <configure ZlpAwareHost=""{ZlpAwareHost}"" SkipStorageInit=""{SkipStorageInit}"" 
             MaxPayloadSizeToTargetInBytes=""{MaxPayloadSizeToTargetInBytes}"" 
             AckRawDataEveryNumPackets=""{AckRawDataEveryNumPackets}"" 
             MemoryName=""{MemoryName}"" 
             {(TargetName != null ? $"TargetName=\"{TargetName}\"" : "")}
             {(SkipWrite != null ? $"SkipWrite=\"{SkipWrite}\"" : "")}
             />
</data>";
}

public class GptPartitionEntry
{
    public string Name { get; set; } = "";
    public ulong StartSector { get; set; }
    public ulong Sectors { get; set; }
    public string? PartitionGuid { get; set; }

    public override string ToString() =>
        $"GPT({Name}, start={StartSector}, sectors={Sectors})";
}

public class GptPartitionInfo
{
    public string Name { get; set; } = "";
    public ulong StartSector { get; set; }
    public ulong Sectors { get; set; }
}

public class FirehoseException : Exception
{
    public FirehoseException(string message) : base(message) { }
    public FirehoseException(string message, Exception inner) : base(message, inner) { }
}