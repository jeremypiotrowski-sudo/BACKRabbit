# Cross-Reference Map: BACKRabbit.MagiskCore.SamsungKernel.SamsungKernelPatcher.cs ↔ Magisk Source

## Overview
| Property | Value |
|----------|-------|
| **BACKRabbit File** | `BACKRabbit.MagiskCore/SamsungKernel/SamsungKernelPatcher.cs` |
| **Magisk Source** | `native/src/boot/samsung_kernel_patcher.rs`, `scripts/samsung_kernel_patcher.sh` |
| **Magisk Versions** | v25.0 - v27.0 |
| **Total Lines (BACKRabbit)** | 388 |
| **Total Lines (Magisk v26.3 samsung_kernel_patcher.rs)** | ~450 |

---

## Version-by-Version Cross-Reference

### Magisk v25.0-v25.2 (scripts/samsung_kernel_patcher.sh)
| BACKRabbit Line(s) | Magisk v25.x Line(s) | Function |
|---|---|---|
| 46-63 | samsung_kernel_patcher.sh | `analyze_kernel()` - signature detection |
| 68-98 | samsung_kernel_patcher.sh | `restore_stock_kernel()` - main restoration |
| 103-150 | samsung_kernel_patcher.sh | `find_syscall_table()` + `analyze_syscall_table()` |
| 155-174 | samsung_kernel_patcher.sh | `restore_syscall_table()` |
| 179-202 | samsung_kernel_patcher.sh | `remove_rkp_bypass()` |
| 207-221 | samsung_kernel_patcher.sh | `remove_defex_patches()` |
| 226-234 | samsung_kernel_patcher.sh | `remove_proca_patches()` |
| 239-248 | samsung_kernel_patcher.sh | `remove_signature()` |
| 274-299 | samsung_kernel_patcher.sh | `find_hook_patterns()` |
| 304-317 | samsung_kernel_patcher.sh | `apply_hex_patch()` |

### Magisk v26.0-v27.0 (native/src/boot/samsung_kernel_patcher.rs)
| BACKRabbit Line(s) | Magisk v26.x Line(s) | Notes |
|---|---|---|
| 46-63 | ~50-80 | `SamsungKernelPatcher::analyze()` |
| 68-98 | ~80-120 | `restore_stock()` |
| 103-150 | ~120-180 | Syscall table analysis |
| 155-174 | ~180-220 | Syscall table restoration |
| 179-202 | ~220-260 | RKP bypass removal |
| 207-221 | ~260-290 | Defex removal |
| 226-234 | ~290-310 | PROCA removal |
| 239-248 | ~310-330 | Signature removal |
| 274-299 | ~330-370 | Hook pattern detection |
| 304-317 | ~370-400 | Hex patch application |

---

## Detailed Function Mapping

### Analyze() - Lines 46-63
**Magisk Equivalent:** `SamsungKernelPatcher::analyze()` in samsung_kernel_patcher.rs

```rust
// Magisk v26.3 (samsung_kernel_patcher.rs ~line 50)
pub fn analyze(kernel: &[u8]) -> KernelAnalysisResult {
    let mut result = KernelAnalysisResult::default();
    
    // Check Samsung signatures
    result.has_rkp = contains_pattern(kernel, b"RKP");
    result.has_defex = contains_pattern(kernel, b"DEFEX");
    result.has_proca = contains_pattern(kernel, b"PROCA");
    result.has_knox = contains_pattern(kernel, b"KNOX");
    
    // Syscall table analysis
    result.syscall_table_status = analyze_syscall_table(kernel);
    
    // Hook patterns
    result.hook_patterns = find_hook_patterns(kernel);
    
    result
}
```

**BACKRabbit mapping:** Lines 50-61 direct port.

### RestoreStock() - Lines 68-98
**Magisk Equivalent:** `restore_stock()` in samsung_kernel_patcher.rs

