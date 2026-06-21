# Companion Documentation: BACKRabbit.MagiskCore.RamdiskEditor.MagiskArtifactDetector.cs

## Purpose
Detects Magisk installation artifacts in a boot image's ramdisk and provides restoration methods to completely uninstall Magisk by restoring the stock ramdisk state.

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                     MagiskArtifactDetector                          │
├─────────────────────────────────────────────────────────────────────┤
│  Detect(ramdisk) ──→ MagiskDetectionResult                         │
│       │                                                             │
│       ├─→ Scan all CPIO entries                                     │
│       ├─→ Check artifact paths (sbin/magisk*, overlay.d, etc.)     │
│       ├─→ Check fstab for verity/encryption patches                │
│       ├─→ Check .backup/.magisk config                             │
│       ├─→ Check ramdisk.cpio.orig (full backup)                    │
│       └─→ Check .backup/init.xz (init backup)                      │
│                                                                     │
│  RestoreFromBackup(ramdisk) ──→ CpioArchive (CLEANEST)             │
│       │                                                             │
│       ├─→ Priority 1: ramdisk.cpio.orig → Full restore             │
│       ├─→ Priority 2: .backup/init.xz → Restore init only          │
│       └─→ Fallback: SurgicalRemoval()                              │
│                                                                     │
│  SurgicalRemoval(ramdisk) ──→ CpioArchive                          │
│       │                                                             │
│       ├─→ Remove: overlay.d, .backup, ramdisk.cpio.orig            │
│       ├─→ Remove: init.magisk.rc, sepolicy.rules                   │
│       ├─→ Patch fstab: remove verity/encryption patterns           │
│       ├─→ Restore fstab flags: avb, verify, fsverity               │
│       └─→ Clean init.rc: remove magisk services                    │
│                                                                     │
│  RestoreFstabFlags() ──→ byte[] (CRITICAL)                         │
│       │                                                             │
│       ├─→ Remove Magisk flags (magisk, wait, recoveryonly, etc.)   │
│       ├─→ Restore required flags from original backup OR defaults  │
│       └─→ Reconstruct fstab lines                                  │
└─────────────────────────────────────────────────────────────────────┘
```

## Public API

### Detection
```csharp
/// <summary>
/// Detect Magisk installation in a CPIO archive
/// </summary>
public MagiskDetectionResult Detect(CpioArchive ramdisk)
```

### Restoration Methods (Priority Order - USE IN ORDER)
```csharp
/// <summary>
/// Restore ramdisk from backup (PREFERRED - most reliable)
/// Priority: 1) ramdisk.cpio.orig, 2) .backup/init.xz
/// </summary>
public CpioArchive RestoreFromBackup(CpioArchive ramdisk)

/// <summary>
/// Surgical removal of Magisk when no backup exists
/// Less reliable than backup restoration
/// </summary>
public CpioArchive SurgicalRemoval(CpioArchive ramdisk)

/// <summary>
/// Surgical removal with explicit fstab flag restoration
/// </summary>
public CpioArchive SurgicalRemovalWithFlagRestoration(CpioArchive ramdisk, byte[]? originalFstabBackup = null)

/// <summary>
/// Restores critical boot flags in fstab entries
/// </summary>
public static byte[] RestoreFstabFlags(byte[] fstabData, byte[]? originalBackup)
```

## Detection Result

```csharp
public class MagiskDetectionResult
{
    public bool IsMagiskInstalled { get; set; }
    public List<string> FoundArtifacts { get; set; } = new();
    public bool HasBackup { get; set; }
    public bool HasFullBackup { get; set; }   // ramdisk.cpio.orig
    public bool HasInitBackup { get; set; }   // .backup/init.xz
    public MagiskBackupConfig? BackupConfig { get; set; }
    public bool HasVerityPatches { get; set; }
    public bool HasEncryptionPatches { get; set; }
    public string? DetectedVersion { get; set; }
    
    public string Summary { get; }  // Human-readable summary
}
```

## Backup Config (.backup/.magisk)

```csharp
public class MagiskBackupConfig
{
    public bool KeepVerity { get; set; }
    public bool KeepForceEncrypt { get; set; }
    public string? Sha1 { get; set; }
    public string? PreinitDevice { get; set; }
    public string? MagiskVersion { get; set; }
}
```

## Magisk Artifact Paths Detected

| Category | Paths |
|----------|-------|
| Binaries | `sbin/magisk`, `sbin/magisk64`, `sbin/magiskinit`, `sbin/magiskpolicy`, `sbin/resetprop`, `sbin/supolicy`, `sbin/magiskboot`, `sbin/magiskhide` |
| Directories | `overlay.d`, `.backup` |
| Configs | `init.magisk.rc`, `sepolicy.rules` |
| Backups | `ramdisk.cpio.orig` |

## Fstab Patterns

### Verity Patterns (removed by Magisk)
- `verity`
- `avb`

### Encryption Patterns (removed by Magisk)
- `forceencrypt`
- `fileencryption`
- `ice`

### Required Flags Restored (by mount point)

| Mount Point | Flags Added |
|-------------|-------------|
| `/system` | `avb`, `verify` |
| `/vendor` | `avb`, `verify` |
| `/product` | `avb`, `verify` |
| `/system_ext` | `avb`, `verify` |
| `/data` | `fsverity` |
| Others | `verify` |

### Magisk Flags Removed
- `magisk`
- `wait`
- `recoveryonly`
- `earlycon`
- `nomagic`

---

## Usage Examples

### Detect Magisk in Boot Image
```csharp
var parser = new BootImageParser();
var detector = new MagiskArtifactDetector();

