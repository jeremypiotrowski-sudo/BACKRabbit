# Companion Documentation: BACKRabbit.Protocol.Fastboot.FastbootClient.cs

## Purpose
Android Fastboot protocol client for flashing partitions, managing A/B slots, unlocking bootloader, and booting temporary images. Essential for Samsung firmware flashing and Magisk uninstallation.

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                        FastbootClient                               │
├─────────────────────────────────────────────────────────────────────┤
│  ConnectAsync() ──→ USB Fastboot connection                        │
│       │                                                             │
│       ├─→ Enumerate Samsung devices (VID:PID)                      │
│       ├─→ Open device interface                                     │
│       └─→ Get device info (product, serial, slot)                  │
│                                                                     │
│  SendCommandAsync() ──→ Text-based command/response                │
│       │                                                             │
│       ├─→ "command\0" → Device                                      │
│       ├─→ "OKAY" / "FAIL<msg>" / "DATA<hex_size>" ← Device        │
│       └─→ If DATA: send binary data                                │
│                                                                     │
│  FlashAsync() ──→ Flash raw partition image                        │
│  FlashSparseAsync() ──→ Flash sparse image (super.img)             │
│  EraseAsync() ──→ Erase partition                                  │
│  SetActiveAsync() ──→ Switch A/B slot                              │
│  RebootAsync() ──→ Reboot device                                   │
│  UnlockBootloaderAsync() ──→ Unlock (WIPES DATA!)                 │
│  LockBootloaderAsync() ──→ Lock                                    │
│  BootAsync() ──→ Boot temp image without flashing                  │
└─────────────────────────────────────────────────────────────────────┘
```

## Public API

### Connection
```csharp
/// <summary>
/// Connect to Fastboot device via USB
/// </summary>
public async Task<bool> ConnectAsync(UsbDeviceManager usb, CancellationToken ct = default)
```

### Device Information
```csharp
public bool IsConnected { get; }
public string? Serial { get; }
public string? Product { get; }
public string? CurrentSlot { get; }

/// <summary>
/// Get single variable
/// </summary>
public async Task<string> GetVarAsync(string variable, CancellationToken ct = default)

/// <summary>
/// Get all common variables
/// </summary>
public async Task<Dictionary<string, string>> GetAllVarsAsync(CancellationToken ct = default)
```

### Flashing
```csharp
/// <summary>
/// Flash raw partition image
/// </summary>
public async Task<bool> FlashAsync(string partition, byte[] data, CancellationToken ct = default)

/// <summary>
/// Flash sparse image (auto-parses sparse format)
/// </summary>
public async Task<bool> FlashSparseAsync(string partition, byte[] sparseData, CancellationToken ct = default)

/// <summary>
/// Erase partition
/// </summary>
public async Task<bool> EraseAsync(string partition, CancellationToken ct = default)
```

### Slot Management (A/B)
```csharp
/// <summary>
/// Set active slot (a or b)
/// </summary>
public async Task<bool> SetActiveAsync(string slot, CancellationToken ct = default)
```

### Reboot
```csharp
/// <summary>
/// Reboot to Android
/// </summary>
public async Task<bool> RebootAsync(CancellationToken ct = default)

/// <summary>
/// Reboot to bootloader (stay in fastboot)
/// </summary>
public async Task<bool> RebootBootloaderAsync(CancellationToken ct = default)

/// <summary>
/// Reboot to recovery
/// </summary>
public async Task<bool> RebootRecoveryAsync(CancellationToken ct = default)
```

### Bootloader Lock/Unlock
```csharp
/// <summary>
/// Unlock bootloader (WIPES DATA!)
/// </summary>
public async Task<bool> UnlockBootloaderAsync(CancellationToken ct = default)

