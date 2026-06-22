# BACKRabbit Firehose Operations Manual

> **Purpose**: How to use the Firehose client correctly — including non-obvious constraints and failure modes.
> **Generated**: 2026-06-21 | **Commit**: `9d328fe`

---

## 1. The Sahara Handshake Sequence

### Why It's Non-Optional

The Sahara protocol is Qualcomm's EDL (Emergency Download Mode) bootloader handshake. It must complete before any Firehose XML commands can be sent. Skipping it results in `SAHARA_ERROR_INVALID_RESPONSE` or USB pipe errors.

### Sequence (9 Valid States)

```
Disconnected → HelloReceived → HelloSent → ImageUploading → ImageUploadComplete → CommandMode
                                                                                        ↓
                                                                                      Done
```

**State Machine**: `SaharaStateMachine.cs` (65 lines) — 9 states, 12 validated transitions.

| State | Trigger | Action |
|-------|---------|--------|
| `Disconnected` | USB device enumerated | Wait for HELLO_REQ packet |
| `HelloReceived` | Device sends HELLO_REQ | Parse chip info (MSM ID, PK hash, serial) |
| `HelloSent` | BACKRabbit sends HELLO_RSP | Device accepts programmer |
| `ImageUploading` | Device requests READ_DATA | Upload Firehose programmer ELF in 4K chunks |
| `ImageUploadComplete` | Device sends DONE | Programmer executed, Firehose channel open |
| `CommandMode` | Firehose <configure> ACK | Ready for XML commands |
| `Done` | Reset command sent | Device reboots |

### Critical Timing Constraints

- **HELLO_REQ timeout**: Device sends within 2 seconds of USB enumeration. If not received, device may not be in EDL mode (check PID: 9008, 900E, 901D).
- **Image upload chunk size**: 4KB (`SaharaLoaderUploader.cs`). Larger chunks cause USB buffer overflows on Qualcomm 9008 bulk endpoint.
- **Inter-chunk delay**: 50ms implicit (USB turnaround). No explicit delay needed — the READ_DATA/ACK cycle provides natural pacing.

### Error States

- `SaharaState.Error` — reached from any state on protocol violation
- Recovery: `Error → Disconnected` (re-enumerate USB, restart handshake)

---

## 2. Firehose XML Command Protocol

### Command Structure

All commands are XML fragments sent as raw bytes over the USB bulk endpoint:

```xml
<?xml version="1.0" encoding="UTF-8" ?>
<data>
  <command attribute="value" ... />
</data>
```

### Supported Commands (FirehoseClient.cs)

| Command | XML Tag | Purpose | Response Pattern |
|---------|---------|---------|-----------------|
| NOP | `<nop />` | Heartbeat test | ACK or NAK |
| Configure | `<configure ... />` | Set memory type, payload size | ACK → Firehose ready |
| Read Partition | `<read ... />` | Read raw partition data | ACK → raw sector data stream |
| Write Partition | `<program ... />` | Write raw data to partition | ACK → send data → ACK/NAK |
| Erase Partition | `<erase ... />` | Erase partition by name | ACK or NAK |
| Print GPT | `<gpt ... />` | Dump partition table | ACK → XML partition list |
| Get Storage Info | `<getstorageinfo />` | Query storage type (UFS/eMMC) | ACK → storage info string |
| Peek | `<peek address="" size="" />` | Read memory at address | ACK → raw bytes |
| Poke | `<poke address="" value="" />` | Write 32-bit value to address | ACK or NAK |
| Reset | `<power value="system" />` | Reboot device | No response (device resets) |
| Set Bootable | `<setbootablestoragedrive />` | Set active LUN | ACK or NAK |

### Response Parsing

`FirehoseResponse.cs` parses mixed XML + log streams. The Firehose programmer interleaves:
- XML ACK/NAK fragments (`<response value="ACK" />`)
- Log messages (`<log value="..."/>`)
- Raw data (after read commands)

**Critical**: Responses may arrive in multiple USB packets. The parser accumulates up to 1MB before declaring timeout. Do NOT assume a single `ReceiveRawAsync()` call gets the complete response.

---

## 3. Packet Size Limits

### USB Bulk Transfer Constraints

Qualcomm 9008 EDL mode uses USB bulk endpoints with:
- **Max packet size**: 512 bytes (USB 2.0) or 1024 bytes (USB 3.0)
- **Practical chunk size**: 4KB (empirically stable across hubs)
- **Max payload**: 1MB (`MaxPayloadSizeToTargetInBytes` in configure)

