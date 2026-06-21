# Companion Documentation: BACKRabbit.MagiskCore.AvbRestorer.AvbRestorer.cs

## Purpose
AVB (Android Verified Boot) restoration service that restores verification flags from Magisk-patched state (flags=3, disabled) to stock state (flags=0, enabled). Critical for complete Magisk uninstallation.

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                        AvbRestorer                                  │
├─────────────────────────────────────────────────────────────────────┤
│  RestoreVerificationFlags(bootImage)                               │
│       │                                                             │
│       ├─→ Find AVB footer ("AVB0" magic in last 1KB)              │
│       ├─→ Read footer: vbmeta_offset, vbmeta_size                  │
│       ├─→ Calculate vbmeta_start = footer_offset - vbmeta_offset  │
│       ├─→ Read vbmeta header at vbmeta_start                       │
│       ├─→ Check flags at offset 88                                 │
│       │     ├─→ flags=3 (Magisk): Patch to 0, return patched      │
│       │     ├─→ flags=0 (Stock): Return unchanged                 │
│       │     └─→ Other: Error                                       │
│       └─→ Return AvbRestoreResult                                  │
│                                                                     │
│  PatchVbmetaPartition(vbmetaData)                                  │
│       │                                                             │
│       ├─→ Validate "AVB0" magic at offset 0                        │
│       ├─→ Read flags at offset 88                                  │
│       ├─→ If flags=3: Patch to 0                                   │
│       └─→ Return result                                            │
└─────────────────────────────────────────────────────────────────────┘
```

## Public API

### Boot Image Restoration
```csharp
/// <summary>
/// Restore AVB verification flags in a boot image
/// </summary>
/// <param name="bootImage">Raw boot image data</param>
/// <returns>AvbRestoreResult with patched image if successful</returns>
public AvbRestoreResult RestoreVerificationFlags(byte[] bootImage)
```

### Standalone vbmeta Partition
```csharp
/// <summary>
/// Patch vbmeta partition directly (for separate vbmeta partition)
/// </summary>
public AvbRestoreResult PatchVbmetaPartition(byte[] vbmetaData)
```

## Result Structure
```csharp
public class AvbRestoreResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public bool FooterFound { get; set; }
    public long FooterOffset { get; set; }
    public ulong OriginalImageSize { get; set; }
    public ulong VbmetaOffset { get; set; }
    public ulong VbmetaSize { get; set; }
    public uint CurrentFlags { get; set; }
    public bool FlagsChanged { get; set; }
    public byte[]? PatchedImage { get; set; }
}
```

## AVB Flags Reference

| Value | Binary | Meaning |
|-------|--------|---------|
| 0 | `00` | **Stock** - Verification enabled |
| 1 | `01` | Verity disabled |
| 2 | `10` | Verification disabled |
| **3** | `11` | **Magisk patched** - Both disabled |

**Magisk sets flags=3** to disable both dm-verity and dm-verification.

## Boot Image AVB Structure

```
Boot Image Layout:
┌─────────────────────────────────────────────────┐
│ Header (512/4096 bytes)                         │
├─────────────────────────────────────────────────┤
│ Kernel (page-aligned)                           │
├─────────────────────────────────────────────────┤
│ Ramdisk (page-aligned, compressed)              │
├─────────────────────────────────────────────────┤
│ Second stage (if present)                       │
├─────────────────────────────────────────────────┤
│ DTB / Recovery DTBO (if present)                │
├─────────────────────────────────────────────────┤
│ Signature (v4)                                  │
├─────────────────────────────────────────────────┤
│ VBMETA (AVB verification metadata)              │
│ ┌─────────────────────────────────────────────┐ │
│ │ magic: "AVB0" (4 bytes)                     │ │
│ │ required_libavb_version (8 bytes)           │ │
│ │ authentication_data_block_size (8 bytes)    │ │
│ │ auxiliary_data_block_size (8 bytes)         │ │
│ │ images[64] (512 bytes)                      │ │
│ │ rollback_index_location (1 byte)            │ │
│ │ rollback_index (8 bytes)                    │ │
│ │ FLAGS ← OFFSET 88 (4 bytes) ← CRITICAL      │ │
│ │ release_string...                           │ │
│ └─────────────────────────────────────────────┘ │
├─────────────────────────────────────────────────┤
│ AVB Footer (64 bytes)                           │
│ ┌─────────────────────────────────────────────┐ │
│ │ magic: "AVB0" (4 bytes)                     │ │
│ │ version (2 bytes)                           │ │
│ │ vbmeta_offset (8 bytes)                     │ │
│ │ vbmeta_size (8 bytes)                       │ │
│ │ original_image_size (8 bytes)               │ │
│ └─────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────┘
```

## Usage Examples

### Restore Flags in Boot Image
```csharp
var restorer = new AvbRestorer();

