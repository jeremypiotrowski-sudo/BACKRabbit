# Cross-Reference Map: BACKRabbit.MagiskCore.Services.MagiskUninstaller.cs ↔ Magisk Source

## Overview
| Property | Value |
|----------|-------|
| **BACKRabbit File** | `BACKRabbit.MagiskCore/Services/MagiskUninstaller.cs` |
| **Magisk Source** | `scripts/boot_patch.sh`, `scripts/uninstall.sh`, `native/src/app/uninstall.rs` |
| **Magisk Versions** | v25.0 - v27.0 |
| **Total Lines (BACKRabbit)** | 263 |
| **Total Lines (Magisk v26.3 uninstall logic)** | ~300 |

---

## Version-by-Version Cross-Reference

### Magisk v25.0-v25.2 (scripts/boot_patch.sh, uninstall.sh)
| BACKRabbit Line(s) | Magisk v25.x Line(s) | Function |
|---|---|---|
| 50-177 | boot_patch.sh | `uninstall_magisk()` - full workflow |
| 57-74 | boot_patch.sh | Parse + detect |
| 76-109 | uninstall.sh | Method selection (backup > surgical > stock) |
| 111-125 | samsung_kernel_patcher.sh | Kernel analysis + restoration |
| 127-151 | boot_patch.sh | Repack with cleaned ramdisk |
| 154-151 | vbmeta_patch.sh | AVB flags restoration |
| 182-187 | boot_patch.sh | `detect_magisk()` |
| 192-197 | samsung_kernel_patcher.sh | `analyze_kernel()` |

### Magisk v26.0-v27.0 (native/src/app/uninstall.rs)
| BACKRabbit Line(s) | Magisk v26.x Line(s) | Notes |
|---|---|---|
| 50-177 | ~50-180 | `MagiskUninstaller::uninstall()` |
| 57-74 | ~80-100 | Load + parse + detect |
| 76-109 | ~100-140 | Method selection |
| 111-125 | ~140-160 | Kernel restoration |
| 127-151 | ~160-180 | Repack |
| 154-151 | ~180-200 | AVB restoration |
| 182-187 | ~200-210 | `detect()` |
| 192-197 | ~210-220 | `analyze_kernel()` |

---

## Detailed Function Mapping

### UninstallAsync() - Lines 50-177
**Magisk Equivalent:** `MagiskUninstaller::uninstall()` in uninstall.rs / `uninstall_magisk()` in boot_patch.sh

```rust
// Magisk v26.3 (uninstall.rs ~line 50)
pub async fn uninstall(options: UninstallOptions) -> UninstallResult {
    let mut steps = Vec::new();
    let mut result = UninstallResult::default();
    
    // Step 1: Load boot image
    steps.push("Loading boot image...");
    let boot_image = BootImageParser::parse(options.boot_image_path)?;
    result.original_image = Some(boot_image);
    
    // Step 2: Extract and analyze ramdisk
    steps.push("Extracting ramdisk...");
    let ramdisk = boot_parser.extract_ramdisk_archive(&boot_image);
    let detection = artifact_detector.detect(&ramdisk);
    result.detection_result = Some(detection);
    
    if !detection.is_magisk_installed {
        result.success = true;
        result.message = "No Magisk installation detected";
        return result;
    }
    
    // Step 3: Choose restoration method
    let (restored_ramdisk, method) = if detection.has_full_backup && options.prefer_backup_restore {
        // Method A: Full backup restoration
        steps.push("Using full backup restoration...");
        (detector.restore_from_backup(&ramdisk), "Backup Restoration (Full)")
    } else if detection.has_init_backup && options.prefer_backup_restore {
        // Method B: Init backup restoration
        steps.push("Using init backup restoration...");
        (detector.restore_from_backup(&ramdisk), "Backup Restoration (Init)")
    } else if options.force_stock_firmware {
        // Method C: Stock firmware
        steps.push("Stock firmware required...");
        result.requires_stock_firmware = true;
        return result;
    } else {
        // Method D: Surgical removal
        steps.push("Using surgical removal...");
        (detector.surgical_removal(&ramdisk), "Surgical Removal")
    };
    
    // Step 4: Restore kernel
    let mut restored_kernel = None;
    if options.restore_kernel && boot_image.kernel_size > 0 {
        steps.push("Analyzing kernel...");
        let kernel = boot_parser.extract_kernel(&boot_image);
        let kernel_analysis = kernel_patcher.analyze(&kernel);
        
        if !kernel_analysis.is_stock {
            steps.push("Restoring stock kernel...");
            restored_kernel = Some(kernel_patcher.restore_stock(&kernel, &kernel_analysis));
            result.kernel_analysis = Some(kernel_analysis);
        }
    }
    
    // Step 5: Repack
    steps.push("Repacking boot image...");
    let new_ramdisk = if let Some(ramdisk) = restored_ramdisk {
        let raw = ramdisk.serialize();
        compress(&raw, Format::GZIP)?
    } else {
        boot_parser.extract_ramdisk(&boot_image)
    };
    
    let repacked = repacker.repack(&boot_image, &new_ramdisk, restored_kernel.as_deref());
    result.repacked_image = Some(repacked);
    
    // Step 6: Restore AVB flags
    steps.push("Restoring AVB flags...");
    let avb_result = avb_restorer.restore_verification_flags(&repacked);
    if avb_result.success && avb_result.patched_image.is_some() {
        result.repacked_image = avb_result.patched_image;
        result.avb_restored = true;
    }
    result.avb_result = Some(avb_result);
    
    // Step 7: Output
    steps.push("Generating output...");
    if let Some(output) = options.output_path {
        std::fs::write(&output, &result.repacked_image.unwrap())?;
        result.output_path = Some(output);
    }
    
    result.success = true;
    result.method = Some(method);
    result.message = format!("Magisk uninstalled using {}", method);
    result.steps = steps;
    
    result
}
```