### Why 4KB Chunks

- Matches Qualcomm 9008 bulk endpoint buffer size
- Prevents `ERROR_SEM_TIMEOUT` on Windows USB stack
- Allows interleaved ACK checking between chunks
- Validated with Beagle USB 480 analyzer

### Write Partition Flow

```
1. Send <program> XML command
2. Wait for ACK (2s timeout)
3. Send raw data in 1MB chunks
4. Wait for final ACK/NAK (5s timeout)
5. Verify: read back partition, compare SHA256
```

---

## 4. Timeout Values

| Operation | Timeout | Reason |
|-----------|---------|--------|
| HELLO_REQ wait | 5s | Device sends immediately in EDL |
| Image upload chunk | 2s per chunk | USB turnaround + device processing |
| Firehose command response | 500ms initial + 200ms grace | XML parsing + log flush |
| Write data ACK | 2s | Device erases before writing |
| Write final ACK | 5s | Device verifies written data |
| Read data stream | 1MB max or 500ms idle | Partition size unknown until GPT query |
| Full download (FUS) | 30 min | 8GB firmware over variable connections |

---

## 5. Partition Name Validation

### Naming Rules

Partition names come from:
1. **GPT table** (`PrintGptAsync`) — authoritative source
2. **Firmware .tar.md5** (`SamsungFirmwareExtractor`) — extracted filenames

### Validation Chain

```
FirmwareSourcer extracts .tar.md5 → gets filenames (e.g., "boot.img")
  ↓
Strips extension → partition name ("boot")
  ↓
PartitionDiagnostics queries GPT → confirms partition exists
  ↓
PartitionRestorer flashes → verifies SHA256 after write
```

### Safety: Never Restore These Partitions

`PartitionRestorer.cs` has a blocklist:
- `sec` — QFuse data (permanent, read-only)
- `ddr` — DDR training data (device-specific calibration)
- `limits` — Hardware limits
- `apdp`, `msadp` — Debug policy

---

## 6. Read-After-Write Verification

### When It's Performed

| Operation | Verification | Reason |
|-----------|-------------|--------|
| Partition restore | ✅ SHA256 compare | Safety-critical |
| Magisk removal | ✅ Re-parse + detect | Confirm Magisk gone |
| Boot image repack | ✅ Re-parse + compare | Prevent brick |
| QFuse audit | ❌ Read-only | No write performed |
| GPT dump | ❌ Read-only | No write performed |

### Verification Flow

```
1. Write partition via Firehose
2. Read back entire partition
3. Compute SHA256 of read data
4. Compare to known-good SHA256
5. If mismatch: log warning, mark RestoreAction.Verified = false
```

---

## 7. Transport Layer

### WinUsbTransport

`WinUsbTransport.cs` — Two constructors:
- `WinUsbTransport(int vid, int pid)` — USB device by VID/PID
- `WinUsbTransport(string devicePath)` — Serial/COM port

### Platform Support

- **Windows**: `System.IO.Ports.SerialPort` for COM ports
- **Linux/macOS**: `FileStream` on `/dev/tty*` (guarded by `RuntimeInformation.IsOSPlatform()`)

### Interface

`IDeviceTransport.cs`:
- `ConnectAsync()` / `DisconnectAsync()`
- `SendPacketAsync(SaharaPacket)` / `ReceivePacketAsync()`
- `SendRawAsync(byte[])` / `ReceiveRawAsync(int maxBytes)`

---

## 8. Rescue Workflow Phases

### Full Sequence (7 Phases)

```
[0/7] Verify firmware backup — check for required .img files
[1/7] Diagnose partitions — 13 security-critical partitions analyzed
[2/7] Audit QFuses — peek at fuse registers, report blown count
[3/7] Restore tampered partitions — erase → write → verify
[4/7] Remove Magisk — parse boot → detect → clean ramdisk → repack → write → verify
[5/7] Final verification — re-run diagnostics on restored partitions
[6/7] Generate report — JSON + console summary, reset device
```

### Phase 0: Firmware Sourcing (TUI)

