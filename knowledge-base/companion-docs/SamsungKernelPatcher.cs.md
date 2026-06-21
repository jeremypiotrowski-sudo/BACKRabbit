# Companion Documentation: BACKRabbit.MagiskCore.SamsungKernel.SamsungKernelPatcher.cs

## Purpose
Samsung kernel security patch analysis and reversal service. Detects and reverses Magisk's modifications to Samsung's kernel-level security features (RKP, Defex, PROCA, KNOX).

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                     SamsungKernelPatcher                            │
├─────────────────────────────────────────────────────────────────────┤
│  Analyze(kernel) ──→ KernelAnalysisResult                          │
│       │                                                             │
│       ├─→ Signature detection:                                     │
│       │    "RKP"       → Real-time Kernel Protection               │
│       │    "DEFEX"     → Defense against Exploits                  │
│       │    "PROCA"     → Process Protection                        │
│       │    "KNOX"      → Knox Kernel Protection                    │
│       ├─→ Syscall table analysis                                   │
│       │    Find table via symbols (sys_call_table, nr_syscalls)   │
│       │    Check for branch hooks (B <offset>)                     │
│       └─→ Hook pattern detection                                   │
│            magisk_hook, kernel_su, selinux_disable, avc_disable   │
│                                                                     │
│  RestoreStock(kernel, analysis?) ──→ byte[] (restored kernel)      │
│       │                                                             │
│       ├─→ Syscall table: Replace B <offset> with MOV X0,#0; RET   │
│       ├─→ RKP: Detect bypass patterns (limited reversal)           │
│       ├─→ Defex: Remove signature                                   │
│       ├─→ PROCA: Remove signature                                   │
│       └─→ Return patched kernel                                    │
│                                                                     │
│  ApplyHexPatch(kernel, patches[]) ──→ byte[] (manual patches)     │
└─────────────────────────────────────────────────────────────────────┘
```

## Public API

### Analysis
```csharp
/// <summary>
/// Analyze kernel for Samsung security patches
/// </summary>
/// <param name="kernel">Raw kernel data</param>
/// <returns>Detailed analysis result</returns>
public KernelAnalysisResult Analyze(byte[] kernel)
```

### Restoration
```csharp
/// <summary>
/// Reverse Magisk kernel patches and restore stock state
/// </summary>
/// <param name="kernel">Raw kernel data</param>
/// <param name="analysis">Optional pre-computed analysis</param>
/// <returns>Restored kernel</returns>
public byte[] RestoreStock(byte[] kernel, KernelAnalysisResult? analysis = null)

/// <summary>
/// Apply manual hex patches to kernel
/// </summary>
public byte[] ApplyHexPatch(byte[] kernel, HexPatch[] patches)
```

## Data Structures

### KernelAnalysisResult
```csharp
public class KernelAnalysisResult
{
    public bool HasRkp { get; set; }
    public bool HasDefex { get; set; }
    public bool HasProca { get; set; }
    public bool HasKnox { get; set; }
    public SyscallTableStatus SyscallTableStatus { get; set; }
    public List<HookPattern> HookPatterns { get; set; } = new();
    
    public bool IsStock => !HasRkp && !HasDefex && !HasProca && !HasKnox 
                          && SyscallTableStatus == SyscallTableStatus.Stock;
    