// Parse boot image
var bootImg = parser.Parse("boot.img");

// Extract and decompress ramdisk
var ramdiskData = parser.ExtractRamdisk(bootImg);
using var compression = new CompressionEngine();
var decompressed = compression.Decompress(ramdiskData);

// Parse CPIO
var cpio = CpioArchive.Parse(decompressed);

// Detect
var result = detector.Detect(cpio);

Console.WriteLine(result.Summary);
// Output example:
// Magisk detected: v26.3
// Artifacts found: 12
// Full backup available (ramdisk.cpio.orig)
// Verity patches detected in fstab
// Encryption patches detected in fstab
```

### Complete Uninstall with Backup Restoration
```csharp
// BEST METHOD: Use Magisk's own backup
var detector = new MagiskArtifactDetector();
var cleanedRamdisk = detector.RestoreFromBackup(cpio);

if (detector.Detect(cpio).HasFullBackup) {
    Console.WriteLine("✓ Restored from ramdisk.cpio.orig (full stock ramdisk)");
} else if (detector.Detect(cpio).HasInitBackup) {
    Console.WriteLine("✓ Restored from .backup/init.xz (init only)");
} else {
    Console.WriteLine("⚠ No backup - used surgical removal");
}

// Repack
var repacked = repacker.RepackWithRamdisk(bootImg, cleanedRamdisk);
```

### Surgical Removal (No Backup)
```csharp
// When no Magisk backup exists
var cleanedRamdisk = detector.SurgicalRemoval(cpio);

// Or with original fstab backup for exact flag restoration
var originalFstab = File.ReadAllBytes("fstab.r8q.orig");
var cleanedRamdisk = detector.SurgicalRemovalWithFlagRestoration(cpio, originalFstab);
```

### Fstab Flag Restoration Only
```csharp
// Restore flags in fstab without full surgical removal
var fstabEntry = cpio.GetEntry("fstab.r8q");
if (fstabEntry != null) {
    var originalFstab = File.ReadAllBytes("fstab.r8q.orig");
    var restored = MagiskArtifactDetector.RestoreFstabFlags(fstabEntry.GetData(), originalFstab);
    fstabEntry.SetData(restored);
}
```

### Parse Backup Config
```csharp
var result = detector.Detect(cpio);

if (result.BackupConfig != null) {
    var config = result.BackupConfig;
    Console.WriteLine($"Magisk Version: {config.MagiskVersion}");
    Console.WriteLine($"KeepVerity: {config.KeepVerity}");
    Console.WriteLine($"KeepForceEncrypt: {config.KeepForceEncrypt}");
    Console.WriteLine($"SHA1: {config.Sha1}");
    Console.WriteLine($"PreinitDevice: {config.PreinitDevice}");
}
```

---

## Restoration Priority (CRITICAL)

### Priority 1: ramdisk.cpio.orig (BEST)
- Full stock ramdisk backup created by Magisk during installation
- Contains EXACT original ramdisk before any Magisk modifications
- **Use this whenever available**

### Priority 2: .backup/init.xz (GOOD)
- Compressed backup of original `init` file only
- Restores init but not other files
- Removes overlay.d and .backup directories

### Priority 3: Surgical Removal (FALLBACK)
- Removes known Magisk artifacts
- Patches fstab flags based on heuristics
- **May miss device-specific modifications**
- **Not guaranteed to produce bootable stock image**

---

## Samsung-Specific Notes

### Samsung GKI 2.0 (S24/S25/Z Fold 7)
- `init_boot` partition contains ramdisk (not `boot`)
- Ramdisk may use different compression (often LZ4)
- Magisk artifacts in init_boot ramdisk

### Samsung fstab Locations
| Device | fstab File |
|--------|------------|
| Most Samsung | `fstab.r8q`, `fstab.exynos990`, etc. |
| With vendor_boot | Check vendor_boot ramdisk too |

### Knox Warning
> **Surgical removal CANNOT restore tripped Knox eFuse.**
> Only full stock firmware flash via Odin/Download Mode can restore Knox.

---

## Integration with Full Uninstall

```csharp
var parser = new BootImageParser();
var repacker = new BootImageRepacker();
var detector = new MagiskArtifactDetector();
var restorer = new AvbRestorer();
var uninstaller = new MagiskUninstaller();

