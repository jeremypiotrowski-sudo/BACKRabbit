# Cross-Reference Map: BACKRabbit.Protocol.DownloadMode.DownloadModeFlasher.cs ↔ Heimdall/Samsung Sources

## Overview
| Property | Value |
|----------|-------|
| **BACKRabbit File** | `BACKRabbit.Protocol.DownloadMode/DownloadModeFlasher.cs` |
| **Heimdall Source** | `heimdall/src/heimdall.cpp`, `heimdall/src/protocol.cpp` |
| **Samsung Source** | Proprietary (no public source) |
| **Total Lines (BACKRabbit)** | 410 |

---

## Version-by-Version Cross-Reference

### Heimdall Protocol (heimdall/src/)
| BACKRabbit Line(s) | Heimdall Source | Function |
|---|---|---|
| 15-22 | `protocol.h` | Protocol constants (REQ_INIT, REQ_IDENTIFY, etc.) |
| 35-62 | `heimdall.cpp` | `InitializeAsync()` - INIT handshake |
| 67-90 | `heimdall.cpp` | `IdentifyAsync()` - device identification |
| 95-113 | `heimdall.cpp` | `ReadPitAsync()` - PIT file retrieval |
| 118-182 | `heimdall.cpp` | `FlashPartitionAsync()` - file transfer |
| 187-222 | `heimdall.cpp` | `FlashFirmwareAsync()` - multi-partition |
| 227-248 | `heimdall.cpp` | `EndSessionAsync()` - session end |
| 253-267 | `heimdall.cpp` | `RebootAsync()` - reboot |

### Heimdall Protocol Constants
| Constant | Value | Description |
|---|---|---|
| `REQ_INIT` | 0x01 | Initialize session |
| `REQ_IDENTIFY` | 0x02 | Get device info |
| `REQ_END_SESSION` | 0x03 | End session |
| `REQ_REBOOT` | 0x04 | Reboot device |
| `REQ_FILE_TRANSFER` | 0x05 | Start file transfer |
| `REQ_PIT` | 0x06 | Read PIT |

---

## Detailed Function Mapping

### InitializeAsync() - Lines 35-62
**Heimdall Equivalent:** `Heimdall::initialize()` in heimdall.cpp

```cpp
// Heimdall (heimdall.cpp ~line 200)
bool Heimdall::initialize() {
    unsigned char request[16] = { 0 };
    request[0] = 0x01; // REQ_INIT
    
    if (!control_transfer(0x40, 0x01, 0, 0, request, 16, &transferred)) {
        return false;
    }
    
    unsigned char response[64];
    if (!read_bulk_endpoint(response, 64, &transferred)) {
        return false;
    }
    
    return response[0] == 0x01;
}
```

**BACKRabbit mapping:** Lines 40-56 direct port with ControlTransfer.

### IdentifyAsync() - Lines 67-90
**Heimdall Equivalent:** `Heimdall::identify()` in heimdall.cpp

```cpp
// Heimdall (heimdall.cpp ~line 250)
bool Heimdall::identify(DeviceInfo& info) {
    unsigned char request[16] = { 0 };
    request[0] = 0x02; // REQ_IDENTIFY
    
    if (!control_transfer(0x40, 0x02, 0, 0, request, 16, &transferred)) {
        return false;
    }
    
    unsigned char response[64];
    if (!read_bulk_endpoint(response, 64, &transferred)) {
        return false;
    }
    
    // Model: bytes 0-31, Serial: bytes 32-63
    info.model = string((char*)response, 32);
    info.serial = string((char*)response + 32, 32);
    
    return true;
}
```

**BACKRabbit mapping:** Lines 71-85 direct port.

### ReadPitAsync() - Lines 95-113
**Heimdall Equivalent:** `Heimdall::read_pit()` in heimdall.cpp