/// <summary>
/// Lock bootloader
/// </summary>
public async Task<bool> LockBootloaderAsync(CancellationToken ct = default)
```

### Temporary Boot
```csharp
/// <summary>
/// Boot image without flashing (for testing)
/// </summary>
public async Task<bool> BootAsync(byte[] bootImage, CancellationToken ct = default)
```

### Events
```csharp
public event EventHandler<string>? LogMessage;
public event EventHandler<FastbootProgressEventArgs>? ProgressChanged;
```

## Fastboot Protocol

### Command/Response Format
```
Host: "command\0" (ASCII, null-terminated)
Device: "OKAY" | "FAIL<message>" | "DATA<hex_size>"
If DATA: Host sends binary data (max 1MB chunks)
```

### Data Transfer
```csharp
// Host sends DATA header: "DATA" + 4-digit hex size
// Example: 1024 bytes = "DATA0400"
var header = Encoding.ASCII.GetBytes($"DATA{size:X4}");
_usb.Write(header);
_usb.Write(chunk);
```

### Common Variables
| Variable | Description | Example |
|---|---|---|
| `product` | Product name | "r8q" |
| `serialno` | Serial number | "R58M123456" |
| `version` | Fastboot version | "0.5" |
| `current-slot` | Active slot | "a" or "b" |
| `slot-suffixes` | Available slots | "_a,_b" |
| `partition-type:boot` | Filesystem type | "raw" |
| `has-slot:boot` | Slot support | "yes" / "no" |
| `unlocked` | Bootloader state | "yes" / "no" |

## Sparse Image Support

### Sparse Format (AOSP)
```c
// Header (28 bytes):
magic: 0xED26FF3A
major_version: 1
minor_version: 0
file_hdr_sz: 28
chunk_hdr_sz: 12
blk_sz: 4096
total_blks: N
total_chunks: M
image_checksum: ...

// Chunk types:
0xCAC1 = Raw (data follows)
0xCAC2 = Fill (4-byte pattern repeats)
0xCAC3 = DontCare (skip blocks)
0xCAC4 = CRC32
```

### FlashSparseAsync Flow
```csharp
// 1. Parse sparse image
var sparse = SparseImage.Parse(sparseData);

// 2. Send flash command
await SendCommandAsync($"flash:{partition}");

// 3. For each chunk:
switch (chunk.Type) {
    case Raw:
        // Send DATA + raw data
        break;
    case Fill:
        // Expand 4-byte pattern, send DATA + expanded data
        break;
    case DontCare:
        // Skip (no data sent)
        break;
}

// 4. Final OKAY
```

## Usage Examples

### Basic Connection
```csharp
var usb = new UsbDeviceManager();
var fastboot = new FastbootClient();

fastboot.LogMessage += (s, msg) => Console.WriteLine(msg);
fastboot.ProgressChanged += (s, e) => 
    Console.WriteLine($"Progress: {e.Partition} {e.Percentage}%");

if (await fastboot.ConnectAsync(usb)) {
    Console.WriteLine($"Connected: {fastboot.Serial}");
    Console.WriteLine($"Product: {fastboot.Product}");
    Console.WriteLine($"Slot: {fastboot.CurrentSlot}");
}
```

### Get Device Info
```csharp
var vars = await fastboot.GetAllVarsAsync();
foreach (var (key, value) in vars) {
    Console.WriteLine($"  {key}: {value}");
}
```

### Flash Stock Images (Samsung S24/S25)
```csharp
// 1. Extract from firmware
var extractor = new SamsungFirmwareExtractor();
var ap = extractor.ExtractTarMd5("AP_SM-S921B_*.tar.md5");

// 2. Flash init_boot (ramdisk only)
await fastboot.FlashAsync("init_boot", ap.GetPartition("init_boot")!);

// 3. Flash boot (kernel + ramdisk)
await fastboot.FlashAsync("boot", ap.GetPartition("boot")!);

// 4. Flash vbmeta
await fastboot.FlashAsync("vbmeta", ap.GetPartition("vbmeta")!);

// 5. Flash dtbo
await fastboot.FlashAsync("dtbo", ap.GetPartition("dtbo")!);

// 6. Reboot
await fastboot.RebootAsync();
```

### Flash Sparse super.img
```csharp
var superData = ap.GetPartition("super");
if (superData != null) {
    // Auto-detects sparse vs raw
    await fastboot.FlashSparseAsync("super", superData);
}
```

### Slot Management
```csharp
// Check current slot
var slot = await fastboot.GetVarAsync("current-slot"); // "a" or "b"

// Switch to other slot
var otherSlot = slot == "a" ? "b" : "a";
await fastboot.SetActiveAsync(otherSlot);
await fastboot.RebootAsync();
```

### Bootloader Unlock (Data Wipe Warning!)
```csharp
Console.WriteLine("WARNING: This will WIPE ALL USER DATA!");
Console.Write("Type 'YES' to confirm: ");
if (Console.ReadLine() == "YES") {
    await fastboot.UnlockBootloaderAsync();
    // Device will show confirmation screen - user must accept
    await fastboot.RebootAsync();
}
```

### Temporary Boot (Test Magisk Patched Image)
```csharp
// Boot patched image without flashing
var patchedBoot = File.ReadAllBytes("boot_patched.img");
await fastboot.BootAsync(patchedBoot);
// Device boots into patched kernel (Magisk active until next reboot)
```

### Complete Samsung Uninstall via Fastboot
```csharp
// 1. Connect in fastboot mode
var usb = new UsbDeviceManager();
var fastboot = new FastbootClient();
await fastboot.ConnectAsync(usb);