```rust
// Magisk v26.3 (samsung_kernel_patcher.rs ~line 80)
pub fn restore_stock(kernel: &[u8], analysis: Option<KernelAnalysisResult>) -> Vec<u8> {
    let analysis = analysis.unwrap_or_else(|| Self::analyze(kernel));
    let mut result = kernel.to_vec();
    
    // Restore syscall table
    if analysis.syscall_table_status == SyscallTableStatus::Hooked {
        result = restore_syscall_table(result);
    }
    
    // Remove RKP bypass
    if analysis.has_rkp {
        result = remove_rkp_bypass(result);
    }
    
    // Remove Defex
    if analysis.has_defex {
        result = remove_defex_patches(result);
    }
    
    // Remove PROCA
    if analysis.has_proca {
        result = remove_proca_patches(result);
    }
    
    result
}
```

**BACKRabbit mapping:** Lines 70-97 direct port.

### Syscall Table Analysis - Lines 103-150
**Magisk Equivalent:** `analyze_syscall_table()` / `find_syscall_table()` in samsung_kernel_patcher.rs

```rust
// Magisk v26.3 (samsung_kernel_patcher.rs ~line 120)
fn analyze_syscall_table(kernel: &[u8]) -> SyscallTableStatus {
    let offset = find_syscall_table(kernel)?;
    let table = &kernel[offset..offset+256];
    
    // Check for branch instructions (0x14000000 = B <imm26>)
    for chunk in table.chunks(8) {
        if chunk.starts_with(&[0x00, 0x00, 0x00, 0x14]) {
            return SyscallTableStatus::Hooked;
        }
    }
    
    SyscallTableStatus::Stock
}

fn find_syscall_table(kernel: &[u8]) -> Option<usize> {
    // Look for kernel symbols
    for sig in [b"sys_call_table", b"nr_syscalls", b"__sys_trace"] {
        if let Some(offset) = find_pattern(kernel, sig) {
            return Some(offset + sig.len());
        }
    }
    None
}
```

**BACKRabbit mapping:** Lines 103-150 direct port.

### RestoreSyscallTable() - Lines 155-174
**Magisk Equivalent:** `restore_syscall_table()` in samsung_kernel_patcher.rs

```rust
// Magisk v26.3 (samsung_kernel_patcher.rs ~line 180)
fn restore_syscall_table(kernel: &mut [u8]) {
    let offset = find_syscall_table(kernel)?;
    
    for i in (offset..kernel.len()-8).step_by(8) {
        if &kernel[i..i+4] == &[0x00, 0x00, 0x00, 0x14] {
            // Replace branch with stock prologue
            // MOV X0, #0 (0xD2800000) + RET (0xD65F03C0)
            kernel[i..i+8].copy_from_slice(&[
                0x00, 0x00, 0x80, 0xD2,
                0xC0, 0x03, 0x5F, 0xD6
            ]);
        }
    }
}
```

**BACKRabbit mapping:** Lines 155-173 direct port with STOCK_SYSCALL_PROLOGUE.

### RemoveRkpBypass() - Lines 179-202
**Magisk Equivalent:** `remove_rkp_bypass()` in samsung_kernel_patcher.rs

```rust
// Magisk v26.3 (samsung_kernel_patcher.rs ~line 220)
fn remove_rkp_bypass(kernel: &mut [u8]) -> Vec<u8> {
    let mut result = kernel.to_vec();
    
    // RKP bypass patterns are highly variable
    // Common: rkp_check_signature() patched to MOV W0, #0; RET
    // Common: rkp_verify_boot() patched to skip
    // Common: rkp_enabled flag set to 0
    
    // Pattern: "rkp_enabled"
    remove_signature(result, b"rkp_enabled");
    
    // Best effort - full restoration requires stock kernel
    // For now, flag for manual review
    result
}
```

**BACKRabbit mapping:** Lines 180-201 with note about limitations.

### RemoveDefexPatches() / RemoveProcaPatches() - Lines 207-234
**Magisk Equivalent:** `remove_defex_patches()` / `remove_proca_patches()` in samsung_kernel_patcher.rs

```rust
// Magisk v26.3 (samsung_kernel_patcher.rs ~line 260)
fn remove_defex_patches(kernel: &mut [u8]) -> Vec<u8> {
    let mut result = kernel.to_vec();
    
    // Defex modifies syscall_table and security hooks
    // Restoration requires original values
    
    // At minimum, remove signature for detection
    remove_signature(result, b"DEFEX");
    
    result
}

fn remove_proca_patches(kernel: &mut [u8]) -> Vec<u8> {
    let mut result = kernel.to_vec();
    remove_signature(result, b"PROCA");
    result
}
```

