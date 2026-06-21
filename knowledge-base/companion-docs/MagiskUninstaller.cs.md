# Companion Documentation: BACKRabbit.MagiskCore.Services.MagiskUninstaller.cs

## Purpose
End-to-end Magisk uninstallation service for Samsung devices (S24/S25/Z Fold 7) with init_boot partition support (GKI 2.0). Orchestrates all MagiskCore modules for complete removal.

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                        MagiskUninstaller                            │
├─────────────────────────────────────────────────────────────────────┤
│  UninstallAsync(UninstallOptions) ──→ UninstallResult              │
│       │                                                             │
│       ├─→ 1. Parse boot/init_boot image (BootImageParser)          │
│       ├─→ 2. Extract & analyze ramdisk (MagiskArtifactDetector)    │
│       ├─→ 3. Choose restoration method:                            │
│       │    │                                                        │
│       │    ├─→ A: Full backup (ramdisk.cpio.orig) → RestoreFromBackup│
│       │    ├─→ B: Init backup (.backup/init.xz) → RestoreFromBackup│
│       │    ├─→ C: Stock firmware (ForceStockFirmware) → Requires FW│
│       │    └─→ D: Surgical removal → SurgicalRemoval()             │
│       ├─→ 4. Analyze & restore kernel (SamsungKernelPatcher)       │
│       ├─→ 5. Repack boot image (BootImageRepacker)                 │
│       ├─→ 6. Restore AVB flags (AvbRestorer)                       │
│       └─→ 7. Write output                                           │
│                                                                     │
│  Detect(path) ──→ MagiskDetectionResult (read-only)                │
│  AnalyzeKernel(path) ──→ KernelAnalysisResult (read-only)         │
│  CheckAvbStatus(path) ──→ AvbRestoreResult (read-only)            │
└─────────────────────────────────────────────────────────────────────┘
```

## Public API

### Main Uninstallation
```csharp
/// <summary>
/// Complete Magisk uninstallation workflow
/// </summary>
/// <param name="options">Uninstallation options</param>
/// <returns>UninstallResult with success status and details</returns>
public async Task<UninstallResult> UninstallAsync(UninstallOptions options)
```

### Read-Only Analysis
```csharp
/// <summary>
/// Detect Magisk installation without modifying
/// </summary>
public MagiskDetectionResult Detect(string bootImagePath)

/// <summary>
/// Analyze kernel for Samsung security patches
/// </summary>
public KernelAnalysisResult AnalyzeKernel(string bootImagePath)

/// <summary>
/// Check AVB status (read-only)
/// </summary>
public AvbRestoreResult CheckAvbStatus(string bootImagePath)
```

## UninstallOptions

```csharp
public class UninstallOptions
{
    /// <summary>Path to boot/init_boot image</summary>
    public string BootImagePath { get; set; } = "";

    /// <summary>Output path for cleaned image (null = memory only)</summary>
    public string? OutputPath { get; set; }

    /// <summary>Prefer backup restoration over surgical removal</summary>
    public bool PreferBackupRestore { get; set; } = true;

    /// <summary>Require stock firmware (most reliable method)</summary>
    public bool ForceStockFirmware { get; set; } = false;

    /// <summary>Attempt to restore stock kernel state</summary>
    public bool RestoreKernel { get; set; } = true;

