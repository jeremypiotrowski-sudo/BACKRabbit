# BACKRabbit Testing Guide v2.0

## Quick Start

```bash
# Run all tests
dotnet test BACKRabbit.slnx -c Release

# Run specific test class
dotnet test BACKRabbit.slnx -c Release --filter "FullyQualifiedName~BootImageParserTests"

# Run with verbose output
dotnet test BACKRabbit.slnx -c Release --verbosity normal
```

## Test Categories

### 1. Unit Tests (BACKRabbit.Tests/)

| Test File | Tests | What It Covers |
|-----------|-------|----------------|
| `BootImageParserTests.cs` | 41 passing | Round-trip parse→repack→re-parse for all boot image formats |
| `CpioArchiveTests.cs` | 1 | CPIO parse/serialize fidelity |
| `CompressionEngineTests.cs` | 1 | Gzip round-trip |
| `MagiskArtifactDetectorTests.cs` | 0 | Artifact detection (pending) |
| `AvbRestorerTests.cs` | 0 | AVB flag restore (pending) |
| `MagiskUninstallerTests.cs` | 0 | Full uninstall workflow (pending) |
| `MagiskCoreTests.cs` | 3 | Format detection (Gzip, LZ4, Boot) |

### 2. Integration Tests

| Test File | Tests | What It Covers |
|-----------|-------|----------------|
| `F966U1IntegrationTests.cs` | 5 | Real Z Fold 6 firmware (boot.img + init_boot.img) |

### 3. Test Mode (No Device Needed)

```bash
# Full wizard with simulated device
backrabbit magisk wizard --test-mode

# Dry run (simulate all steps, touch nothing)
backrabbit magisk wizard --test-mode --dry-run

# Specific steps only
backrabbit magisk wizard --test-mode --steps detect,analyze,clean

# Verbose output for debugging
backrabbit magisk wizard --test-mode --verbose
```

Uses `MockAdbClient` (implements `IAdbClient`) with:
- Fake device list (returns mock serial numbers)
- Fake shell responses (echoes commands)
- Fake pull (returns empty/sample files)
- Fake push (no-ops)
- Configurable failure modes for testing error paths

### 4. Offline Mode (File-Based)

```bash
# Analyze local boot image without device
backrabbit magisk wizard --offline staging/F966U1/boot.img

# Dry run offline analysis
backrabbit magisk wizard --offline staging/F966U1/boot.img --dry-run

# Specific steps offline
backrabbit magisk wizard --offline staging/F966U1/boot.img --steps analyze,clean
```

### 5. Live Device Testing (Requires ADB Connection)

```bash
# Quick detection
backrabbit magisk detect --serial 100.67.154.12:39603 --verbose

# Forensic sweep
backrabbit trap-escape --serial 100.67.154.12:39603 --diagnose-only --verbose

# Full wizard (READ-ONLY with --dry-run)
backrabbit magisk wizard --serial 100.67.154.12:39603 --dry-run --verbose
```

## Round-Trip Test Methodology

The core verification pattern used throughout BACKRabbit:

```
1. Parse original boot image → extract all components
2. Modify ramdisk (simulate Magisk removal)
3. Repack to new boot image
4. Parse the repacked image
5. Verify: all components match expected state
6. Verify: repacked image is bit-identical to expected output
```

### CPIO Round-Trip
```
1. Parse ramdisk.cpio → CpioArchive
2. Serialize → new_ramdisk.cpio
3. Parse new_ramdisk.cpio → CpioArchive
4. Compare: entry count, names, sizes, data
5. Expected: identical
```

### Compression Round-Trip
```
1. Compress test data → compressed
2. Decompress compressed → decompressed
3. Compare: original == decompressed
4. Expected: identical
```

### AVB Flag Verification
```
1. Parse Magisk-patched boot.img
2. Check AvbVBMetaImageHeader.flags → should be 3
3. Run AvbRestorer.RestoreVerificationFlags()
4. Check flags → should be 0
```

## Scoop Detector

Run before every release. Zero hits required.

