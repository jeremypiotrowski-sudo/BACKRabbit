---
paths:
  - "BACKRabbit.Protocol.Firehose/**"
  - "BACKRabbit.Protocol.Firehose.Tests/**"
---
# Rescue Pipeline Rules

- BACKRabbit is a NAND normalization engine, not a Magisk remover.
- 8-phase pipeline:
  1. Verify firmware backup
  2. Diagnose partitions (full-GPT audit)
  3. Audit QFuses
  4. Restore tampered partitions (sparse repair)
  4c. Wipe-data gate (if `--wipe-data` set, erase userdata with 3-gate safety)
  5. Remove Magisk
  6. Final verification
  7. Generate report and reset device
- Dry-run gate: hard `if (_dryRun)` at every destructive phase, ZERO bypass.
- Stock-only write: `VerifyStockIntegrity` checks manifest.json SHA256 BEFORE any write.
- Blocklist: `sec`, `ddr`, `limits`, `apdp`, `msadp` — never written without `--force`.
- `--wipe-data` has three gates: flag, typed confirmation, dry-run override.
- `FullGptAuditResult` sector addresses are BYTE OFFSETS — convert to sector numbers (`addr / 512`) before `WritePartitionBlocksAsync`.
- `startSector` parameter is a SECTOR NUMBER, not byte offset.
- Mock-based tests prove logic, not protocol. When fixing protocol parsing, add tests with REAL response data.