// 2. Get current slot
var slot = await fastboot.GetVarAsync("current-slot");

// 3. Extract stock firmware
var ap = SamsungFirmwareExtractor.ExtractTarMd5("AP_SM-S921B_*.tar.md5");

// 4. Flash stock images to current slot
await fastboot.FlashAsync("init_boot", ap.GetPartition("init_boot")!);
await fastboot.FlashAsync("boot", ap.GetPartition("boot")!);
await fastboot.FlashAsync("vbmeta", ap.GetPartition("vbmeta")!);
await fastboot.FlashAsync("dtbo", ap.GetPartition("dtbo")!);

// 5. Reboot
await fastboot.RebootAsync();
```

## Progress Reporting
```csharp
fastboot.ProgressChanged += (s, e) => {
    Console.Write($"\rFlashing {e.Partition}: {e.Percentage}% ({e.BytesSent}/{e.TotalBytes} bytes)");
};
```

## Limitations & Known Gaps

1. **Logical Partitions** - `flash:super` with dynamic partition metadata not implemented
2. **Partition Resize** - `resize:partition` not implemented
3. **Bootloader Flash** - `flash:bootloader` requires special handling
4. **Update/Sideload** - `update:` / `sideload:` not implemented
5. **OEM Commands** - Samsung-specific OEM commands not implemented
6. **GPT Images** - Flash GPT partition tables not implemented
7. **Multiple Interfaces** - No interface selection for multi-interface devices

## Samsung-Specific Notes

### Samsung Fastboot VID:PID
| Mode | VID | PID |
|---|---|---|
| Fastboot | 0x04E8 | 0x685D (varies by model) |
| Download | 0x04E8 | 0x6860 |

### init_boot Partition (GKI 2.0)
Samsung S24/S25/Z Fold 7 use Generic Kernel Image 2.0:
- `boot` = Kernel only
- `init_boot` = Ramdisk only
- **Both must be flashed** for complete uninstall

### Knox Warning
> **Unlocking bootloader on Samsung PERMANENTLY trips Knox eFuse.**
> This voids warranty and disables Samsung Pay, Secure Folder, Knox apps.
> Cannot be reversed by any software tool.

## Related Files

| File | Relationship |
|------|--------------|
| `UsbDeviceManager.cs` | USB transport backend |
| `SparseImage.cs` | Sparse image parsing |
| `SamsungFirmwareExtractor.cs` | Extract stock images |
| `MagiskUninstaller.cs` | Uses fastboot for flashing |
| `AdbClient.cs` | Reboot to fastboot mode |
| `DownloadModeFlasher.cs` | Alternative: Odin/Download Mode |

## References

1. **Android Fastboot Protocol** - `system/core/fastboot/protocol.txt`
2. **Android Fastboot Implementation** - `system/core/fastboot/fastboot.cpp`
3. **Android Sparse Format** - `system/extras/ext4_utils/sparse_format.h`
4. **Magisk flash.sh** - `scripts/flash.sh`
5. **Android Dynamic Partitions** - `system/tools/mkbootimg/dynamic_partitions.py`

## Testing Recommendations

### Unit Tests
```csharp
[Test] void ConnectAsync_ValidDevice_Connects()
[Test] void GetVarAsync_KnownVariable_ReturnsValue()
[Test] void GetAllVarsAsync_ReturnsDictionary()
[Test] void FlashAsync_RawImage_ChunksAndSends()
[Test] void FlashSparseAsync_SparseImage_ParsesAndFlashes()
[Test] void EraseAsync_ValidPartition_SendsCommand()
[Test] void SetActiveAsync_SlotA_SwitchesSlot()
[Test] void RebootAsync_SendsReboot()
[Test] void UnlockBootloaderAsync_SendsUnlockCommand()
[Test] void BootAsync_Image_SendsBootCommand()
```

### Integration Tests (Requires Device in Fastboot)
```csharp
[Test] void SamsungS24_FastbootConnect_GetProduct()
[Test] void SamsungS24_FlashBootAndInitBoot_BootsStock()
[Test] void SamsungS24_FlashSparseSuperImg_FlashesCorrectly()
```

## Cross-Reference
See `knowledge-base/cross-reference-map/FastbootClient.cs.md` for line-by-line source mapping.