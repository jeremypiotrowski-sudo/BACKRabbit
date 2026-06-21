# Cross-Reference Map: BACKRabbit.MagiskCore.RamdiskEditor.MagiskArtifactDetector.cs ↔ Magisk Sources

## Overview
| Property | Value |
|----------|-------|
| **BACKRabbit File** | `BACKRabbit.MagiskCore/RamdiskEditor/MagiskArtifactDetector.cs` |
| **Magisk Source** | `native/src/rootdir.rs`, `native/src/boot/magisk_patch.rs`, `scripts/boot_patch.sh` |
| **Total Lines (BACKRabbit)** | 396 |

---

## Version-by-Version Cross-Reference

### Magisk v25.0-v27.0
| BACKRabbit Line(s) | Magisk Source | Function |
|---|---|---|
| 19-70 | `rootdir.rs` | `detect()` - artifact scanning |
| 72-110 | `magisk_patch.rs` | `parse_backup_config()` |
| 116-142 | `rootdir.rs` | `restore_from_backup()` |
| 148-184 | `rootdir.rs` | `surgical_removal()` |
| 189-227 | `magisk_patch.rs` | `surgical_removal_with_flags()` |
| 229-334 | `magisk_patch.rs` | `restore_fstab_flags()` |
| 336-350 | `magisk_patch.rs` | `remove_patterns()` |

### Magisk rootdir.rs (v26.3)
| BACKRabbit Line(s) | rootdir.rs Line(s) | Notes |
|---|---|---|
| 19-70 | ~100-180 | `RootDir::detect()` |
| 116-142 | ~180-220 | `RootDir::restore_from_backup()` |
| 148-184 | ~220-280 | `RootDir::surgical_removal()` |
| 229-334 | ~280-350 | `restore_fstab_flags()` |

---

## Detailed Function Mapping

### Detect() - Lines 19-70
**Magisk Equivalent:** `RootDir::detect()` in rootdir.rs

```rust
// Magisk v26.3 (rootdir.rs ~line 100)
pub fn detect(&self) -> DetectionResult {
    let mut result = DetectionResult::default();
    
    for entry in &self.entries {
        // Check artifact paths
        for path in ARTIFACT_PATHS {
            if entry.name.ends_with(path) || entry.name.contains("overlay.d/sbin/") {
                result.found_artifacts.push(entry.name.clone());
                result.is_magisk_installed = true;
            }
        }
        
        // Check fstab for patches
        if entry.name.starts_with("fstab.") {
            let content = str::from_utf8(&entry.data).unwrap();
            if VERITY_PATTERNS.iter().any(|p| content.contains(p)) {
                result.has_verity_patches = true;
            }
            if ENCRYPTION_PATTERNS.iter().any(|p| content.contains(p)) {
                result.has_encryption_patches = true;
            }
        }
    }
    
    // Check backup config
    if let Some(backup) = self.get_entry(".backup/.magisk") {
        result.backup_config = parse_backup_config(&backup.data);
        result.has_backup = true;
    }
    
    // Check stock backup
    if self.get_entry("ramdisk.cpio.orig").is_some() {
        result.has_full_backup = true;
    }
    
    // Check init backup
    if self.get_entry(".backup/init.xz").is_some() {
        result.has_init_backup = true;
    }
    
    result
}
```

**BACKRabbit mapping:** Lines 23-70 direct port with MagiskArtifactsStatic.

### ParseBackupConfig() - Lines 76-110
**Magisk Equivalent:** `parse_backup_config()` in magisk_patch.rs

```rust
// Magisk v26.3 (magisk_patch.rs ~line 50)
fn parse_backup_config(data: &[u8]) -> BackupConfig {
    let mut config = BackupConfig::default();
    let content = str::from_utf8(data).unwrap();
    
    for line in content.lines() {
        let parts: Vec<&str> = line.split('=').collect();
        if parts.len() != 2 { continue; }
        
        match parts[0].trim() {
            "KEEPVERITY" => config.keep_verity = parts[1].trim() == "true",
            "KEEPFORCEENCRYPT" => config.keep_force_encrypt = parts[1].trim() == "true",
            "SHA1" => config.sha1 = parts[1].trim().to_string(),
            "PREINITDEVICE" => config.preinit_device = parts[1].trim().to_string(),
            "MAGISK_VERSION" => config.magisk_version = parts[1].trim().to_string(),
            _ => {}
        }
    }
    
    config
}
```

**BACKRabbit mapping:** Lines 79-108 direct port.

### RestoreFromBackup() - Lines 116-142
**Magisk Equivalent:** `RootDir::restore_from_backup()` in rootdir.rs