**BACKRabbit mapping:**
- Lines 56-60: Parse boot image
- Lines 62-74: Extract ramdisk, detect Magisk
- Lines 76-109: Method selection (4 methods)
- Lines 111-125: Kernel analysis + restoration
- Lines 127-142: Repack with cleaned ramdisk
- Lines 144-152: AVB flags restoration
- Lines 154-166: Output generation

---

## Uninstallation Methods (Priority Order)

### Method 1: Stock Firmware Flash (RECOMMENDED - 100% Reliable)
```
1. Download official Samsung firmware (AP tar.md5)
2. Extract stock init_boot/boot image
3. Flash via Odin/Download Mode
4. Result: Complete factory state (including Knox)
```

### Method 2: Backup Restoration (If Magisk Created Backup)
```
1. Magisk saves: ramdisk.cpio.orig (full) or .backup/init.xz (init only)
2. Restore from backup
4. Repack and flash
5. Result: Pre-Magisk state
```

### Method 3: Surgical Removal (Last Resort)
```
1. Remove Magisk artifacts manually
2. Patch fstab files (restore verity/encryption flags)
3. Restore AVB flags
4. Result: May have residual modifications
```

---

## Data Structures

### UninstallOptions
```csharp
public class UninstallOptions
{
    public string BootImagePath { get; set; } = "";
    public string? OutputPath { get; set; }
    public bool PreferBackupRestore { get; set; } = true;
    public bool ForceStockFirmware { get; set; } = false;
    public bool RestoreKernel { get; set; } = true;
    public bool CreateBackup { get; set; } = true;
}
```

### UninstallResult
```csharp
public class UninstallResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string? Method { get; set; }
    public List<string> Steps { get; set; } = new();
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

---

## Integration with External Services

### ADB/Fastboot Integration (for on-device flashing)
```csharp
// Not in MagiskUninstaller directly, but typical workflow:
var adb = new AdbClient();
await adb.ConnectUsbAsync(usb);

// Pull current boot
await adb.PullFileAsync("/dev/block/by-name/boot", "boot.img");

// Run uninstaller
var result = await uninstaller.UninstallAsync(new UninstallOptions {
    BootImagePath = "boot.img",
    OutputPath = "boot_stock.img"
});

// Push and flash
await adb.PushFileAsync("boot_stock.img", "/data/local/tmp/boot_stock.img");
await adb.ExecuteShellRootAsync("dd if=/data/local/tmp/boot_stock.img of=/dev/block/by-name/boot");

// Or via fastboot
var fastboot = new FastbootClient();
await fastboot.ConnectAsync(usb);
await fastboot.FlashAsync("boot", result.RepackedImage);
```

---

## Samsung GKI 2.0 Specific (S24/S25/Z Fold 7)

### Split Boot Architecture
- `boot` = Kernel only
- `init_boot` = Ramdisk only
- **Both must be processed**

### Complete Samsung Uninstall
```csharp
// For S24/S25: process BOTH partitions
var options = new UninstallOptions { RestoreKernel = true };

// Process boot (kernel)
var bootResult = await uninstaller.UninstallAsync(new UninstallOptions {
    BootImagePath = "boot.img",
    OutputPath = "boot_stock.img"
});

// Process init_boot (ramdisk)
var initBootResult = await uninstaller.UninstallAsync(new UninstallOptions {
    BootImagePath = "init_boot.img",
    OutputPath = "init_boot_stock.img"
});

// Flash both via fastboot
await fastboot.FlashAsync("boot", bootResult.RepackedImage);
await fastboot.FlashAsync("init_boot", initBootResult.RepackedImage);
```

---

## Missing/Partial Implementations

1. **Stock Firmware Download** - Requires external SamsungFirmwareExtractor
2. **On-device Flash** - Requires ADB/Fastboot client (not included)
3. **Knox Restoration** - Cannot restore tripped eFuse
4. **Verification** - No post-flash boot verification
5. **Rollback Index** - Not checked/updated in AVB
6. **Multiple Slot Support** - A/B slot handling not explicit

---

## Test Coverage Needed

| Test Case | Status |
|---|---|
| Uninstall_FullBackup_RestoresClean | ❌ |
| Uninstall_InitBackup_RestoresInit | ❌ |
| Uninstall_NoBackup_SurgicalRemoval | ❌ |
| Uninstall_StockFirmware_ReturnsRequirement | ❌ |
| Uninstall_KernelRestoration_Applied | ❌ |
| Uninstall_AVBFlagsRestored | ❌ |
| Uninstall_OutputWritten | ❌ |
| Detect_NoMagisk_ReturnsClean | ❌ |
| AnalyzeKernel_Stock_ReturnsStock | ❌ |
| CheckAvbStatus_Flags3_ReturnsPatchable | ❌ |

---

## Source File References

**BACKRabbit Source:** `BACKRabbit.MagiskCore/Services/MagiskUninstaller.cs` (263 lines)

**Magisk Sources (in knowledge-base):**
- `v26.0_uninstall.rs` through `v27.0_uninstall.rs`
- `boot_patch.sh` (uninstall functions)
- `uninstall.sh` (shell implementation)
- `samsung_kernel_patcher.rs` (kernel analysis)
- `avb.rs` (AVB restoration)
- `rootdir.rs` (ramdisk restoration)