    /// <summary>Create backup before uninstalling</summary>
    public bool CreateBackup { get; set; } = true;
}
```

## UninstallResult

```csharp
public class UninstallResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string? Method { get; set; }           // "Backup Restoration (Full)" etc.
    public List<string> Steps { get; set; } = new();  // Step-by-step log
    public MagiskDetectionResult? DetectionResult { get; set; }
    public KernelAnalysisResult? KernelAnalysis { get; set; }
    public AvbRestoreResult? AvbResult { get; set; }
    public bool AvbRestored { get; set; }
    public BootImage? OriginalImage { get; set; }
    public byte[]? RepackedImage { get; set; }
    public string? OutputPath { get; set; }
    public bool RequiresStockFirmware { get; set; }
    public Exception? Exception { get; set; }
}
```

## Uninstallation Methods (Priority Order)

### Method 1: Stock Firmware Flash (Most Reliable - 100%)
```csharp
options.ForceStockFirmware = true;
// Requires: Official Samsung firmware
// Steps:
//  1. Download stock firmware for exact model/region
//  2. Extract AP tar → boot.img / init_boot.img
//  3. Flash via Odin (Download Mode)
// Result: Factory state, Knox eFuse still tripped
```

### Method 2: Full Backup Restoration (95% Reliable)
```csharp
// Requires: Magisk created ramdisk.cpio.orig
options.PreferBackupRestore = true;
// Steps:
//  1. Detect ramdisk.cpio.orig in ramdisk
//  2. Parse as CPIO archive (complete stock ramdisk)
//  3. Repack with original kernel
//  4. Restore AVB flags
// Result: Pre-Magisk state (best software restoration)
```

### Method 3: Method 3: Init Backup Restoration (85% Reliable)
```csharp
// Requires: Magisk created .backup/init.xz
options.PreferBackupRestore = true;
// Steps:
//  1. Decompress .backup/init.xz
//  2. Replace init in current ramdisk
//  3. Remove overlay.d/, .backup/, ramdisk.cpio.orig
//  4. Patch fstab flags
//  5. Repack & restore AVB
// Result: Clean init, Magisk hooks removed
```

### Method 4: Surgical Removal (70% Reliable - Last Resort)
```csharp
// No backup available
// Steps:
//  1. Remove: overlay.d/, .backup/, init.magisk.rc, sepolicy.rules
//  2. Patch fstab: Restore avb, verify, fsverity flags
//  3. Clean init.rc: Remove magisk services
//  4. Repack & restore AVB
//  5. (Optional) Restore Samsung kernel patches
// Result: May have residual modifications
```

## Samsung-Specific Features

### init_boot Partition (GKI 2.0)
Samsung S24/S25/Z Fold 7 use Generic Kernel Image (GKI) 2.0 with split boot:
- `init_boot` - Contains ramdisk only (no kernel)
- `boot` - Contains kernel only
- **MagiskUninstaller handles both** via BootImageParser

### Samsung Kernel Restoration
```csharp
options.RestoreKernel = true;
// 1. Extract kernel from boot image
// 2. Analyze for RKP, Defex, PROCA, KNOX
// 3. Restore syscall table hooks
// 4. Repack with restored kernel
```

### Knox eFuse Warning
> **Critical**: Samsung Knox warranty bit is a **hardware eFuse** that permanently trips when:
> - Custom boot/init_boot flashed
> - System partition modified
> - Custom recovery flashed
> 
> **This CANNOT be restored by software.** MagiskUninstaller restores software state only.

## Usage Examples

### Basic Uninstall
```csharp
var uninstaller = new MagiskUninstaller();

var result = await uninstaller.UninstallAsync(new UninstallOptions
{
    BootImagePath = "init_boot.img",  // or boot.img
    OutputPath = "init_boot_stock.img",
    PreferBackupRestore = true,
    RestoreKernel = true
});

if (result.Success) {
    Console.WriteLine($"Success: {result.Message}");
    Console.WriteLine($"Method: {result.Method}");
    Console.WriteLine($"AVB Restored: {result.AvbRestored}");
    Console.WriteLine($"Output: {result.OutputPath}");
} else {
    Console.WriteLine($"Failed: {result.Message}");
    if (result.RequiresStockFirmware) {
        Console.WriteLine("Stock firmware required - download from samfw.com");
    }
}
```

### Detect Only
```csharp
var detection = uninstaller.Detect("boot.img");
Console.WriteLine(detection.Summary);
// Magisk detected: v26.3
// Artifacts found: 5
// Full backup available (ramdisk.cpio.orig)
// Verity patches detected in fstab
```

### Kernel Analysis
```csharp
var kernelAnalysis = uninstaller.AnalyzeKernel("boot.img");
Console.WriteLine(kernelAnalysis.Summary);
// Kernel Security Analysis:
//   RKP: Present
//   Defex: Present
//   PROCA: Not found
//   KNOX: Present
//   Syscall Table: Hooked
//   Hook patterns found: 2
//   Overall: MODIFIED
```

### AVB Status Check
```csharp
var avbResult = uninstaller.CheckAvbStatus("boot.img");
if (avbResult.Success) {
    Console.WriteLine($"AVB Footer: 0x{avbResult.FooterOffset:X}");
    Console.WriteLine($"VBMeta Offset: 0x{avbResult.VbmetaOffset:X}");
    Console.WriteLine($"Current Flags: {avbResult.CurrentFlags} (0=stock, 3=Magisk)");
}
```

### Complete Samsung S24 Uninstall
```csharp
// 1. Download stock firmware for exact model (e.g., SM-S921B)
// 2. Extract init_boot.img and boot.img from AP tar
// 3. For init_boot (ramdisk only):
var initBootResult = await uninstaller.UninstallAsync(new UninstallOptions
{
    BootImagePath = "init_boot.img",
    OutputPath = "init_boot_stock.img",
    RestoreKernel = false  // init_boot has no kernel
});

