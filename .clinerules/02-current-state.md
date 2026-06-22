# Current State — Updated 2026-06-22 | Commit: ed52cee

## Build & Test
- Build: 0 errors, 11 warnings (LibUsbDotNet target, SharpCompress vuln)
- Firehose.Tests: 10 FirehoseResponseTests pass; full suite has 1 pre-existing failure (RebootDownloadManagerTests cancellation)
- BACKRabbit.Tests: 14 BootImageParserTests, 10 failing (parser offset/header-size bug), 4 passing
- BACKRabbit.Tests overall: 55 total, 39 pass, 16 pre-existing (firmware data missing)

## Loader Coverage
- SM8750 (S25/S24 Ultra): 001920E100200000_4A14C27B518909E1_fhprg.elf (1,011,056 bytes)
- Z Fold Ultra 7 (SM-F966U1): No loader found

## Known Blockers (Tier 0)
- ✅ PrintGptAsync real GPT XML parsing — FIXED
- 🔄 BootImageParser offset/header-size bug breaking V0-V4/MTK/DHTB round-trip — IN PROGRESS
- 🔄 RebootDownloadManager cancellation not honored — IN PROGRESS

## Tech Debt
- 3 MockFirehoseClient implementations (should be 1)
- goto GenerateReport in RescueOrchestrator
- RebootDownloadManager cancellation test fails