If no `--backup` directory provided:
1. Launch Spectre.Console TUI
2. Detect device model/region via Firehose/USB
3. Prompt user to confirm/edit model, region, IMEI
4. Download from Samsung FUS (auth token required)
5. Decrypt .enc4 → extract .zip → extract .tar.md5 → .img files
6. Pass output directory to orchestrator as `backupDir`

---

## 9. USB Transport Layer (Deep Dive)

### Endpoint Assignment

BACKRabbit hardcodes two USB bulk endpoints (`UsbDeviceManager.cs:39,65`):
- **EP 0x02 (OUT)** — Host→Device writes. All Sahara commands, Firehose XML payloads, and raw flash data flow through this endpoint.
- **EP 0x81 (IN)** — Device→Host reads. Sahara responses, Firehose ACKs, and partition dumps return through this endpoint.

These are standard Qualcomm EDL endpoint assignments. Samsung devices in Download Mode expose the same endpoint pair when the Sahara protocol handshake succeeds and the device transitions to Firehose mode.

### Packet Framing (Sahara Protocol)

Every Sahara packet uses an 8-byte header followed by a variable-length payload (`WinUsbTransport.cs`):

```
[uint32 Command (LE)] [uint32 Length (LE)] [payload of (Length - 8) bytes]
```

- **Command:** Sahara command code (e.g., 0x01 = Hello, 0x02 = HelloResponse, 0x03 = ReadData, 0x06 = CommandDone)
- **Length:** Total packet size including the 8-byte header
- **Receive logic:** Read 8 bytes → parse command+length → read (length-8) payload bytes
- **Send logic:** Serialize packet to byte array → write entire buffer to EP 0x02

### Per-Operation Timeouts

| Operation | Timeout | Source |
|-----------|---------|--------|
| USB bulk write | 5000ms | `UsbDeviceManager.Write()` default |
| USB bulk read | 5000ms | `UsbDeviceManager.Read()` default |
| Serial read/write | 5000ms | `WinUsbTransport` serial mode |
| Sahara Hello response | 30000ms | `FirehoseClient` handshake |
| Firehose configure ACK | 120000ms | `FirehoseClient` per-operation |
| Firehose program (flash) | 120000ms | Per-partition, size-dependent |
| Full rescue operation | No hard cap | Orchestrator step-by-step |

### Transport Fallback Chain

1. **WinUSB** (primary) — LibUsbDotNet via `UsbDeviceManager`, VID 0x04E8, Download Mode PIDs [0x685D, 0x6860, 0x6862, 0x6864, 0x6601]
2. **Serial/COM** (fallback) — 115200 baud, 8N1, 4096-byte stream buffer, for devices exposing EDL via serial
3. **Unix TTY** (Linux/macOS fallback) — Same framing, different device path

### Device Detection

- `UsbDeviceManager.FindSamsungDownloadMode()` — Opens by VID 0x04E8, PID 0x6601 (most common Download Mode PID)
- `UsbDeviceManager.ListDevices(samsungOnly: true)` — Enumerates all USB devices, filters by VID 0x04E8
- `FirehoseDeviceDetector` — Wraps `UsbDeviceManager`, adds Sahara handshake verification to confirm EDL readiness

### Magisk-Specific Transport Diagnostics

During Magisk uninstall verification, the transport layer must meet these thresholds:

- **Boot partition read speed** must exceed 1.2MB/s (indicates healthy USB 2.0 link). Below 800KB/s → risk of incomplete boot image flashing.
- **Retry threshold**: >3 consecutive read retries on boot partition triggers USB fallback to serial transport.
- **Magisk artifact scan**: When reading boot partition, monitor for:
  - Unexpected `0xFF` padding patterns (Magisk often leaves traces in unused ramdisk space)
  - Non-standard offsets in `init.rc` (Magisk insertion points)
- **Transport health metric**: `successful_boot_reads / total_boot_reads` must exceed 0.95 for uninstall to proceed. Below this threshold, the USB link is too unstable for reliable boot image manipulation.

---

## Cross-References

- **Failure modes**: See `FAILURES.md` for 15 documented failure modes with recovery procedures
- **Design rationale**: See `DESIGN.md` for why sequential USB transfers are mandatory (Design Decision 1)
- **Build/deploy**: See `BUILD.md` for producing distributable EXE
- **MagiskCore architecture**: See `../OFFLINE_AGENT_GUIDE.md` for boot image parsing, repacking, and artifact detection
- **Health check**: See `../HEALTH_CHECK.md` for system validation commands