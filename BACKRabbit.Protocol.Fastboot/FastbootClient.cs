using System.Buffers.Binary;
using System.Text;
using BACKRabbit.Usb;

namespace BACKRabbit.Protocol.Fastboot;

/// <summary>
/// Fastboot Client - Android bootloader flashing protocol
/// Supports sparse images, A/B slots, and all standard commands
/// </summary>

public class FastbootClient : IFastbootClient, IDisposable
{
    private UsbDeviceManager? _usb;
    private bool _connected;
    
    private const int FASTBOOT_TIMEOUT = 30000;
    private const int MAX_TRANSFER_SIZE = 1048576;
    
    private const string RESPONSE_OKAY = "OKAY";
    private const string RESPONSE_FAIL = "FAIL";
    private const string RESPONSE_DATA = "DATA";
    
    public bool IsConnected => _connected && _usb?.IsConnected == true;
    public string? Serial { get; private set; }
    public string? Product { get; private set; }
    public string? CurrentSlot { get; private set; }
    
    public event EventHandler<string>? LogMessage;
    public event EventHandler<FastbootProgressEventArgs>? ProgressChanged;
    
    public async Task<bool> ConnectAsync(UsbDeviceManager usb, CancellationToken ct = default)
    {
        _usb = usb;
        
        var devices = usb.EnumerateSamsungDevices();
        var fastbootDevice = devices.FirstOrDefault(d => d.DeviceMode == DeviceMode.Fastboot);
        
        if (fastbootDevice == null)
        {
            Log("No Fastboot device found");
            return false;
        }
        
        if (!usb.OpenDevice(fastbootDevice.ProductId))
        {
            Log("Failed to open Fastboot device");
            return false;
        }
        
        Serial = fastbootDevice.SerialNumber;
        _connected = true;
        
        Log($"Fastboot connected: {Serial}");
        await PopulateDeviceInfoAsync(ct);
        
        return true;
    }
    
    private async Task PopulateDeviceInfoAsync(CancellationToken ct)
    {
        Product = await GetVarAsync("product", ct);
        CurrentSlot = await GetVarAsync("current-slot", ct);
        Log($"Device: {Product}, Slot: {CurrentSlot}");
    }
    
    public async Task<string> SendCommandAsync(string command, CancellationToken ct = default)
    {
        if (!_connected || _usb == null) 
            throw new InvalidOperationException("Not connected to Fastboot");
        
        Log($"Command: {command}");
        
        var commandBytes = Encoding.ASCII.GetBytes(command + '\0');
        _usb.Write(commandBytes, FASTBOOT_TIMEOUT);
        
        var response = await ReadResponseAsync(ct);
        Log($"Response: {response}");
        
        if (response.StartsWith(RESPONSE_FAIL))
        {
            var error = response.Substring(RESPONSE_FAIL.Length).TrimStart(':');
            throw new FastbootException(error);
        }
        
        return response;
    }
    
    public async Task<string> GetVarAsync(string variable, CancellationToken ct = default)
    {
        var response = await SendCommandAsync($"getvar:{variable}", ct);
        return response.StartsWith(RESPONSE_OKAY) 
            ? response.Substring(RESPONSE_OKAY.Length).TrimStart(':') 
            : "";
    }
    
    public async Task<Dictionary<string, string>> GetAllVarsAsync(CancellationToken ct = default)
    {
        var vars = new Dictionary<string, string>();
        var commonVars = new[]
        {
            "product", "serialno", "version", "version-bootloader",
            "current-slot", "slot-suffixes", "partition-type:boot",
            "partition-type:system", "partition-type:vendor",
            "has-slot:boot", "has-slot:system", "has-slot:vendor",
            "unlocked", "off-mode-charge"
        };
        
        foreach (var v in commonVars)
        {
            try
            {
                var value = await GetVarAsync(v, ct);
                if (!string.IsNullOrEmpty(value)) vars[v] = value;
            }
            catch { }
        }
        
        return vars;
    }
    
    public async Task<bool> FlashAsync(string partition, byte[] data, CancellationToken ct = default)
    {
        Log($"Flashing {partition} ({data.Length} bytes)");
        
        await SendCommandAsync($"flash:{partition}", ct);
        
        var chunks = (data.Length + MAX_TRANSFER_SIZE - 1) / MAX_TRANSFER_SIZE;
        var bytesSent = 0;
        
        for (int i = 0; i < chunks; i++)
        {
            ct.ThrowIfCancellationRequested();
            
            var offset = i * MAX_TRANSFER_SIZE;
            var chunkSize = Math.Min(MAX_TRANSFER_SIZE, data.Length - offset);
            var chunk = data.Skip(offset).Take(chunkSize).ToArray();
            
            var dataHeader = CreateDataHeader(chunkSize);
            _usb!.Write(dataHeader, FASTBOOT_TIMEOUT);
            _usb.Write(chunk, FASTBOOT_TIMEOUT);
            bytesSent += chunkSize;
            
            ProgressChanged?.Invoke(this, new FastbootProgressEventArgs
            {
                Partition = partition,
                BytesSent = bytesSent,
                TotalBytes = data.Length,
                Percentage = (int)(100.0 * bytesSent / data.Length)
            });
            
            var response = await ReadResponseAsync(ct);
            if (response.StartsWith(RESPONSE_FAIL))
            {
                throw new FastbootException($"Flash failed: {response}");
            }
        }
        
        var finalResponse = await ReadResponseAsync(ct);
        return finalResponse.StartsWith(RESPONSE_OKAY);
    }
    
