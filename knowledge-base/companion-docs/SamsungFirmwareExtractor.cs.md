# Companion Documentation: BACKRabbit.Firmware.SamsungFirmwareExtractor.cs

## Purpose
Extracts Samsung firmware partitions from official `.tar.md5` firmware files and parses sparse `super.img` images. Essential for obtaining stock boot/init_boot/vbmeta images for Magisk uninstallation.

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                  SamsungFirmwareExtractor                           │
├─────────────────────────────────────────────────────────────────────┤
│  ExtractTarMd5(path) ──→ FirmwarePackage                            │
│       │                                                             │
│       ├─→ Read file, verify MD5 footer (last 16 bytes)             │
│       ├─→ Open TAR archive                                         │
│       ├─→ Extract each partition to dictionary                     │
│       ├─→ Normalize partition names (lowercase, no ext)            │
│       └─→ Parse metadata from filename                             │
│                                                                     │
│  ExtractSuperImg(path) ──→ Dictionary<partition, byte[]>           │
│       │                                                             │
│       ├─→ Check sparse magic (0xED26)                              │
│       ├─→ If sparse: Parse + convert to raw                        │
│       └─→ If raw: Return as-is                                     │
│                                                                     │
│  GetPartitionName(entry) ──→ string                                │
│  ParseFirmwareMetadata(filename) ──→ FirmwareMetadata              │
└─────────────────────────────────────────────────────────────────────┘
```

## Public API

### Firmware Extraction
```csharp
/// <summary>
/// Extract all partitions from Samsung .tar.md5 firmware file
/// </summary>
/// <param name="tarMd5Path">Path to AP_*.tar.md5 file</param>
/// <returns>FirmwarePackage with partitions and metadata</returns>
public static FirmwarePackage ExtractTarMd5(string tarMd5Path)

/// <summary>
/// Extract partitions from super.img (sparse or raw)
/// </summary>
public static Dictionary<string, byte[]> ExtractSuperImg(string superImgPath)
```

### Result Access
```csharp
// Get specific partition by name (case-insensitive)
var boot = package.GetPartition("boot");
var initBoot = package.GetPartition("init_boot");
var vbmeta = package.GetPartition("vbmeta");
var dtbo = package.GetPartition("dtbo");
var super = package.GetPartition("super");

// List all available partitions
foreach (var name in package.GetPartitionNames()) {
    Console.WriteLine(name);
}
```

## Samsung Firmware Format (.tar.md5)

### File Structure
```
┌─────────────────────────────────────────────────────────────┐
│ TAR Archive (multiple partition images)                     │
├─────────────────────────────────────────────────────────────┤
│ 16-byte MD5 Checksum (of the TAR data only)                │
└─────────────────────────────────────────────────────────────┘
```

### Verification
```csharp
// Embedded MD5 = last 16 bytes
// Calculated MD5 = MD5(file_data_without_last_16_bytes)
// Must match exactly
```

## Samsung Firmware Types

| Prefix | Type | Description | Key Partitions |
|--------|------|-------------|----------------|
| `AP_|
| `AP_` | Application Processor | Main Android OS | boot, init_boot, vbmeta, dtbo, super |
| `CP_` | Cellular Processor | Modem/baseband | modem.img |
| `BL_` | Bootloader | Samsung bootloader | sboot, cm, upsbl |
| `CSC_` | Carrier Customization | Region/carrier configs | - |
| `HOME_` | Home CSC | User data/apps | - |

## Filename Format
```
AP_SM-S921B_14.0.0.XXX_20240101_REV00.tar.md5
│   │           │           │         │
│   │           │           │         └─ Revision
│   │           │           └─ Build date (YYYYMMDD)
│   │           └─ Android version
│   └─ Model (SM-S921B = S24 Ultra)
└─ Type (AP)
```

## S24/S25/Z Fold 7 (GKI 2.0) Partitions

### Critical for Magisk Uninstall
| Partition | In AP Tar | Description |
|-----------|-----------|-------------|
| `boot` | ✅ | **Kernel only** (no ramdisk) |
| `init_boot` | ✅ | **Ramdisk only** |
| `vbmeta` | ✅ | AVB verification metadata |
| `vbmeta_system` | ✅ | System vbmeta |
| `vbmeta_vendor` | ✅ | Vendor vbmeta |
| `dtbo` | ✅ | DTB overlay |
| `super` | ✅ | Dynamic partitions (sparse) |

### Flash Order (Samsung)
```bash
# Via Odin (Download Mode) - recommended
# Or via Fastboot (if unlocked):
fastboot flash boot boot.img
fastboot flash init_boot init_boot.img
fastboot flash vbmeta vbmeta.img
fastboot flash vbmeta_system vbmeta_system.img
fastboot flash vbmeta_vendor vbmeta_vendor.img
fastboot flash dtbo dtbo.img
fastboot flash super super.img  # sparse
```

---

## Usage Examples