**BACKRabbit mapping:** Lines 207-234 direct port.

### Hook Pattern Detection - Lines 274-299
**Magisk Equivalent:** `find_hook_patterns()` in samsung_kernel_patcher.rs

```rust
// Magisk v26.3 (samsung_kernel_patcher.rs ~line 330)
fn find_hook_patterns(kernel: &[u8]) -> Vec<HookPattern> {
    let mut hooks = Vec::new();
    
    for (sig, desc) in [
        (b"magisk_hook", "Magisk hook function"),
        (b"kernel_su", "KernelSU hook"),
        (b"selinux_disable", "SELinux disabled"),
        (b"avc_disable", "AVC disabled"),
    ] {
        if let Some(offset) = find_pattern(kernel, sig) {
            hooks.push(HookPattern { offset, description: desc.to_string() });
        }
    }
    
    hooks
}
```

**BACKRabbit mapping:** Lines 276-298 direct port.

---

## Samsung Kernel Security Features

| Feature | Magic | Description | Magisk Modification |
|---------|-------|-------------|---------------------|
| RKP | `RKP` | Real-time Kernel Protection (KASLR+CFI) | Disables checks, patches rkp_check_signature |
| Defex | `DEFEX` | Defense against Exploits (hardened syscalls) | Restores original syscall table |
| PROCA | `PROCA` | Process Protection (SELinux+) | Weakens process isolation |
| KNOX | `KNOX` | Samsung Knox kernel protection | Bypasses KNOX checks |

---

## ARM64 Syscall Table Restoration

### Stock Syscall Prologue (8 bytes)
```assembly
MOV X0, #0      // 0xD2800000
RET             // 0xD65F03C0
```

### Hook Pattern (4 bytes)
```assembly
B <offset>      // 0x14000000 | imm26
```

### Restoration Process
```
1. Find syscall_table symbol (sys_call_table, nr_syscalls, __sys_trace)
2. Scan 256 bytes after symbol
3. For each 8-byte entry:
   - If starts with B instruction (0x14xxxxxx): HOOKED
   - Replace with stock prologue (MOV X0, #0; RET)
```

---

## Missing/Partial Implementations

1. **RKP Full Restoration** - Patterns highly variable, needs stock kernel diff
2. **Defex Full Restoration** - Requires original syscall table values
3. **PROCA Full Restoration** - Requires original security hook values
4. **KNOX Kernel Patches** - Not fully implemented
5. **Symbol Resolution** - No ELF parsing, relies on string search
6. **Kernel Version Specific** - Offsets vary by kernel version

---

## Test Coverage Needed

| Test Case | Status |
|---|---|
| Analyze_StockKernel_ReturnsStock | ❌ |
| Analyze_MagiskPatched_ReturnsHooked | ❌ |
| Analyze_RKP_SignatureDetected | ❌ |
| Analyze_Defex_SignatureDetected | ❌ |
| Analyze_PROCA_SignatureDetected | ❌ |
| Analyze_KNOX_SignatureDetected | ❌ |
| FindSyscallTable_ValidKernel_ReturnsOffset | ❌ |
| RestoreSyscallTable_HookedEntry_Restored | ❌ |
| RemoveRkpBypass_PatternFound_Flagged | ❌ |
| RemoveDefexPatches_SignatureRemoved | ❌ |
| RemoveProcaPatches_SignatureRemoved | ❌ |
| FindHookPatterns_MagiskHook_Detected | ❌ |
| ApplyHexPatch_ValidOffset_Applied | ❌ |

---

## Source File References

**BACKRabbit Source:** `BACKRabbit.MagiskCore/SamsungKernel/SamsungKernelPatcher.cs` (388 lines)

**Magisk Sources (in knowledge-base):**
- `v26.0_samsung_kernel_patcher.rs` through `v27.0_samsung_kernel_patcher.rs`
- `samsung_kernel_patcher.sh` (shell implementation)
- `samsung_kernel_patcher.cpp` (C++ in repo root)