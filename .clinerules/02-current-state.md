


# Current State — Updated 2026-06-22 | Commit: 52b5af5

## Build & Test
- Build: 0 errors, 17 warnings (LibUsbDotNet target, SharpCompress vuln)
- Firehose.Tests: 63 total, 62 pass, 1 pre-existing failure (RebootDownloadManagerTests cancellation)
- BACKRabbit.Tests: 55 total, 49 pass, 6 pre-existing (F966U1IntegrationTests — firmware data missing)
- BootImageParserTests: 14/14 pass

## Firmware Extraction
- Tab A (SM-T307U): ✅ Extracted 25 partitions to `t307u-stock/` including `boot.img` and `vbmeta.img`
  - Extraction method: `System.Formats.Tar` for outer TAR + `LZ4Stream.Decode` for `.img.lz4` entries
  - AP archive partially truncated at one large entry ("Stream was too long"); still yielded boot/recovery/dt/dtbo
  - Boot image analysis: AOSP v1, page size 2048, kernel 29.1 MB, ramdisk 0 bytes, no Magisk artifacts, stock
- Z Fold 7 (SM-F966U1): ⏳ Firmware staged; extraction pending (16.68 GB archive)

## Loader Coverage
- SM8750 (S25/S24 Ultra): 001920E100200000_4A14C27B518909E1_fhprg.elf (1,011,056 bytes)
- Z Fold Ultra 7 (SM-F966U1): No loader found
- Tab A (SM-T307U): No loader found (MSM IDs 0x000000E1/0x000004D0/0x000005D0/0x00060001 all absent from 823 loaders)


## Known Blockers (Tier 0)
- ✅ PrintGptAsync real GPT XML parsing — FIXED (commit 04215b1)
- ✅ BootImageParser offset/header-size bug breaking V0-V4/MTK/DHTB round-trip — FIXED (commit cc84952)
- 🔄 RebootDownloadManager cancellation not honored — remaining tech debt

## Tech Debt
- 3 MockFirehoseClient implementations (should be 1)
- goto GenerateReport in RescueOrchestrator
- RebootDownloadManager cancellation test fails