```rust
// Magisk v26.3 (rootdir.rs ~line 180)
fn restore_from_backup(&self) -> RootDir {
    // Priority 1: ramdisk.cpio.orig (full restore)
    if let Some(orig) = self.get_entry("ramdisk.cpio.orig") {
        return RootDir::parse(&orig.data);
    }
    
    // Priority 2: .backup/init.xz (restore init only)
    if let Some(backup_init) = self.get_entry(".backup/init.xz") {
        let decompressed = decompress(backup_init.data, CompressionFormat::Xz);
        let mut new_ramdisk = self.clone();
        new_ramdisk.replace_entry("init", decompressed);
        new_ramdisk.remove_directory("overlay.d");
        new_ramdisk.remove_directory(".backup");
        new_ramdisk.remove_entry("ramdisk.cpio.orig");
        return new_ramdisk;
    }
    
    // No backup: surgical removal
    surgical_removal(self)
}
```

**BACKRabbit mapping:** Lines 119-142 direct port.

### SurgicalRemoval() - Lines 148-184
**Magisk Equivalent:** `RootDir::surgical_removal()` in rootdir.rs

```rust
// Magisk v26.3 (rootdir.rs ~line 220)
fn surgical_removal(&self) -> RootDir {
    let mut cleaned = self.clone();
    
    // Remove Magisk dirs/files
    cleaned.remove_directory("overlay.d");
    cleaned.remove_directory(".backup");
    cleaned.remove_entry("ramdisk.cpio.orig");
    cleaned.remove_entry("init.magisk.rc");
    cleaned.remove_entry("sepolicy.rules");
    
    // Patch fstab
    for entry in &mut cleaned.entries.iter_mut().filter(|e| e.name.starts_with("fstab.")) {
        entry.data = restore_fstab_flags(&entry.data, None);
    }
    
    // Clean init.rc
    if let Some(init) = cleaned.get_entry_mut("init.rc") {
        let filtered: Vec<&str> = init.data.lines()
            .filter(|l| !l.contains("magisk") && !l.contains("overlay.d"))
            .collect();
        init.data = filtered.join("\n").into_bytes();
    }
    
    cleaned
}
```

**BACKRabbit mapping:** Lines 153-184 direct port.

### RestoreFstabFlags() - Lines 236-334
**Magisk Equivalent:** `restore_fstab_flags()` in magisk_patch.rs

```rust
// Magisk v26.3 (magisk_patch.rs ~line 280)
fn restore_fstab_flags(fstab_data: &[u8], original_backup: Option<&[u8]>) -> Vec<u8> {
    let content = str::from_utf8(fstab_data).unwrap();
    let required_flags = ["avb", "verify", "fsverity"];
    
    // Extract original flags if backup provided
    let mut original_flags = HashMap::new();
    if let Some(backup) = original_backup {
        for line in str::from_utf8(backup).unwrap().lines() {
            let parts: Vec<&str> = line.split_whitespace().collect();
            if parts.len() >= 4 {
                let mount_point = parts[1];
                let flags: HashSet<&str> = parts[3].split(',').collect();
                original_flags.insert(mount_point, flags);
            }
        }
    }
    
    let mut result = Vec::new();
    for line in content.lines() {
        if line.is_empty() || line.starts_with('#') {
            result.push(line);
            continue;
        }
        
        let parts: Vec<&str> = line.split_whitespace().collect();
        if parts.len() < 4 {
            result.push(line);
            continue;
        }
        
        let device = parts[0];
        let mount_point = parts[1];
        let fstype = parts[2];
        let mut flags: HashSet<&str> = parts[3].split(',').collect();
        
        // Remove Magisk flags
        for mf in ["magisk", "wait", "recoveryonly", "earlycon", "nomagic"] {
            flags.remove(mf);
        }
        
        // Restore required flags
        if let Some(orig) = original_flags.get(mount_point) {
            for flag in required_flags {
                if orig.contains(flag) { flags.insert(flag); }
            }
        } else {
            // Default restoration
            match mount_point {
                "/system" | "/vendor" | "/product" | "/system_ext" => {
                    flags.insert("avb");
                    flags.insert("verify");
                }
                "/data" => flags.insert("fsverity"),
                _ => if !flags.contains("verify") { flags.insert("verify"); }
            }
        }
        
        result.push(format!("{}\t{}\t{}\t{}", device, mount_point, fstype, flags.join(",")));
    }
    
    result.join("\n").into_bytes()
}
```

**BACKRabbit mapping:** Lines 241-333 direct port with HashSet for flags.

---

## Data Structures