```cpp
// Heimdall (heimdall.cpp ~line 300)
bool Heimdall::read_pit(vector<unsigned char>& pit_data) {
    unsigned char request[16] = { 0 };
    request[0] = 0x06; // REQ_PIT
    
    if (!control_transfer(0x40, 0x06, 0, 0, request, 16, &transferred)) {
        return false;
    }
    
    // Read PIT data (variable size)
    unsigned char buffer[65536];
    if (!read_bulk_endpoint(buffer, sizeof(buffer), &transferred)) {
        return false;
    }
    
    pit_data.assign(buffer, buffer + transferred);
    return true;
}
```

**BACKRabbit mapping:** Lines 99-109 direct port with PitFile.Parse().

### FlashPartitionAsync() - Lines 118-182
**Heimdall Equivalent:** `Heimdall::flash_partition()` in heimdall.cpp

```cpp
// Heimdall (heimdall.cpp ~line 350)
bool Heimdall::flash_partition(const string& partition, const vector<uint8_t>& data) {
    // 1. Send FILE_TRANSFER request
    unsigned char request[64] = { 0 };
    request[0] = 0x05; // REQ_FILE_TRANSFER
    memcpy(request + 1, partition.c_str(), min(partition.size(), 32));
    *reinterpret_cast<uint32_t*>(request + 33) = data.size();
    
    if (!control_transfer(0x40, 0x05, 0, 0, request, 64, &transferred)) {
        return false;
    }
    
    // 2. Check response
    unsigned char response[16];
    if (!read_bulk_endpoint(response, 16, &transferred) || response[0] != 0x01) {
        return false;
    }
    
    // 3. Send data in chunks
    const size_t MAX_TRANSFER = 1024 * 1024;
    size_t offset = 0;
    while (offset < data.size()) {
        size_t chunk_size = min(MAX_TRANSFER, data.size() - offset);
        
        if (!write_bulk_endpoint(data.data() + offset, chunk_size, &transferred)) {
            return false;
        }
        
        // 4. Wait for ACK
        if (!read_bulk_endpoint(response, 16, &transferred) || response[0] != 0x01) {
            return false;
        }
        
        offset += chunk_size;
    }
    
    return true;
}
```

**BACKRabbit mapping:**
- Lines 129-134: Build FILE_TRANSFER request (partition name + size)
- Lines 135-141: ControlTransfer + response check
- Lines 147-177: Chunk loop with WriteAsync + ACK check
- Lines 160-167: Progress reporting

---

## Download Mode USB Protocol

### Control Transfer Parameters
```csharp
_usb.ControlTransfer(
    0x40,           // bmRequestType: Vendor OUT (0x40 = 01000000b)
    request[0],     // bRequest: REQ_* constant
    0,              // wValue: 0
    0,              // wIndex: 0
    request,        // data buffer
    out transferred,
    5000            // timeout (ms)
)
```

### Endpoint Configuration
| Endpoint | Direction | Type | Purpose |
|---|---|---|---|
| EP0 | OUT | Control | Commands (INIT, IDENTIFY, etc.) |
| EP1 | IN | Bulk | Responses, PIT data |
| EP2 | OUT | Bulk | File transfer data |

### Samsung VID/PID
- **VID**: 0x04E8 (Samsung)
- **PID**: 0x6860 (Download Mode), varies by device
- **Interface**: 0
- **Configuration**: 1

---

## Data Structures

### DeviceIdentifyInfo (Lines 361-365)
```csharp
public class DeviceIdentifyInfo
{
    public string Model { get; set; } = "";
    public string SerialNumber { get; set; } = "";
}
```

### FlashProgressEventArgs (Lines 370-378)
```csharp
public class FlashProgressEventArgs : EventArgs
{
    public string PartitionName { get; set; } = "";
    public int BytesSent { get; set; }
    public int TotalBytes { get; set; }
    public int Percentage { get; set; }
    public TimeSpan Elapsed { get; set; }
    public TimeSpan Remaining { get; set; }
}
```

### FirmwareFlashResult (Lines 383-390)
```csharp
public class FirmwareFlashResult
{
    public bool Success { get; set; }
    public List<string> SuccessfulPartitions { get; set; } = new();
    public List<string> FailedPartitions { get; set; } = new();
    public TimeSpan TotalTime { get; set; }
    public string ErrorMessage { get; set; } = "";
}
```

