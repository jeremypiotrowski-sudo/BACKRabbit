# Rescue Pipeline — Consolidated Pre-Testing Evaluation

> **Synthesized from**: DeepSeek Agent, NEMO Agent, GLM Agent plan-mode explorations  
> **Verified on disk**: June 21, 2026 — all source files, test directories, and KB docs inspected  
> **Purpose**: Prioritized finishing touches before live-device testing this afternoon

---

## What All Three Agents Agree On (Convergent Findings)

| # | Finding | Agents | Verified on Disk |
|---|---------|--------|------------------|
| 1 | **No `RescueOrchestratorTests` / `PartitionRestorerTests` / `MagiskRemoverTests` exist** | All three | ✅ Confirmed — `Firehose.Tests/Rescue/` has only `QFuseDatabaseTests.cs` + `RescueReportTests.cs` |
| 2 | **No dry-run unit test asserting zero write calls** | All three | ✅ Confirmed — dry-run gating is behavioral only (if/else branches), no mock-based test |
| 3 | **`MagiskVerifier.cs` does not exist as standalone file** | All three | ✅ Confirmed — verification logic is inline in `MagiskRemover.cs:160-172` and `PartitionDiagnostics.cs:143-173` |
| 4 | **Phase 1–4 execution evidence is absent** | All three | ✅ Confirmed — `TESTING_PROTOCOL.md` has protocol on paper, no Execution Log appendix |
| 5 | **USB transport speed logging not implemented** | DeepSeek, NEMO | ✅ Confirmed — flagged P2 in `TESTING_PROTOCOL.md:258`, no speed-logging code in `PartitionRestorer.cs` |
| 6 | **Console.WriteLine throughout — no structured logger** | NEMO, GLM | ✅ Confirmed — `RescueOrchestrator.cs`, `MagiskRemover.cs`, `PartitionRestorer.cs`, `PartitionDiagnostics.cs` all use direct console writes |
| 7 | **Emergency quick-start card would reduce panic-mode cognitive load** | DeepSeek, GLM | ✅ Confirmed — current docs are comprehensive but assume study time |

---

## What Was Already Implemented (False Gaps in Agent Reports)

| Claimed Gap | Agent | Actual State on Disk |
|-------------|-------|---------------------|
| "No pre-flash capacity/offset validation" | GLM | **Already exists** — `PartitionRestorer.cs:93-107` compares backup size to GPT partition size with 10% tolerance warning |
| "CROSS_REFERENCE_INDEX has stale MagiskVerifier planned entries" | NEMO | **Not found** — CROSS_REFERENCE_INDEX does not mention MagiskVerifier.cs; the stale reference is in `TESTING_PROTOCOL.md:147` (`[MagiskVerifier]` log tag) |
| "MagiskUninstaller.cs not located" | GLM | **Exists** at `BACKRabbit.MagiskCore/Services/MagiskUninstaller.cs` (259L) — it's in MagiskCore, not Firehose/Rescue |
| "No size/offset validation gate" | GLM | **Partially implemented** — size check exists; no auto-rollback on verify failure (just logs warning at line 130) |

---

## Prioritized Finishing Touches (Do Before Testing)

### Tier 1 — Must Do (Prevents False Confidence, Catches Bugs Before Device)

**1. Add `RescueOrchestratorTests` with mock FirehoseClient covering dry-run paths**  
- **Impact**: High — validates the most critical safety mechanism without a device  
- **Effort**: ~2 hours — mock `FirehoseClient` (already has `IDeviceTransport` interface pattern), test 4 scenarios:  
  a. Dry-run with tampered partitions → asserts zero write/erase calls, all actions `DryRunSkipped`  
  b. Dry-run with clean device → asserts diagnosis-only path  
  c. Live-run with tampered partitions → asserts write/erase called  
  d. Post-rescue verdict recalculation (Tampered→Normal transition)  
- **File**: Create `BACKRabbit.Protocol.Firehose.Tests/Rescue/RescueOrchestratorTests.cs`  
- **Agents**: DeepSeek #1, NEMO #1, GLM #1 — unanimous top priority

**2. Add dry-run assertion test: mock client → zero `program`/`reset`/`erase` calls**  
- **Impact**: High — proves `--dry-run` cannot accidentally flash  
- **Effort**: ~30 min — can be folded into RescueOrchestratorTests  
- **Validation**: Grep test output for `WritePartition\|ErasePartition\|program\|reset` → must be zero matches  
- **Agents**: NEMO #1 specifically, DeepSeek/GLM implicitly

**3. Fix `TESTING_PROTOCOL.md:147` stale `[MagiskVerifier]` log tag**  
- **Impact**: Medium — prevents operator confusion when grepping logs for a tag that doesn't exist  
- **Effort**: 5 min — replace `[MagiskVerifier]` with `[MagiskRemover]` (the actual class that emits verification output at lines 160-172)  
- **File**: `knowledge-base/firehose/TESTING_PROTOCOL.md` line 147

### Tier 2 — Should Do (Strengthens Audit Trail, Reduces Operator Risk)

**4. Persist `RescueReport` to disk on dry-run and print its SHA-256**  
- **Impact**: Medium — dry-run currently prints report to console but doesn't save it; saving creates an audit artifact even for dry-runs  
- **Effort**: ~15 min — in `RescueOrchestrator.cs:182-192`, remove the `!string.IsNullOrEmpty(_backupDir)` guard for dry-run path, or save to a temp/fallback path  
- **Current behavior**: Dry-run with no backup dir prints JSON to console only (line 190-191)  
- **Agent**: NEMO #4