// Full pipeline
var bootImg = parser.Parse("boot.img");
var ramdiskData = parser.ExtractRamdisk(bootImg);
var decompressed = new CompressionEngine().Decompress(ramdiskData);
var cpio = CpioArchive.Parse(decompressed);

// 1. Detect
var detection = detector.Detect(cpio);

// 2. Restore (best available method)
var cleanedRamdisk = detector.RestoreFromBackup(cpio);

// 3. Repack
var repacked = repacker.RepackWithRamdisk(bootImg, cleanedRamdisk);

// 4. Restore AVB flags
var avbResult = restorer.RestoreVerificationFlags(repacked);
if (avbResult.FlagsChanged) repacked = avbResult.PatchedImage!;

// 5. Flash
File.WriteAllBytes("boot_stock.img", repacked);
```

---

## Error Handling

```csharp
var result = detector.Detect(cpio);

if (!result.IsMagiskInstalled) {
    Console.WriteLine("No Magisk detected - already stock?");
    return;
}

try {
    var cleaned = detector.RestoreFromBackup(cpio);
    // Verify
    var verify = detector.Detect(cleaned);
    if (verify.IsMagiskInstalled) {
        Console.WriteLine("⚠ Magisk still detected after restoration!");
    } else {
        Console.WriteLine("✓ Magisk fully removed");
    }
} catch (Exception ex) {
    Console.WriteLine($"Restoration failed: {ex.Message}");
    // Try surgical removal
    var cleaned = detector.SurgicalRemoval(cpio);
}
```

---

## Limitations

1. **sepolicy.rules** - Not parsed/validated, only removed
2. **Overlay.d contents** - Not deep-inspected (could contain custom modifications)
3. **Multiple fstab formats** - Assumes standard 4+ column format
4. **No boot verification** - Cannot guarantee restored image will boot
5. **Magisk version detection** - Only from .backup/.magisk config
6. **No integrity check** - No SHA256 verification of restored ramdisk

---

## Related Files

| File | Relationship |
|------|--------------|
| `CpioArchive.cs` | Parses/serializes ramdisk |
| `CompressionEngine.cs` | Decompresses ramdisk |
| `BootImageParser.cs` | Extracts ramdisk from boot image |
| `BootImageRepacker.cs` | Repacks with cleaned ramdisk |
| `AvbRestorer.cs` | Restores AVB flags after repack |
| `MagiskUninstaller.cs` | Orchestrates full uninstall |
| `Structures/Ramdisk/MagiskArtifacts.cs` | Artifact path definitions |

---

## References

1. **Magisk rootdir.rs** - `native/src/rootdir.rs`
2. **Magisk magisk_patch.rs** - `native/src/boot/magisk_patch.rs`
3. **Magisk boot_patch.sh** - Shell implementation
4. **Android fstab format** - `system/core/fs_mgr/include/fs_mgr.h`

---

## Test Coverage Needed

### Detection
```csharp
[Test] void Detect_MagiskArtifacts_Found_ReturnsTrue()
[Test] void Detect_NoMagisk_ReturnsFalse()
[Test] void Detect_VerityPatternsInFstab_SetsFlag()
[Test] void Detect_EncryptionPatternsInFstab_SetsFlag()
[Test] void Detect_BackupConfig_ParsedCorrectly()
[Test] void Detect_FullBackup_Detected()
[Test] void Detect_InitBackup_Detected()
```

### Restoration
```csharp
[Test] void RestoreFromBackup_FullBackup_ReturnsCleanRamdisk()
[Test] void RestoreFromBackup_InitBackup_RestoresInitOnly()
[Test] void RestoreFromBackup_NoBackup_FallsBackToSurgical()
```

### Surgical Removal
```csharp
[Test] void SurgicalRemoval_RemovesOverlayDir()
[Test] void SurgicalRemoval_RemovesBackupDir()
[Test] void SurgicalRemoval_RemovesMagiskEntries()
[Test] void SurgicalRemoval_PatchesFstabVerityPatterns()
[Test] void SurgicalRemoval_PatchesFstabEncryptionPatterns()
[Test] void SurgicalRemoval_CleansInitRcMagiskServices()
```

### Fstab Flag Restoration
```csharp
[Test] void RestoreFstabFlags_RestoresAvbVerifySystemVendor()
[Test] void RestoreFstabFlags_RestoresFsverityData()
[Test] void RestoreFstabFlags_RemovesMagiskFlags()
[Test] void RestoreFstabFlags_WithOriginalBackup_ExactRestoration()
[Test] void RestoreFstabFlags_HandlesCommentsAndEmptyLines()