### RebootMode (Lines 395-402)
```csharp
public enum RebootMode
{
    Normal = 0,
    Bootloader = 1,
    Recovery = 2,
    Download = 3,
    EDL = 4
}
```

---

## Usage Examples

### Flash Single Partition
```csharp
var usb = new UsbDeviceManager();
var flasher = new DownloadModeFlasher(usb);

await flasher.InitializeAsync();
var info = await flasher.IdentifyAsync();
Console.WriteLine($"Device: {info.Model}");

var bootImg = File.ReadAllBytes("boot.img");
await flasher.FlashPartitionAsync("boot", bootImg);

await flasher.EndSessionAsync(RebootMode.Normal);
```

### Flash Full Firmware
```csharp
var extractor = new SamsungFirmwareExtractor();
var firmware = extractor.ExtractTarMd5("AP_*.tar.md5");

var partitions = new[] { "boot", "vbmeta", "dtbo", "super" };
var result = await flasher.FlashFirmwareAsync(firmware, partitions);

if (result.Success) {
    Console.WriteLine("All partitions flashed successfully");
} else {
    Console.WriteLine($"Failed: {string.Join(", ", result.FailedPartitions)}");
}

await flasher.EndSessionAsync(RebootMode.Normal);
```

### Read PIT for Partition Info
```csharp
var pit = await flasher.ReadPitAsync();
foreach (var entry in pit.Entries) {
    Console.WriteLine($"{entry.PartitionName}: offset={entry.BlockOffset}, size={entry.BlockCount}");
}
```

---

## Samsung-Specific Notes

### Download Mode Entry
```bash
# Samsung devices enter Download Mode via:
# Volume Up + Volume Down + Power (screen off)
# Or: adb reboot download
```

### Protocol Differences from Fastboot
| Feature | Download Mode | Fastboot |
|---|---|---|
| Protocol | Heimdall (proprietary) | Android Fastboot |
| Partitions | PIT-defined | GPT/super |
| Auth | None (usually) | AVB/verified boot |
| Reboot modes | Normal/Bootloader/Recovery/EDL | Bootloader/Recovery/Fastboot |

### Known Limitations
1. **No encryption support** - Can't flash encrypted partitions
2. **No AVB handling** - Doesn't verify/fix vbmeta
3. **Single-threaded** - No parallel flashing
4. **PIT parsing** - Basic, doesn't handle all PIT versions

---

## Missing/Partial Implementations

1. **PIT Writing** - Can't write modified PIT back
2. **Partition Resize** - No resize capability
3. **Firmware Verification** - No signature/checksum verification
4. **EDL Mode** - Qualcomm Emergency Download not implemented
5. **Multiple Devices** - Single device at a time
6. **Progress Cancellation** - Partial, uses CancellationToken

---

## Test Coverage Needed

| Test Case | Status |
|---|---|
| InitializeAsync_ValidDevice_ReturnsTrue | ❌ |
| IdentifyAsync_ReturnsModelSerial | ❌ |
| ReadPitAsync_ValidPIT_ParsesEntries | ❌ |
| FlashPartitionAsync_SmallPartition_Succeeds | ❌ |
| FlashPartitionAsync_LargePartition_Chunks | ❌ |
| FlashFirmwareAsync_MultiplePartitions_ReportsProgress | ❌ |
| EndSessionAsync_RebootMode_Correct | ❌ |
| RebootAsync_DownloadMode_Reenters | ❌ |

---

## Source File References

**BACKRabbit Source:** `BACKRabbit.Protocol.DownloadMode/DownloadModeFlasher.cs` (410 lines)

**Heimdall Sources:**
- `heimdall/src/heimdall.cpp` - Main flashing logic
- `heimdall/src/protocol.cpp` - Protocol implementation
- `heimdall/src/pit.cpp` - PIT parsing

**Related BACKRabbit Files:**
- `PitFile.cs` - PIT parsing
- `UsbDeviceManager.cs` - USB transport