    public async Task<bool> FlashSparseAsync(string partition, byte[] sparseData, CancellationToken ct = default)
    {
        Log($"Flashing sparse image to {partition}");
        
        var sparse = SparseImage.Parse(sparseData);
        Log($"Sparse image: {sparse.TotalBlocks} blocks, {sparse.Chunks} chunks");
        
        await SendCommandAsync($"flash:{partition}", ct);
        
        var blockSize = sparse.BlockSize;
        var totalBlocks = sparse.TotalBlocks;
        var blocksWritten = 0;
        
        foreach (var chunk in sparse.Chunks)
        {
            ct.ThrowIfCancellationRequested();
            
            switch (chunk.Type)
            {
case SparseChunkType.Raw:
                    var dataHeader = CreateDataHeader((int)chunk.DataSize);
                    _usb!.Write(dataHeader, FASTBOOT_TIMEOUT);
                    _usb.Write(chunk.Data, FASTBOOT_TIMEOUT);
                    blocksWritten += (int)chunk.Blocks;
                    break;
                    
                case SparseChunkType.Fill:
                    var pattern = chunk.Data.Take(4).ToArray();
                    var fillData = new byte[chunk.DataSize];
                    for (int i = 0; i < fillData.Length; i += 4)
                        pattern.CopyTo(fillData, i);
                    var fillHeader = CreateDataHeader(fillData.Length);
                    _usb!.Write(fillHeader, FASTBOOT_TIMEOUT);
                    _usb.Write(fillData, FASTBOOT_TIMEOUT);
                    blocksWritten += (int)chunk.Blocks;
                    break;
                    
                case SparseChunkType.DontCare:
                    blocksWritten += (int)chunk.Blocks;
                    break;
            }
            
            ProgressChanged?.Invoke(this, new FastbootProgressEventArgs
            {
                Partition = partition,
                BytesSent = (int)(blocksWritten * blockSize),
                TotalBytes = (int)(totalBlocks * blockSize),
                Percentage = totalBlocks > 0 ? (int)(100.0 * blocksWritten / totalBlocks) : 0
            });
            
            await ReadResponseAsync(ct);
        }
        
        var finalResponse = await ReadResponseAsync(ct);
        return finalResponse.StartsWith(RESPONSE_OKAY);
    }
    
    public async Task<bool> EraseAsync(string partition, CancellationToken ct = default)
    {
        Log($"Erasing {partition}");
        var response = await SendCommandAsync($"erase:{partition}", ct);
        return response.StartsWith(RESPONSE_OKAY);
    }
    
    public async Task<bool> SetActiveAsync(string slot, CancellationToken ct = default)
    {
        Log($"Setting active slot: {slot}");
        var response = await SendCommandAsync($"set_active:{slot}", ct);
        return response.StartsWith(RESPONSE_OKAY);
    }
    
    public async Task<bool> RebootAsync(CancellationToken ct = default)
    {
        Log("Rebooting device");
        await SendCommandAsync("reboot", ct);
        _connected = false;
        return true;
    }
    
    public async Task<bool> RebootBootloaderAsync(CancellationToken ct = default)
    {
        Log("Rebooting to bootloader");
        await SendCommandAsync("reboot-bootloader", ct);
        _connected = false;
        return true;
    }
    
    public async Task<bool> RebootRecoveryAsync(CancellationToken ct = default)
    {
        Log("Rebooting to recovery");
        await SendCommandAsync("reboot-recovery", ct);
        _connected = false;
        return true;
    }
    
    public async Task<bool> UnlockBootloaderAsync(CancellationToken ct = default)
    {
        Log("Unlocking bootloader (THIS WILL WIPE DATA!)");
        var response = await SendCommandAsync("flashing:unlock", ct);
        return response.StartsWith(RESPONSE_OKAY);
    }
    
    public async Task<bool> LockBootloaderAsync(CancellationToken ct = default)
    {
        Log("Locking bootloader");
        var response = await SendCommandAsync("flashing:lock", ct);
        return response.StartsWith(RESPONSE_OKAY);
    }
    
    public async Task<bool> BootAsync(byte[] bootImage, CancellationToken ct = default)
    {
        Log($"Booting image ({bootImage.Length} bytes)");
        await SendCommandAsync($"boot", ct);
        
        var dataHeader = CreateDataHeader(bootImage.Length);
        _usb!.Write(dataHeader, FASTBOOT_TIMEOUT);
        _usb.Write(bootImage, FASTBOOT_TIMEOUT);
        
        var response = await ReadResponseAsync(ct);
        return response.StartsWith(RESPONSE_OKAY);
    }
    
    private async Task<string> ReadResponseAsync(CancellationToken ct)
    {
        if (_usb == null) throw new InvalidOperationException("Not connected");
        
        return await Task.Run(() =>
        {
            var buffer = new byte[64];
            var bytesRead = _usb.Read(buffer, FASTBOOT_TIMEOUT);
            return Encoding.ASCII.GetString(buffer, 0, bytesRead).Trim();
        }, ct);
    }
    
    private byte[] CreateDataHeader(int size)
    {
        // DATA + 4-byte hex size (e.g., "DATA0400")
        var header = Encoding.ASCII.GetBytes($"DATA{size:X4}");
        return header;
    }
    
    private void Log(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        LogMessage?.Invoke(this, $"[{timestamp}] Fastboot: {message}");
    }
    
    public void Dispose()
    {
        _usb?.CloseDevice();
        _connected = false;
    }
}

public class FastbootProgressEventArgs : EventArgs
{
    public string Partition { get; set; } = "";
    public int BytesSent { get; set; }
    public int TotalBytes { get; set; }
    public int Percentage { get; set; }
}

public class FastbootException : Exception
{
    public FastbootException(string message) : base(message) { }
}