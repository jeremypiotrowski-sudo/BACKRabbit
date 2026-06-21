using BACKRabbit.Usb;

namespace BACKRabbit.Protocol.DownloadMode;

/// <summary>
/// Samsung Download Mode Flasher - Heimdall protocol implementation
/// Handles PIT parsing, handshake, and partition flashing
/// </summary>
public class DownloadModeFlasher : IDisposable
{
    private readonly UsbDeviceManager _usb;
    private bool _sessionActive;
    
    // Download Mode protocol constants
    private const byte REQ_INIT = 0x01;
    private const byte REQ_IDENTIFY = 0x02;
    private const byte REQ_END_SESSION = 0x03;
    private const byte REQ_REBOOT = 0x04;
    private const byte REQ_FILE_TRANSFER = 0x05;
    private const byte REQ_PIT = 0x06;
    
    private const int MAX_TRANSFER_SIZE = 1048576; // 1MB chunks
    
    public event EventHandler<FlashProgressEventArgs>? ProgressChanged;
    public event EventHandler<string>? LogMessage;
    
    public DownloadModeFlasher(UsbDeviceManager usb)
    {
        _usb = usb;
    }
    
    /// <summary>
    /// Initialize Download Mode session
    /// </summary>
    public async Task<bool> InitializeAsync(CancellationToken ct = default)
    {
        Log("Initializing Download Mode session...");
        
        // Send INIT request
        var initRequest = new byte[16];
        initRequest[0] = REQ_INIT;
        
        var response = await ControlTransferAsync(initRequest, ct);
        
        if (response == null || response.Length < 4)
        {
            Log("Failed to initialize - no response");
            return false;
        }
        
        // Check response
        if (response[0] != 0x01)
        {
            Log($"Failed to initialize - invalid response: {response[0]:X2}");
            return false;
        }
        
        _sessionActive = true;
        Log("Download Mode session initialized");
        
        return true;
    }
    
    /// <summary>
    /// Get device identification info
    /// </summary>
    public async Task<DeviceIdentifyInfo> IdentifyAsync(CancellationToken ct = default)
    {
        Log("Requesting device identification...");
        
        var identifyRequest = new byte[16];
        identifyRequest[0] = REQ_IDENTIFY;
        
        var response = await ControlTransferAsync(identifyRequest, ct);
        
        if (response == null || response.Length < 64)
        {
            throw new DownloadModeException("Failed to identify device");
        }
        
        var info = new DeviceIdentifyInfo
        {
            Model = System.Text.Encoding.ASCII.GetString(response.Take(32).ToArray()).TrimEnd('\0'),
            SerialNumber = System.Text.Encoding.ASCII.GetString(response.Skip(32).Take(32).ToArray()).TrimEnd('\0')
        };
        
        Log($"Device identified: {info.Model}");
        
        return info;
    }
    
    /// <summary>
    /// Read PIT file from device
    /// </summary>
    public async Task<PitFile> ReadPitAsync(CancellationToken ct = default)
    {
        Log("Reading PIT file...");
        
        var pitRequest = new byte[16];
        pitRequest[0] = REQ_PIT;
        
        var response = await ControlTransferAsync(pitRequest, ct);
        
        if (response == null)
        {
            throw new DownloadModeException("Failed to read PIT");
        }
        
        var pit = PitFile.Parse(response);
        Log($"PIT loaded: {pit.Entries.Count} partitions");
        
        return pit;
    }
    
    /// <summary>
    /// Flash partition
    /// </summary>
    public async Task<bool> FlashPartitionAsync(string partitionName, byte[] data, 
                                                 CancellationToken ct = default)
    {
        if (!_sessionActive)
        {
            throw new DownloadModeException("Session not active");
        }
        
        Log($"Flashing partition: {partitionName} ({data.Length} bytes)");
        
        // Send FILE_TRANSFER request
        var transferRequest = new byte[64];
        transferRequest[0] = REQ_FILE_TRANSFER;
        System.Text.Encoding.ASCII.GetBytes(partitionName.PadRight(32, '\0'))
            .CopyTo(transferRequest, 1);
        System.BitConverter.GetBytes(data.Length).CopyTo(transferRequest, 33);
        
        var response = await ControlTransferAsync(transferRequest, ct);
        
        if (response == null || response[0] != 0x01)
        {
            Log($"Failed to start transfer for {partitionName}");
            return false;
        }
        
        // Send data in chunks
        var chunks = (data.Length + MAX_TRANSFER_SIZE - 1) / MAX_TRANSFER_SIZE;
        var bytesSent = 0;
        
        for (int i = 0; i < chunks; i++)
        {
            ct.ThrowIfCancellationRequested();
            
            var offset = i * MAX_TRANSFER_SIZE;
            var chunkSize = Math.Min(MAX_TRANSFER_SIZE, data.Length - offset);
            var chunk = data.Skip(offset).Take(chunkSize).ToArray();
            
            // Send chunk
            var bytesWritten = await WriteAsync(chunk, ct);
            bytesSent += bytesWritten;
            
            // Report progress
            var progress = new FlashProgressEventArgs
            {
                PartitionName = partitionName,
                BytesSent = bytesSent,
                TotalBytes = data.Length,
                Percentage = (int)(100.0 * bytesSent / data.Length)
            };
            ProgressChanged?.Invoke(this, progress);
            
            Log($"Chunk {i + 1}/{chunks}: {bytesWritten} bytes");
            
            // Wait for ACK
            var ack = await ReadAsync(16, ct);
            if (ack.Length == 0 || ack[0] != 0x01)
            {
                Log($"Transfer failed at chunk {i + 1}");
                return false;
            }
        }
        
        Log($"Partition {partitionName} flashed successfully");
        return true;
    }
    