    public string Summary { get; }  // Human-readable summary
}
```

### SyscallTableStatus
```csharp
public enum SyscallTableStatus
{
    NotFound,
    Stock,
    Hooked
}
```

### HookPattern
```csharp
public class HookPattern
{
    public long Offset { get; set; }
    public string Description { get; set; } = "";
}
```

### HexPatch
```csharp
public class HexPatch
{
    public long Offset { get; set; }
    public byte[] Pattern { get; set; } = Array.Empty<byte>();
    public byte[] Replacement { get; set; } = Array.Empty<byte>();
    public string Description { get; set; } = "";
}
```

## Samsung Security Features

### 1. RKP (Real-time Kernel Protection)
- **Description**: Samsung's KASLR + CFI implementation
- **Detection**: "RKP" magic string in kernel
- **Magisk Patch**: Disables RKP checks (rkp_check_signature, rkp_verify_boot)
- **BACKRabbit**: Detection only; full reversal requires kernel-specific knowledge
- **Warning**: Hardware-enforced on some devices

### 2. Defex (Defense against Exploits)
- **Description**: Hardened syscall table with security hooks
- **Detection**: "DEFEX" signature
- **Magisk Patch**: Modifies syscall_table entries, security_hook_list
- **BACKRabbit**: Signature removal; hook restoration incomplete

### 3. PROCA (Process Protection)
- **Description**: Enhanced SELinux + process isolation
- **Detection**: "PROCA" signature
- **Magisk Patch**: Weakens process restrictions
- **BACKRabbit**: Signature removal; full reversal incomplete

### 4. KNOX Kernel Protection
- **Description**: Multiple security layers including eFuse
- **Detection**: "KNOX" signature
- **Magisk Patch**: Bypasses KNOX checks
- **BACKRabbit**: Detection only
- **Critical**: Knox eFuse is **permanent hardware fuse** - cannot be restored

## ARM64 Instruction Patterns

### Stock Syscall Prologue
```
Hex:    00 00 80 D2 C0 03 5F D6
Asm:    MOV X0, #0; RET
Purpose: Stock syscall returns 0 (success) and returns to caller
```

### Hook Branch Pattern
```
Hex:    00 00 00 14
Asm:    B <offset>
Purpose: Unconditional branch to hook handler (Magisk patches)
```

## Syscall Table Analysis

### Symbol-Based Location
```csharp
// Searches for symbols near syscall table
var signatures = new[]
{
    "sys_call_table",    // Main table symbol
    "nr_syscalls",       // Syscall count
    "__sys_trace"        // Trace symbol
};
```

### Hook Detection
```csharp
// Scans syscall table entries (8 bytes each on ARM64)
for (int i = tableOffset; i < kernel.Length - 8; i += 8) {
    if (kernel[i..i+4] == HOOK_BRANCH_PATTERN) {
        // Found hooked entry
        return SyscallTableStatus.Hooked;
    }
}
```

### Restoration
```csharp
// Replace each hook with stock prologue
STOCK_SYSCALL_PROLOGUE.CopyTo(result.AsSpan(i, 8));
```

## Usage Examples

### Analyze Samsung Kernel
```csharp
var patcher = new SamsungKernelPatcher();
var kernel = File.ReadAllBytes("Image.gz-dtb"); // or decompressed kernel

var analysis = patcher.Analyze(kernel);
Console.WriteLine(analysis.Summary);
// Output:
// Kernel Security Analysis:
//   RKP: Present
//   Defex: Present
//   PROCA: Not found
//   KNOX: Present
//   Syscall Table: Hooked
//   Hook patterns found: 2
//     - Magisk hook function at offset 0x1A2B3C
//     - SELinux disabled at offset 0x4D5E6F
//   Overall: MODIFIED
```

### Restore Stock Kernel
```csharp
var restoredKernel = patcher.RestoreStock(kernel, analysis);

// Or auto-analyze
var restoredKernel = patcher.RestoreStock(kernel);

File.WriteAllBytes("Image_restored.gz-dtb", restoredKernel);
```

### Apply Manual Hex Patches
```csharp
var patches = new[]
{
    new HexPatch {
        Offset = 0x1A2B3C,
        Pattern = new byte[] { 0x00, 0x00, 0x00, 0x14 },  // B <hook>
        Replacement = new byte[] { 0x00, 0x00, 0x80, 0xD2, 0xC0, 0x03, 0x5F, 0xD6 },  // MOV X0,#0; RET
        Description = "Restore syscall #42"
    },
    new HexPatch {
        Offset = 0x4D5E6F,
        Pattern = new byte[] { 0x00, 0x00, 0x00, 0x14 },
        Replacement = new byte[] { 0x00, 0x00, 0x80, 0xD2, 0xC0, 0x03, 0x5F, 0xD6 },
        Description = "Restore SELinux hook"
    }
};

var patchedKernel = patcher.ApplyHexPatch(kernel, patches);
```

### Complete Samsung Uninstall Flow
```csharp
// 1. Extract kernel from boot image
var parser = new BootImageParser();
var bootImage = parser.Parse("boot.img");
var kernel = parser.ExtractKernel(bootImage);

// 2. Analyze
var patcher = new SamsungKernelPatcher();
var analysis = patcher.Analyze(kernel);

// 3. Restore kernel patches
var restoredKernel = patcher.RestoreStock(kernel, analysis);

// 4. Extract and clean ramdisk
var ramdisk = parser.ExtractRamdiskArchive(bootImage);
var detector = new MagiskArtifactDetector();
var detection = detector.Detect(ramdisk);