// Read patched boot image
var bootImage = File.ReadAllBytes("boot_patched.img");

// Restore verification flags
var result = restorer.RestoreVerificationFlags(bootImage);

if (result.Success) {
    Console.WriteLine($"✓ {result.Message}");
    Console.WriteLine($"  Footer at: 0x{result.FooterOffset:X}");
    Console.WriteLine($"  VBMeta at: 0x{result.VbmetaOffset:X}");
    Console.WriteLine($"  Flags: {result.CurrentFlags} → 0");
    Console.WriteLine($"  Changed: {result.FlagsChanged}");
    
    // Write restored image
    if (result.FlagsChanged && result.PatchedImage != null) {
        File.WriteAllBytes("boot_restored.img", result.PatchedImage);
        Console.WriteLine("Restored image written");
    }
} else {
    Console.WriteLine($"✗ {result.Message}");
}
```

### Patch Standalone vbmeta Partition
```csharp
// For devices with separate vbmeta partition
var vbmetaData = File.ReadAllBytes("vbmeta.img");

var result = restorer.PatchVbmetaPartition(vbmetaData);

if (result.Success && result.FlagsChanged) {
    File.WriteAllBytes("vbmeta_patched.img", result.PatchedImage!);
    // Flash via fastboot:
    // fastboot flash vbmeta vbmeta_patched.img
}
```

### Complete Uninstall with AVB
```csharp
var parser = new BootImageParser();
var repacker = new BootImageRepacker();
var restorer = new AvbRestorer();
var adb = new AdbClient();

// 1. Connect and pull boot
await adb.ConnectUsbAsync(usb);
await adb.PullFileAsync("/dev/block/by-name/boot", "boot.img");

// 2. Parse
var bootImg = parser.Parse("boot.img");

// 3. Extract ramdisk, remove Magisk artifacts
var ramdisk = parser.ExtractRamdisk(bootImg);
var cpio = parser.ExtractRamdiskArchive(bootImg);
var artifacts = new MagiskArtifactDetector();
var cleaned = artifacts.CleanRamdisk(cpio);

// 4. Repack with cleaned ramdisk
var repacked = repacker.RepackWithRamdisk(bootImg, cleaned);

// 5. Restore AVB flags
var avbResult = restorer.RestoreVerificationFlags(repacked);
if (avbResult.Success && avbResult.FlagsChanged) {
    repacked = avbResult.PatchedImage!;
}

// 6. Flash
File.WriteAllBytes("boot_stock.img", repacked);
await adb.PushFileAsync("boot_stock.img", "/data/local/tmp/boot_stock.img");
await adb.ExecuteShellRootAsync("dd if=/data/local/tmp/boot_stock.img of=/dev/block/by-name/boot");

// 7. Also patch vbmeta partition if separate
var vbmeta = File.ReadAllBytes("vbmeta.img");
var vbmetaResult = restorer.PatchVbmetaPartition(vbmeta);
if (vbmetaResult.FlagsChanged) {
    File.WriteAllBytes("vbmeta_stock.img", vbmetaResult.PatchedImage!);
    await adb.PushFileAsync("vbmeta_stock.img", "/data/local/tmp/vbmeta_stock.img");
    await adb.ExecuteShellRootAsync("dd if=/data/local/tmp/vbmeta_stock.img of=/dev/block/by-name/vbmeta");
}