**5. Add `--force` override for `_neverRestore` blocklist with confirmation prompt**  
- **Impact**: Medium — prevents tool from being useless if a blocklisted partition genuinely needs restoration  
- **Effort**: ~30 min — add `--force` option to `FirehoseCommands.cs`, thread through `RescueOrchestrator` → `PartitionRestorer`, gate behind "I understand the risks" typed confirmation  
- **Agent**: DeepSeek #3

**6. Create one-page "Emergency Rescue Quick Start" card**  
- **Impact**: Medium — reduces cognitive load for panicked users  
- **Effort**: ~30 min — distill to: detect device → source firmware → dry-run → rescue → interpret report  
- **File**: Create `knowledge-base/firehose/QUICK_START.md`  
- **Agents**: DeepSeek #5, GLM (recommends "Rescue Quickstart")

### Tier 3 — Nice to Have (Quality/Polish, Not Blocking Testing)

**7. Extract `MagiskVerifier.cs` from inline verification logic**  
- **Impact**: Medium — enables independent testing of verify-after-clean step, reduces duplication between `MagiskRemover.cs:160-172` and `PartitionDiagnostics.cs:143-173`  
- **Effort**: ~1 hour — extract shared `VerifyBootForMagisk(byte[] bootData)` method, use in both places  
- **Risk**: Low — pure extraction, no behavior change  
- **Agents**: All three agree this should exist; DeepSeek #1, GLM #3

**8. Inject `IRescueLogger` interface replacing `Console.WriteLine`**  
- **Impact**: Low-Medium — enables TUI/log capture, structured output, per-phase verbosity  
- **Effort**: ~1.5 hours — define interface, default Console implementation, thread through 4 classes  
- **Risk**: Medium — touches 4 files, could introduce regressions if not careful  
- **Agents**: NEMO #2, GLM #3

**9. Implement USB transport speed logging in `PartitionRestorer`**  
- **Impact**: Low — catches degraded USB 2.0 links (<800KB/s) that risk incomplete flashing  
- **Effort**: ~20 min — timestamp before/after `ReadPartitionAsync`, log bytes/sec  
- **Already planned**: P2 in `TESTING_PROTOCOL.md:258`  
- **Agent**: DeepSeek #4

**10. Add `rescue self-check` CLI command automating Phase 0 Go/No-Go gates**  
- **Impact**: Medium — automates pre-flight checks (backup exists, hashes recorded, build passes, tests pass)  
- **Effort**: ~1 hour — new command in `FirehoseCommands.cs`, runs build verification + test suite + backup integrity  
- **Agent**: GLM #4

---

## What Does NOT Need Attention Before Testing

| Item | Reason |
|------|--------|
| Rebaseline CROSS_REFERENCE_INDEX (NEMO #3) | No stale "MagiskVerifier planned" entries found — index is accurate against disk |
| Malformed-input fuzz tests for parsers (GLM #5) | Existing unit tests cover happy-path; fuzz testing is valuable but not pre-testing critical |
| Auto-rollback on verify failure (GLM #2) | Current behavior (log warning + continue) is correct for rescue — operator should decide, not auto-revert |
| Offline build viability testing | Requires NuGet mirror setup — orthogonal to rescue pipeline testing |

---

## Pre-Testing Execution Checklist

Before connecting a fused device this afternoon:

- [ ] **Tier 1 complete**: RescueOrchestratorTests pass (dry-run + live-run mock scenarios)
- [ ] **Tier 1 complete**: Dry-run assertion test confirms zero write calls
- [ ] **Tier 1 complete**: TESTING_PROTOCOL.md line 147 fixed
- [ ] `dotnet build -c Release` — 0 errors
- [ ] `dotnet test -c Release` — all existing 32+ Firehose tests + new orchestrator tests pass
- [ ] Dry-run against stock backup directory produces sensible diagnosis report
- [ ] `RescueReport.IsDryRun: true` verified in JSON output
- [ ] Emergency recovery kit prepared (Odin, stock firmware, TWRP backup)

---

## Agent-Specific Contributions

| Agent | Unique Value | Adopted? |
|-------|-------------|----------|
| **DeepSeek** | `--force` override for blocklist, emergency quick-start card, USB speed logging | ✅ All adopted |
| **NEMO** | Dry-run regression test specificity, ILogger injection, persist dry-run report with SHA-256 | ✅ All adopted |
| **GLM** | Pre-flash validation gate analysis (found partially implemented), `rescue self-check` command, malformed-input tests | ✅ Self-check adopted; fuzz tests deferred |

---

## Verdict

The rescue pipeline is **structurally sound** for testing. Dry-run is end-to-end, blocklist is enforced, forensic snapshots are captured, and post-rescue re-verification exists. The critical gap is **no unit tests for the orchestrator itself** — the one class that gates all destructive operations. Adding mock-based orchestrator tests (Tier 1) is the single highest-value action before connecting a fused device.

**If Tier 1 is completed before testing**: Risk of accidental flash is near-zero. The dry-run path will have been proven in code, not just in documentation.

**If testing proceeds without Tier 1**: Dry-run safety relies entirely on code review of if/else branches — no automated guard. Given the stakes (fused device), this is an unnecessary risk.

---

*Synthesized 2026-06-21 from DeepSeek, NEMO, and GLM plan-mode evaluations + on-disk verification*