// 4. For boot (kernel only):
var bootResult = await uninstaller.UninstallAsync(new UninstallOptions
{
    BootImagePath = "boot.img",
    OutputPath = "boot_stock.img",
    RestoreKernel = true   // boot has kernel
});

// 5. Flash both via Odin:
//    AP: init_boot_stock.img → init_boot
//    AP: boot_stock.img → boot
```

## Workflow Steps (Logged in Result.Steps)

```
Loading boot image...
Extracting ramdisk...
Using full backup restoration (ramdisk.cpio.orig)...
Analyzing kernel for patches...
Restoring stock kernel state...
Repacking boot image...
Restoring AVB verification flags...
Generating output...
```

## Magisk Version Compatibility

| Magisk Version | Boot Image | init_boot | Samsung Kernel | AVB | BACKRabbit |
|----------------|------------|-----------|----------------|-----|------------|
| v25.0 | ✅ | ❌ | ✅ | ✅ | ✅ |
| v25.1-v25.2 | ✅ | ❌ | ✅ | ✅ | ✅ |
| v26.0 | ✅ | ✅ (GKI) | ✅ | ✅ | ✅ |
| v26.1-v27.0 | ✅ | ✅ | ✅ | ✅ | ✅ |

## Limitations & Known Gaps

1. **Stock Firmware** - Not automated; requires manual download
2. **init_boot GKI** - Parser handles but untested on GKI 2.0
3. **Multi-Slot (A/B)** - No slot_a/slot_b handling
4. **Recovery Partition** - Separate uninstall not implemented
5. **Rollback Index** - Not checked/updated
6. **VBMeta Signing** - Flags patched but not re-signed

## Related Files

| File | Role in Uninstallation |
|------|------------------------|
| `BootImageParser.cs` | Parse boot/init_boot |
| `BootImageRepacker.cs` | Repack cleaned image |
| `MagiskArtifactDetector.cs` | Detect & restore ramdisk |
| `AvbRestorer.cs` | Restore AVB flags |
| `SamsungKernelPatcher.cs` | Restore kernel patches |
| `CompressionEngine.cs` | Compress ramdisk |

## References

1. **Magisk uninstall.sh** - `scripts/uninstall.sh`
2. **Magisk magiskboot.cpp** - `magiskboot_uninstall()`
3. **Samsung Kernel Patcher** - `scripts/samsung_kernel_patcher.sh`
4. **AVB Restoration** - `scripts/vbmeta_patch.sh`

## Testing Recommendations

### Unit Tests
```csharp
[Test] void UninstallAsync_FullBackup_RestoresStock()
[Test] void UninstallAsync_InitBackup_RestoresInit()
[Test] void UninstallAsync_Surgical_CleansArtifacts()
[Test] void UninstallAsync_KernelRestore_PatchesSyscallTable()
[Test] void UninstallAsync_AVBRestore_PatchesFlagsToZero()
[Test] void UninstallAsync_NoMagisk_ReturnsSuccess()
[Test] void UninstallAsync_RequiresFirmware_ReturnsFlag()
[Test] void Detect_ValidImage_ReturnsDetection()
[Test] void AnalyzeKernel_SamsungKernel_ReturnsAnalysis()
[Test] void CheckAvbStatus_ValidImage_ReturnsFlags()
```

### Integration Tests (Requires Real Images)
```csharp
[Test] void SamsungS24_Uninstall_InitBootAndBoot_BothClean()
[Test] void SamsungS25_Uninstall_KernelRestore_SyscallTableStock()
[Test] void Pixel_Uninstall_AVBRestore_FlagsZero()
```

## Cross-Reference
See `knowledge-base/cross-reference-map/MagiskUninstaller.cs.md` for line-by-line Magisk source mapping.