CpioArchive cleanedRamdisk;
if (detection.HasFullBackup) cleanedRamdisk = detector.RestoreFromBackup(ramdisk);
else if (detection.HasInitBackup) cleanedRamdisk = detector.RestoreFromBackup(ramdisk);
else cleanedRamdisk = detector.SurgicalRemoval(ramdisk);

// 5. Repack with restored kernel and cleaned ramdisk
var repacker = new BootImageRepacker();
var newImage = repacker.Repack(bootImage, 
    repacker.RepackWithRamdisk(bootImage, cleanedRamdisk), // ramdisk
    restoredKernel); // kernel

// 6. Restore AVB flags
var restorer = new AvbRestorer();
var avbResult = restorer.RestoreVerificationFlags(newImage);

File.WriteAllBytes("boot_stock.img", avbResult.PatchedImage!);
```

## Important Limitations

### What This DOES
- Detects Samsung security signatures
- Restores syscall table hooks (branch → stock prologue)
- Removes DEFEX/PROCA signatures
- Detects hook patterns (magisk_hook, kernel_su, etc.)
- Allows manual hex patching

### What This DOES NOT
- **Reset Knox eFuse** - Hardware fuse, permanent
- **Fully reverse RKP bypass** - Requires kernel-specific analysis
- **Fully restore Defex hooks** - Needs original hook list
- **Fully restore PROCA hooks** - Needs original hook list
- **Parse kernel symbol tables** - String search only
- **Handle kernel version variations** - No version database

### Samsung Knox Warning
> On Samsung devices, the Knox warranty bit (eFuse) is a **hardware fuse** that permanently trips when:
> - Custom kernel flashed
> - Boot image modified
> - System partition modified
> 
> **This cannot be restored by any software tool.** SamsungKernelPatcher restores kernel *software* state only.

## Magisk Version Compatibility

| Magisk Version | Samsung Patching | BACKRabbit Status |
|----------------|------------------|-------------------|
| v25.0 | samsung_kernel_patcher.sh | ✅ Compatible |
| v25.1-v25.2 | Same | ✅ Compatible |
| v26.0 | native samsung_kernel.cpp | ✅ Compatible |
| v26.1-v27.0 | Same | ✅ Compatible |

## Related Files

| File | Relationship |
|------|--------------|
| `BootImageParser.cs` | Extracts kernel from boot image |
| `BootImageRepacker.cs` | Repacks with restored kernel |
| `MagiskArtifactDetector.cs` | Ramdisk cleanup before repack |
| `AvbRestorer.cs` | AVB flags after kernel restore |

## References

1. **Magisk samsung_kernel.cpp** - `native/src/boot/samsung_kernel.cpp` (v26.3)
2. **Magisk samsung_kernel_patcher.sh** - `scripts/samsung_kernel_patcher.sh`
3. **Samsung RKP** - Samsung KNOX Real-time Kernel Protection
4. **ARM64 Syscall ABI** - ARM Architecture Reference Manual
5. **Linux Kernel Syscall Table** - `arch/arm64/kernel/syscall.c`

## Testing Recommendations

### Unit Tests
```csharp
[Test] void Analyze_RKPPresent_DetectsRKP()
[Test] void Analyze_DEFXPresent_DetectsDEFEX()
[Test] void Analyze_PROCAPresent_DetectsPROCA()
[Test] void Analyze_KNOXPresent_DetectsKNOX()
[Test] void Analyze_SyscallTableStock_ReturnsStock()
[Test] void Analyze_SyscallTableHooked_ReturnsHooked()
[Test] void FindSyscallTable_ValidKernel_ReturnsOffset()
[Test] void RestoreSyscallTable_HookedEntries_ReplacesWithStock()
[Test] void RemoveRkpBypass_PatternFound_Flags()
[Test] void RemoveDefexPatches_SignatureRemoved()
[Test] void RemoveProcaPatches_SignatureRemoved()
[Test] void FindHookPatterns_MagiskHook_Detected()
[Test] void ApplyHexPatch_ValidOffset_AppliesPatch()
```

### Test Data
```bash
# Need Samsung stock kernel and Magisk-patched kernel pairs
# Extract from Samsung firmware (AP tar)
# Or use Magisk to patch stock kernel for testing
```

## Cross-Reference
See `knowledge-base/cross-reference-map/SamsungKernelPatcher.cs.md` for line-by-line Magisk source mapping.