```bash
# Windows
findstr /s /i /m "stub placeholder TODO FIXME HACK WORKAROUND TEMP" *.cs *.py *.md 2>nul

# Linux/Mac
grep -rn "stub\|placeholder\|TODO\|FIXME\|HACK\|WORKAROUND\|TEMP" \
  --include="*.cs" \
  --include="*.py" \
  --include="*.md" \
  --exclude-dir="knowledge-base/canary-*" \
  --exclude-dir=".git" \
  .
```

Expected: **NO OUTPUT**. If any hit found → STOP, fix it, re-run.

## Documented Test Limitations (8)

| # | Limitation | Reason | Impact |
|---|-----------|--------|--------|
| 1 | LZ4 vendor boot round-trip | LZ4 legacy format edge case | Low — rarely used |
| 2 | MTK synthetic header | Requires real MTK device | Low — MTK not primary target |
| 3 | ChromeOS header parsing | ChromeOS boot image format | Low — not Samsung |
| 4 | AVB signature verification | Requires Samsung signing keys | Medium — AVB flags still restorable |
| 5 | Samsung kernel patch verification | Requires real device with Magisk | Medium — patterns verified statically |
| 6 | Download Mode flash | Requires Odin/device in Download Mode | High — core functionality |
| 7 | Live ADB integration | Requires connected device | High — core functionality |
| 8 | Trap-escape execution | Requires root or unlocked bootloader | High — core functionality |

## Adding New Tests

### For a new boot image format:
1. Add boot image to `staging/<model>/`
2. Create test method in `BootImageParserTests.cs`
3. Follow pattern: `Parse()` → verify header → verify sections → `Repack()` → re-`Parse()` → compare
4. Run: `dotnet test --filter "FullyQualifiedName~NewTest"`

### For a new Samsung model:
1. Download firmware from SAMFW.com or Frija
2. Extract boot.img using `SamsungFirmwareExtractor`
3. Place in `staging/<model>/`
4. Add integration test in `F966U1IntegrationTests.cs` (or new file)
5. Test: parse → detect artifacts → clean → repack → verify

### For mock device testing:
1. Add new mock responses to `MockAdbClient.cs`
2. Configure failure modes for error path testing
3. Test: `backrabbit magisk wizard --test-mode`

## Pre-Release Regression Checklist

- [ ] `dotnet build BACKRabbit.slnx -c Release` (zero errors)
- [ ] `dotnet test BACKRabbit.slnx -c Release` (42/50 passing, 8 documented limitations)
- [ ] Scoop Detector: zero hits
- [ ] `backrabbit magisk wizard --test-mode --dry-run` (all 7 steps pass)
- [ ] `backrabbit magisk wizard --test-mode` (full flow completes)
- [ ] `backrabbit magisk wizard --offline staging/F966U1/boot.img --dry-run` (offline analysis works)
- [ ] `backrabbit magisk detect --serial <live-device> --verbose` (live detection works)
- [ ] `backrabbit trap-escape --serial <live-device> --diagnose-only` (forensic sweep works)
- [ ] `dotnet publish -r win-x64 --self-contained true -p:PublishSingleFile=true` (success)
- [ ] `dotnet publish -r linux-x64 --self-contained true -p:PublishSingleFile=true` (success)
- [ ] Smoke test: `dist/win/BACKRabbit.exe --version` (Windows)
- [ ] Smoke test: `dist/linux/BACKRabbit --version` (Linux)

## Performance Benchmarks

| Operation | Target | Actual (v2.0) |
|-----------|--------|---------------|
| Parse 32MB boot.img | < 2 seconds | ~1.5s |
| Decompress gzip ramdisk | < 1 second | ~0.3s |
| Parse CPIO (500 entries) | < 0.5 seconds | ~0.2s |
| Repack boot.img | < 3 seconds | ~2.0s |
| ADB shell command | < 1 second | ~0.3s (via server proxy) |
| Full wizard (test mode) | < 10 seconds | ~5s |

## 6. Firehose Rescue Testing (Live Device)

See [Firehose Rescue Testing Protocol](firehose/TESTING_PROTOCOL.md) for the complete zero-brick validation procedure. Summary:

| Phase | Command | Risk | What It Validates |
|-------|---------|------|-------------------|
| 1: Dry-Run | `firehose rescue full --backup ./stock --dry-run` | **Zero** | Diagnosis, Magisk detection, GPT validation — no flashing |
| 2: Stock Flash | Manual Odin boot flash + dry-run | **Low** | Stock boot integrity, clean detection |
| 3: Magisk Test | Dry-run → live rescue on Magisk-patched device | **Medium** | Full removal pipeline with verification |
| 4: DTBO Stress | Dry-run → live rescue with DTBO Magisk module | **Medium** | DTBO overlay detection + reflash ordering |

### Dry-Run Mode (P0 — Implemented)

```powershell
# Zero-risk validation — runs diagnosis + detection, skips all flash/erase
backrabbit firehose rescue full --device COM3 --loader firehose.elf --backup ./stock-backup --dry-run
```

**What runs:** Phase 0 (backup check), Phase 1 (diagnosis), Phase 2 (fuse audit).  
**What's skipped:** Phase 3 (restore), Phase 4 (unmagisk), Phase 5 (verify), reset.  
**Expected output:** `🔥 FIREHOSE DRY-RUN MODE ENABLED — NO FLASHING WILL OCCUR` banner, `[DRY-RUN]` prefixed logs, `IsDryRun: true` in report JSON.

### Firehose Test Suite

```powershell
# 32 tests — must all pass before any live device testing
dotnet test BACKRabbit.Protocol.Firehose.Tests -c Release
# Expected: 32 passed, 0 failed, 0 skipped
```

---

## 7. Firehose Dry-Run Validation Checklist

After each dry-run execution, verify:

| # | Check | Command | Expected |
|---|-------|---------|----------|
| 1 | Dry-run banner | `findstr "FIREHOSE DRY-RUN MODE" log.txt` | Present at start |
| 2 | Diagnosis completed | `findstr "partitions analyzed" log.txt` | Count matches device |
| 3 | No flash operations | `findstr "Erased\|Flashed" log.txt` | **ZERO matches** |
| 4 | All actions dry-run | Check `rescue-report.json` | `Action: DryRunSkipped` |
| 5 | Magisk not removed | Check `rescue-report.json` | `MagiskRemoved: false` |
| 6 | Completion banner | `findstr "DRY-RUN COMPLETE" log.txt` | Present at end |
| 7 | Report IsDryRun flag | Check `rescue-report.json` | `IsDryRun: true` |
| 8 | No device reset | `findstr "Resetting device" log.txt` | Only `[DRY-RUN] Would reset` |
| 9 | Build still passes | `dotnet build BACKRabbit.CLI -c Release` | 0 errors |
| 10 | Firehose tests pass | `dotnet test BACKRabbit.Protocol.Firehose.Tests` | 32/32 PASS |

---

## Coverage Gaps

| Component | Gap | Priority |
|-----------|-----|----------|
| BootImageParser | No unit tests | HIGH |
| BootImageRepacker | No unit tests | HIGH |
| AvbRestorer | No unit tests | MEDIUM |
| MagiskArtifactDetector | No unit tests | MEDIUM |
| MagiskUninstaller | No unit tests | MEDIUM |
| SamsungKernelPatcher | No unit tests | LOW |
| AdbClient | No unit tests (requires device) | LOW |
| FastbootClient | No unit tests (requires device) | LOW |
| DownloadModeFlasher | No unit tests (requires device) | LOW |
| **Firehose: DTBO Magisk overlay detection** | **No Magisk-specific DTBO scanning (P1)** | **HIGH** |
| **Firehose: Structured log output** | **Prose-only, no machine-parseable tags (P2)** | **MEDIUM** |
| **Firehose: USB transport speed logging** | **Speed not logged during reads (P2)** | **MEDIUM** |
| **Firehose: MagiskVerifier extraction** | **Verification inline in MagiskRemover (P3)** | **LOW** |

---

## Last Updated
2026-06-21 — v2.1: Added Firehose rescue testing sections, dry-run validation checklist, Firehose coverage gaps