await adb.RebootAsync();
```

## Samsung-Specific Notes

### Knox eFuse Warning
> **IMPORTANT**: This restores AVB flags ONLY. It does NOT and CANNOT restore tripped Knox eFuse on Samsung devices.

| What AvbRestorer Does | What It CANNOT Do |
|------------------------|-------------------|
| Restore flags 3→0 in vbmeta | Un-trip Knox eFuse |
| Re-enable dm-verity | Restore Samsung warranty |
| Re-enable dm-verification | Enable Samsung Pay/Secure Folder |
| Allow stock boot verification | Reverse hardware fuse |

**Knox eFuse is permanent** - once tripped by unlocking bootloader, it cannot be reset by software.

### Samsung GKI 2.0 (S24/S25/Z Fold 7)
These devices use split boot:
- `boot` = Kernel only
- `init_boot` = Ramdisk only (has its own AVB)

**Both partitions need AVB restoration:**
```csharp
// Restore boot (kernel)
var bootResult = restorer.RestoreVerificationFlags(bootData);

// Restore init_boot (ramdisk) 
var initBootResult = restorer.RestoreVerificationFlags(initBootData);

// Flash both
await fastboot.FlashAsync("boot", bootResult.PatchedImage);
await fastboot.FlashAsync("init_boot", initBootResult.PatchedImage);
```

## Integration with MagiskUninstaller

```csharp
// MagiskUninstaller uses AvbRestorer internally
var uninstaller = new MagiskUninstaller();

var result = await uninstaller.UninstallAsync(new UninstallOptions
{
    BootImagePath = "boot.img",
    OutputPath = "boot_stock.img",
    RestoreAvbFlags = true,  // Uses AvbRestorer
    UseAdb = true,
    AdbDevice = adb
});
```

## Error Handling

```csharp
var result = restorer.RestoreVerificationFlags(bootImage);

if (!result.Success) {
    switch (result.Message) {
        case "No AVB footer found":
            // Image not AVB signed, or footer corrupted
            break;
        case "Invalid vbmeta offset":
            // Footer corrupted or invalid
            break;
        case "Unknown AVB flags value: X":
            // Unexpected flags (not 0 or 3)
            break;
    }
}
```

## Limitations

1. **No Re-signing** - Patches flags but doesn't re-sign vbmeta (requires private key)
2. **Chained Partitions** - Doesn't handle chained vbmeta partitions
3. **Rollback Index** - Doesn't verify/update rollback index
4. **Multiple VBMeta** - Some devices have multiple vbmeta partitions (vendor_boot, dtbo, etc.)

## Related Files

| File | Relationship |
|------|--------------|
| `BootImageParser.cs` | Finds AVB footer, extracts vbmeta |
| `BootImageRepacker.cs` | Preserves tail (including AVB) during repack |
| `MagiskUninstaller.cs` | Orchestrates full uninstall including AVB |
| `Structures/Avb.cs` | AVB structure definitions |
| `FastbootClient.cs` | Flash restored images |

## References

1. **Android AVB Specification** - `system/libavb/`
2. **Magisk avb.rs** - `native/src/boot/avb.rs`
3. **Magisk vbmeta_patch.sh** - `scripts/vbmeta_patch.sh`
4. **AVB Flags** - `system/libavb/include/libavb/avb_vbmeta_image.h`

## Test Coverage Needed

```csharp
[Test] void FindAvbFooter_ValidImage_ReturnsOffset()
[Test] void FindAvbFooter_NoFooter_ReturnsMinus1()
[Test] void RestoreFlags_Flags3_PatchesTo0()
[Test] void RestoreFlags_Flags0_ReturnsUnchanged()
[Test] void RestoreFlags_UnknownFlags_ReturnsError()
[Test] void PatchVbmetaPartition_ValidVbmeta_PatchesFlags()
[Test] void PatchVbmetaPartition_InvalidMagic_ReturnsError()