### Extract Stock Images for Uninstall
```csharp
var extractor = new SamsungFirmwareExtractor();

// 1. Extract AP firmware (contains boot/init_boot/vbmeta/dtbo)
var apPackage = extractor.ExtractTarMd5("AP_SM-S921B_*.tar.md5");

Console.WriteLine($"Model: {apPackage.Metadata.Model}");
Console.WriteLine($"Partitions: {string.Join(", ", apPackage.GetPartitionNames())}");

// 2. Get stock images
var bootImg = apPackage.GetPartition("boot");
var initBootImg = apPackage.GetPartition("init_boot");
var vbmetaImg = apPackage.GetPartition("vbmeta");
var dtboImg = apPackage.GetPartition("dtbo");

// 3. Save for flashing
File.WriteAllBytes("boot_stock.img", bootImg);
File.WriteAllBytes("init_boot_stock.img", initBootImg);
File.WriteAllBytes("vbmeta_stock.img", vbmetaImg);
File.WriteAllBytes("dtbo_stock.img", dtboImg);
```

### Extract super.img (Dynamic Partitions)
```csharp
// Get super partition
var superData = apPackage.GetPartition("super");
if (superData != null) {
    // Check if sparse
    var isSparse = superData.Length >= 2 && 
        BinaryPrimitives.ReadUInt16LittleEndian(superData) == 0xED26;
    
    if (isSparse) {
        // ExtractSuperImg handles sparse automatically
        var partitions = SamsungFirmwareExtractor.ExtractSuperImg("super.img");
        // Contains: system, vendor, product, system_ext, odm, etc.
    }
}
```

### Batch Extract All Firmware Types
```csharp
var firmwareFiles = Directory.GetFiles("firmware", "*.tar.md5");

foreach (var file in firmwareFiles) {
    try {
        var package = SamsungFirmwareExtractor.ExtractTarMd5(file);
        Console.WriteLine($"{package.Metadata.Type} ({package.Metadata.Model}): {package.GetPartitionNames().Count()} partitions");
    } catch (Exception ex) {
        Console.WriteLine($"Failed {file}: {ex.Message}");
    }
}
```

### Verify Firmware Integrity
```csharp
try {
    var package = SamsungFirmwareExtractor.ExtractTarMd5("AP_SM-S921B_*.tar.md5");
    Console.WriteLine("✓ MD5 verified - firmware intact");
} catch (InvalidDataException ex) {
    Console.WriteLine($"✗ MD5 verification failed: {ex.Message}");
    Console.WriteLine("Firmware file is corrupted or modified!");
}
```

---

## Integration with Magisk Uninstaller

```csharp
// Complete stock restore workflow
var extractor = new SamsungFirmwareExtractor();
var fastboot = new FastbootClient();
var usb = new UsbDeviceManager();

// 1. Extract stock firmware
var ap = extractor.ExtractTarMd5("AP_SM-S921B_*.tar.md5");

// 2. Connect in fastboot mode
await fastboot.ConnectAsync(usb);

// 3. Flash stock images
await fastboot.FlashAsync("boot", ap.GetPartition("boot")!);
await fastboot.FlashAsync("init_boot", ap.GetPartition("init_boot")!);
await fastboot.FlashAsync("vbmeta", ap.GetPartition("vbmeta")!);
await fastboot.FlashAsync("dtbo", ap.GetPartition("dtbo")!);

// 4. Flash super (sparse)
var superData = ap.GetPartition("super");
if (superData != null) {
    await fastboot.FlashSparseAsync("super", superData);
}

// 5. Reboot
await fastboot.RebootAsync();
```

---

## Limitations

1. **Super.img Logical Partitions** - Returns raw super, doesn't unpack system/vendor/product
2. **LZ4 Ramdisks** - Some Samsung ramdisks use LZ4 compression in tar (handled by CompressionEngine)
3. **Multi-file Firmware** - Some firmware split across multiple .tar.md5 files
4. **Encrypted Firmware** - Newer firmwares (Android 14+) may be encrypted
5. **VBmeta Chaining** - Doesn't auto-handle vbmeta_system/vbmeta_vendor chaining
6. **Region/CSC Variants** - Multiple CSC files for different regions

---

## Related Files

| File | Relationship |
|------|--------------|
| `FastbootClient.cs` | Flash extracted partitions |
| `SparseImage.cs` | Parse sparse super.img |
| `BootImageParser.cs` | Verify extracted boot images |
| `MagiskUninstaller.cs` | Uses stock images for uninstall |
| `DownloadModeFlasher.cs` | Alternative: Odin protocol |

---

## References

1. **Samsung Firmware Format** - Community documented (SamMobile, XDA)
2. **Android Sparse Image** - `system/extras/ext4_utils/sparse_format.h`
3. **Magisk firmware.sh** - `scripts/firmware.sh`
4. **Magisk flash.sh** - `scripts/flash.sh`

---

## Test Coverage Needed

```csharp
[Test] void ExtractTarMd5_ValidAP_ReturnsPartitions()
[Test] void ExtractTarMd5_InvalidMD5_ThrowsInvalidDataException()
[Test] void ExtractTarMd5_PartitionNamesNormalized()
[Test] void ExtractSuperImg_SparseMagic_ConvertsToRaw()
[Test] void ExtractSuperImg_NoSparseMagic_ReturnsAsIs()
[Test] void GetPartition_CaseInsensitive()
[Test] void ParseFirmwareMetadata_APFormat_ParsesModel()