### MagiskDetectionResult (Lines 356-384)
```csharp
public class MagiskDetectionResult
{
    public bool IsMagiskInstalled { get; set; }
    public List<string> FoundArtifacts { get; set; } = new();
    public bool HasBackup { get; set; }
    public bool HasFullBackup { get; set; }      // ramdisk.cpio.orig
    public bool HasInitBackup { get; set; }      // .backup/init.xz
    public MagiskBackupConfig? BackupConfig { get; set; }
    public bool HasVerityPatches { get; set; }
    public bool HasEncryptionPatches { get; set; }
    public string? DetectedVersion { get; set; }
    
    public string Summary { get; }  // Human-readable
}
```

### MagiskBackupConfig (Lines 389-396)
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

---

## Magisk Artifact Paths (from MagiskArtifactsStatic)

```rust
// Magisk v26.3 (rootdir.rs)
const ARTIFACT_PATHS: &[&str] = &[
    "overlay.d/sbin/magisk",
    "overlay.d/sbin/magiskinit",
    "overlay.d/sbin/magiskpolicy",
    "overlay.d/sbin/magiskboot",
    "init.magisk.rc",
    "sepolicy.rules",
    "ramdisk.cpio.orig",
    ".backup/init.xz",
    ".backup/.magisk",
];
```

### Verity/Encryption Patterns
```rust
const VERITY_PATTERNS: &[&[u8]] = &[
    b"avb",
    b"verify",
    b"fsverity",
];

const ENCRYPTION_PATTERNS: &[&[u8]] = &[
    b"forceencrypt",
    b"encryptable",
    b"keymaster",
    b"gatekeeper",
];
```

---

## Usage Examples

### Detect Magisk
```csharp
var parser = new BootImageParser();
var bootImage = parser.Parse("boot.img");
var ramdisk = parser.ExtractRamdiskArchive(bootImage);

var detector = new MagiskArtifactDetector();
var detection = detector.Detect(ramdisk);

Console.WriteLine(detection.Summary);
// Output:
// Magisk detected: v26.3
// Artifacts found: 5
// Full backup available (ramdisk.cpio.orig)
// Verity patches detected in fstab
```

### Restore from Backup (Preferred)
```csharp
var cleanedRamdisk = detector.RestoreFromBackup(ramdisk);
// Uses ramdisk.cpio.orig if available, else .backup/init.xz
```

### Surgical Removal (No Backup)
```csharp
var cleanedRamdisk = detector.SurgicalRemoval(ramdisk);
// Or with explicit flag restoration
var cleanedRamdisk = detector.SurgicalRemovalWithFlagRestoration(ramdisk, originalFstabBackup);
```

---

## Missing/Partial Implementations

1. **Multiple Backup Versions** - Only checks for latest backup format
2. **SEPolicy Restoration** - Removes sepolicy.rules but doesn't restore original
3. **Overlay.d Variants** - Only checks "overlay.d/sbin/", Magisk may use other paths
4. **Magisk Version Detection** - Only from backup config, not from binary
5. **Dynamic Partitions** - Doesn't handle vendor_boot/init_boot ramdisks
6. **Recovery Ramdisk** - Separate ramdisk not checked

---

## Test Coverage Needed

| Test Case | Status |
|---|---|
| Detect_CleanRamdisk_ReturnsNotInstalled | ❌ |
| Detect_MagiskInstalled_FindsArtifacts | ❌ |
| Detect_VerityPatches_Detected | ❌ |
| Detect_EncryptionPatches_Detected | ❌ |
| Detect_BackupConfig_Parsed | ❌ |
| Detect_FullBackup_Detected | ❌ |
| Detect_InitBackup_Detected | ❌ |
| RestoreFromBackup_FullBackup_Restores | ❌ |
| RestoreFromBackup_InitBackup_RestoresInit | ❌ |
| SurgicalRemoval_RemovesOverlay | ❌ |
| SurgicalRemoval_RemovesBackup | ❌ |
| SurgicalRemoval_PatchesFstabFlags | ❌ |
| RestoreFstabFlags_RemovesMagiskFlags | ❌ |
| RestoreFstabFlags_AddsAvbVerify | ❌ |
| RestoreFstabFlags_AddsFsverity | ❌ |
| RestoreFstabFlags_UsesOriginalBackup | ❌ |

---

## Source File References

**BACKRabbit Source:** `BACKRabbit.MagiskCore/RamdiskEditor/MagiskArtifactDetector.cs` (396 lines)

**Magisk Sources (in knowledge-base):**
- `v26.0_rootdir.rs` through `v27.0_rootdir.rs`
- `v26.0_magisk_patch.rs` through `v27.0_magisk_patch.rs`
- `scripts/boot_patch.sh` (backup/restore functions)

**Related BACKRabbit Files:**
- `CpioArchive.cs` - Parses ramdisk, FindMagiskArtifacts()
- `BootImageParser.cs` - Extracts ramdisk
- `BootImageRepacker.cs` - Repacks with cleaned ramdisk