    /// <summary>
    /// Flash multiple partitions from firmware package
    /// </summary>
    public async Task<FirmwareFlashResult> FlashFirmwareAsync(
        FirmwarePackage firmware, 
        List<string> partitions,
        CancellationToken ct = default)
    {
        var result = new FirmwareFlashResult();
        
        Log($"Starting firmware flash: {partitions.Count} partitions");
        
        foreach (var partition in partitions)
        {
            ct.ThrowIfCancellationRequested();
            
            var partitionData = firmware.GetPartition(partition);
            if (partitionData == null)
            {
                result.FailedPartitions.Add(partition);
                Log($"Partition {partition} not found in firmware");
                continue;
            }
            
            var success = await FlashPartitionAsync(partition, partitionData, ct);
            
            if (success)
            {
                result.SuccessfulPartitions.Add(partition);
            }
            else
            {
                result.FailedPartitions.Add(partition);
            }
        }
        
        result.Success = result.FailedPartitions.Count == 0;
        return result;
    }
    
    /// <summary>
    /// End session and reboot
    /// </summary>
    public async Task<bool> EndSessionAsync(RebootMode mode = RebootMode.Normal, 
                                            CancellationToken ct = default)
    {
        Log("Ending Download Mode session...");
        
        var endRequest = new byte[16];
        endRequest[0] = REQ_END_SESSION;
        endRequest[1] = (byte)mode;
        
        var response = await ControlTransferAsync(endRequest, ct);
        
        _sessionActive = false;
        
        if (response != null && response[0] == 0x01)
        {
            Log("Session ended successfully, device will reboot");
            return true;
        }
        
        Log("Session ended (response may not be received due to reboot)");
        return true; // Reboot may cut response
    }
    
    /// <summary>
    /// Reboot device
    /// </summary>
    public async Task<bool> RebootAsync(RebootMode mode = RebootMode.Normal, 
                                        CancellationToken ct = default)
    {
        Log($"Rebooting device (mode: {mode})...");
        
        var rebootRequest = new byte[16];
        rebootRequest[0] = REQ_REBOOT;
        rebootRequest[1] = (byte)mode;
        
        await ControlTransferAsync(rebootRequest, ct);
        
        // Device will reboot, no response expected
        _sessionActive = false;
        return true;
    }
    
    #region Private Methods
    
private async Task<byte[]?> ControlTransferAsync(byte[] request, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            try
            {
                var response = new byte[64];
                // Use ControlTransfer with proper parameters: requestType, request, value, index, buffer, out transferred
                int transferred;
                var success = _usb.ControlTransfer(
                    0x40,  // Vendor OUT request
                    request[0],  // request
                    0,  // value
                    0,  // index
                    request,  // buffer
                    out transferred,
                    5000);
                
                if (!success) return null;
                
                var bytesRead = _usb.Read(response, 5000);
                
                return response.Take(bytesRead).ToArray();
            }
            catch (Exception ex)
            {
                Log($"Control transfer error: {ex.Message}");
                return null;
            }
        }, ct);
    }
    
    private async Task<int> WriteAsync(byte[] data, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            try
            {
                return _usb.Write(data, 10000);
            }
            catch (Exception ex)
            {
                Log($"Write error: {ex.Message}");
                throw;
            }
        }, ct);
    }
    
    private async Task<byte[]> ReadAsync(int length, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            try
            {
                var buffer = new byte[length];
                var bytesRead = _usb.Read(buffer, 5000);
                return buffer.Take(bytesRead).ToArray();
            }
            catch (Exception ex)
            {
                Log($"Read error: {ex.Message}");
                throw;
            }
        }, ct);
    }
    
    private void Log(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        LogMessage?.Invoke(this, $"[{timestamp}] {message}");
    }
    
    #endregion
    
    public void Dispose()
    {
        if (_sessionActive)
        {
            try
            {
                EndSessionAsync().Wait();
            }
            catch { }
        }
    }
}

/// <summary>
/// Device identification info
/// </summary>
public class DeviceIdentifyInfo
{
    public string Model { get; set; } = "";
    public string SerialNumber { get; set; } = "";
}

/// <summary>
/// Flash progress event args
/// </summary>
public class FlashProgressEventArgs : EventArgs
{
    public string PartitionName { get; set; } = "";
    public int BytesSent { get; set; }
    public int TotalBytes { get; set; }
    public int Percentage { get; set; }
    public TimeSpan Elapsed { get; set; }
    public TimeSpan Remaining { get; set; }
}

/// <summary>
/// Firmware flash result
/// </summary>
public class FirmwareFlashResult
{
    public bool Success { get; set; }
    public List<string> SuccessfulPartitions { get; set; } = new();
    public List<string> FailedPartitions { get; set; } = new();
    public TimeSpan TotalTime { get; set; }
    public string ErrorMessage { get; set; } = "";
}

/// <summary>
/// Reboot mode
/// </summary>
public enum RebootMode
{
    Normal = 0,
    Bootloader = 1,
    Recovery = 2,
    Download = 3,
    EDL = 4
}

/// <summary>
/// Download Mode exception
/// </summary>
public class DownloadModeException : Exception
{
    public DownloadModeException(string message) : base(message) { }
}