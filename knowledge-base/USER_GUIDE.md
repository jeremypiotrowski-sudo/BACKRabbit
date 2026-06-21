# 🐰 BACKRabbit v2.0 — User Guide

## What BACKRabbit Does

BACKRabbit removes Magisk root from Samsung Android devices. It parses your phone's boot image, finds Magisk modifications, removes them, restores security verification, and produces a clean boot image you can flash back to your device.

**v2.0 is CLI-only.** The Windows Forms GUI has been deleted. Everything runs from the terminal. This means BACKRabbit works on Windows, Linux, and macOS.

---

## Quick Start

### 1. Build

```bash
dotnet restore BACKRabbit.slnx
dotnet build BACKRabbit.slnx -c Release
```

### 2. Test (No Phone Required)

```bash
dotnet run --project BACKRabbit.CLI -- magisk wizard --test-mode
```

This runs the full 7-step uninstall wizard against a simulated Samsung Galaxy S24 Ultra. No USB cable, no ADB setup, no phone needed. Every step executes real code — the only difference is ADB responses come from `MockAdbClient` instead of a physical device.

### 3. Run Against a Real Phone

```bash
# USB connection (default)
dotnet run --project BACKRabbit.CLI -- magisk wizard

# TCP connection (Wireless Debugging / Tailscale)
dotnet run --project BACKRabbit.CLI -- magisk wizard --serial 100.87.65.32:5555
```

### 4. Analyze a Local Boot Image (Offline)

```bash
dotnet run --project BACKRabbit.CLI -- magisk wizard --offline staging/F966U1/boot.img
```

This parses, analyzes, and cleans a boot image file without connecting to any device. The cleaned image is saved to `Documents\BACKRabbit\Backups\`.

---

## The 7-Step Wizard

| Step | Name | What Happens |
|------|------|-------------|
| 1 | **Detect** | Checks device for Magisk, Knox warranty bit, bootloader lock state |
| 2 | **Backup** | Pulls boot.img and init_boot.img from device to your PC |
| 3 | **Analyze** | Parses boot image, decompresses ramdisk, finds Magisk artifacts, checks AVB/SELinux |
| 4 | **Clean** | Removes Magisk files from ramdisk, restores stock init, repacks boot image |
| 5 | **Flash** | Writes cleaned image to device (or gives Odin instructions if bootloader locked) |
| 6 | **Verify** | Re-checks device to confirm Magisk is gone |
| 7 | **Reboot** | Restarts device with clean boot image |

### On Any Step Failure

The wizard offers three choices:
- **Retry this step** — runs the same step again
- **Skip this step** — continues with a warning (device may be in incomplete state)
- **Abort wizard** — stops and writes a report file to `Documents\BACKRabbit\Reports\`

A report is always written, even if you abort. You can resume later or use the report to debug.

---

## Testing Without a Physical Device

BACKRabbit v2.0 has three modes for testing without a phone:

### Mode 1: Test Mode (`--test-mode`)

```bash
dotnet run --project BACKRabbit.CLI -- magisk wizard --test-mode
```

Uses `MockAdbClient` which simulates a Galaxy S24 Ultra (SM-S928U1):
- Magisk v28.1 installed, 3 modules
- Bootloader unlocked
- Knox 0x0 (not tripped)
- Creates a 32MB synthetic boot image with valid ANDROID! magic bytes
- All 7 wizard steps execute real code against simulated responses

**What this tests:** The entire pipeline — parsing, artifact detection, uninstall logic, repacking, AVB restoration. Everything except actual USB communication.

### Mode 2: Offline Mode (`--offline <path>`)

```bash
dotnet run --project BACKRabbit.CLI -- magisk wizard --offline staging/F966U1/boot.img
```

Analyzes a local boot image file. No device connection at all. Steps 1, 2, 5, 6, 7 are skipped or informational. Steps 3 and 4 run fully against the local file. The cleaned image is saved locally.

**What this tests:** Boot image parsing, ramdisk decompression, Magisk artifact detection, uninstall, repacking, AVB restoration — all against real firmware.

### Mode 3: Unit Tests

```bash
dotnet test BACKRabbit.slnx -c Release
```

42 automated tests covering:
- Boot image parse→repack→re-parse round-trips (v0-v4, vendor v3/v4, Samsung PXA)
- CPIO archive parse/serialize fidelity
- Compression round-trips (Gzip, LZ4, LZ4 Legacy, XZ, LZMA, Bzip2)
- Magisk artifact detection (patched, clean, backup scenarios)
- AVB flag restoration (flags 3→0, flags 0→0, no footer)
- Full uninstall workflow on synthetic images
- Real Samsung Z Fold 6 firmware integration tests

---

## How ADB Commands Work (And How to Test Without ADB)

BACKRabbit uses an **interface abstraction** (`IAdbClient`) so every ADB operation can be swapped between real and simulated:

```
Production:  CLI → IAdbClient → AdbClient → USB/TCP → Phone
Test Mode:   CLI → IAdbClient → MockAdbClient → Simulated responses
Offline:     CLI → IAdbClient → MockAdbClient → Local file analysis
```

### Real ADB (Production)

`AdbClient.cs` (783 lines) implements the full ADB protocol:
- USB and TCP transport
- RSA key authentication (A_AUTH)
- Shell commands (A_OPEN shell)
- File push/pull (A_SYNC)
- Reboot commands

Requirements for real ADB:
1. USB Debugging enabled on phone (Settings → Developer Options)
2. Phone authorized for your PC (accept RSA key prompt on phone screen)
3. USB data cable (not charge-only cable)

### Simulated ADB (Test Mode)

`MockAdbClient.cs` (218 lines) implements the same `IAdbClient` interface with canned responses:

| Method | Simulated Response |
|--------|-------------------|
| `CheckMagiskStatusAsync()` | Magisk v28.1, 3 modules |
| `ExecuteShellAsync("ls /sdcard")` | "Download\nDCIM\nPictures\nMusic\nboot.img" |
| `PullFileAsync("/dev/block/by-name/boot", ...)` | Creates 32MB mock boot image with ANDROID! magic |
| `PushFileAsync(...)` | Returns success |
| `GetPropertiesAsync()` | SM-S928U1, Android 15, ro.boot.warranty_bit=0 |
| `CheckBootloaderLockStatusAsync()` | Unlocked (configurable) |
| `RebootAsync()` | Simulates disconnect |

### Configuring MockAdbClient for Different Scenarios

```csharp
var mock = new MockAdbClient();
mock.SetMockMagiskInstalled(false);     // Simulate clean device
mock.SetBootloaderLocked(true);         // Simulate locked bootloader → Odin path
mock.SetKnoxTripped(true);              // Simulate Knox 0x1 → shows CAN/CANNOT lists
mock.SimulateDisconnect();              // Test error recovery in Step 6
```

### Remote ADB via Wireless Debugging (Android 11+)

No USB cable needed:
1. Settings → Developer Options → Wireless debugging → ON
2. Tap "Pair device with pairing code" → get IP:port + 6-digit code
3. On PC: `adb pair <ip>:<pairing-port> <code>`
4. On PC: `adb connect <ip>:<connect-port>`
5. Use with BACKRabbit: `backrabbit magisk wizard --serial <ip>:<connect-port>`

This works over Tailscale, ZeroTier, or any VPN — you can run BACKRabbit on a remote machine and connect to a phone anywhere.

---

## Workarounds When Things Don't Work Automatically

### 1. ADB Won't Connect

**Symptom:** "No device connected" in Step 1

**Workarounds (try in order):**
1. **Check USB cable** — must be a data cable, not charge-only. Try a different cable.
2. **Check USB debugging authorization** — look at phone screen for RSA key prompt. Revoke old authorizations in Developer Options and reconnect.
3. **Use TCP/IP instead of USB:**
   - Plug in USB once: `adb tcpip 5555`
   - Unplug USB, then: `backrabbit magisk wizard --serial 192.168.1.100:5555`
4. **Use Wireless Debugging** (Android 11+) — no USB cable ever needed (see above)
5. **Use Test Mode** to verify the tool works: `backrabbit magisk wizard --test-mode`
6. **Use Offline Mode** with a manually pulled boot image:
   ```bash
   adb pull /dev/block/by-name/boot boot.img
   backrabbit magisk wizard --offline boot.img
   ```

### 2. Fastboot Not Detected / Doesn't Work

**Symptom:** Fastboot commands hang or fail

**Workarounds:**
1. Most Samsung devices use **Download Mode (Odin)**, not Fastboot. Use the Download Mode path.
2. US carrier variants (T-Mobile, Verizon, AT&T) often have Fastboot disabled entirely.
3. Reboot to bootloader manually: `adb reboot bootloader`
4. If bootloader is locked, you cannot use Fastboot at all — use Odin (see below).

### 3. Magisk Detection Fails or Returns Wrong Status

**Symptom:** Step 1 says "No modifications detected" but you know Magisk is installed

**Workarounds:**
1. **Check root access:** Magisk may be installed but ADB shell isn't running as root. Try `adb root` first.
2. **Analyze boot image offline:**
   ```bash
   adb pull /dev/block/by-name/boot boot.img
   backrabbit magisk wizard --offline boot.img
   ```
   This bypasses ADB detection entirely and analyzes the raw boot image.
3. **Check init_boot.img** (Android 13+ GKI devices):
   ```bash
   adb pull /dev/block/by-name/init_boot init_boot.img
   backrabbit magisk wizard --offline init_boot.img
   ```
   On v4 GKI devices, Magisk lives in init_boot.img, not boot.img.

### 4. Magisk Uninstall Requires Stock Firmware

**Symptom:** Step 4 says "Stock firmware required" or "No backup found"

**Workarounds:**
1. **Use Magisk's own backup:** If `ramdisk.cpio.orig` exists in your boot image, the uninstaller restores from it automatically. This is the preferred method.
2. **Extract from stock firmware:**
   ```bash
   backrabbit firmware extract SAMFW.COM_SM-S928U1_TMB_S928U1UES6DZE2_fac.zip
   ```
   This extracts boot.img and init_boot.img from Samsung's official firmware package.
3. **Download stock firmware:** From samfw.com, sammobile.com, or frija tool. Search for your model number (e.g., SM-S928U1).
4. **Force surgical removal:** If no backup exists, the uninstaller removes Magisk files manually. This works but may leave residual config files in `/data/adb/`.

### 5. Flashing Fails (Bootloader Locked)

**Symptom:** Step 5 says "Bootloader is LOCKED — cannot flash directly"

**This is expected on most US Samsung devices.** The wizard will display a 10-step Odin guide:

1. Power off your phone completely
2. Hold Volume Up + Volume Down simultaneously
3. While holding both buttons, plug USB cable into your PC
4. Screen should show: "Downloading... Do not turn off target"
5. Download Odin3 from odindownload.com
6. Open Odin3 — you should see "Added!" in the log
7. Click the **AP** button and select your cleaned image
8. **IMPORTANT:** Make sure ONLY "AP" is checked. Uncheck BL, CP, CSC.
9. Click **Start**. Wait 2-3 minutes.
10. Phone will reboot automatically with clean system.

**If Odin fails:** Try a different USB cable (data cable, not charge-only). Try a USB 2.0 port (not USB 3.0). Try a different PC.

### 6. Device Boot-Loops After Flash

**Symptom:** Phone won't boot past Samsung logo

**DO NOT PANIC.** Samsung devices have a hardware Download Mode that cannot be overwritten:

1. Hold **Volume Down + Power** for 10 seconds to force reboot
2. Immediately hold **Volume Up + Volume Down** and plug in USB cable
3. You're back in Download Mode — flash stock firmware with Odin

**Emergency recovery script:**
```bash
python emergency-flasher/emergency_fix.py --device <model> --boot <stock_boot.img>
```

This is a pure Python 3 script (no pip installs needed) that can flash a stock boot image via USB.

### 7. Knox Warranty Bit Shows 0x1

**Symptom:** Step 1 shows "⚠️ Knox Warranty Bit: 0x1 (permanently tripped)"

**This cannot be reversed. Period.** Knox is a physical eFuse inside the Samsung CPU. Once blown, it stays blown forever. No software tool, no firmware flash, no factory reset can undo it.

**What you CAN still do:**
- Use all standard Android features (calls, camera, messages, internet)
- Use Samsung apps that don't require Knox (camera, gallery, settings)
- Receive OTA updates (if bootloader is locked)
- Use Google Pay (uses different security system)

**What you CANNOT do:**
- Samsung Pay
- Samsung Secure Folder
- Samsung Pass
- Samsung Health (some features)
- Warranty service (Samsung may refuse)

BACKRabbit never claims it can fix Knox. It tells you the truth, shows you what's still possible, and never gives false hope.

### 8. Dim Screen After Magisk Removal

**Symptom:** Screen brightness is lower than expected after cleaning

**Cause:** Magisk often sets SELinux to permissive mode, which can affect brightness control. After removal, SELinux returns to enforcing mode.

**Fix:** This is normal. Go to Settings → Display → Brightness and adjust. If brightness still seems low, check that Adaptive Brightness is enabled.

### 9. Root Apps Stop Working

**Symptom:** Apps that required root no longer function

**This is expected.** Removing Magisk removes root access. Apps that depend on root (Titanium Backup, AdAway, etc.) will stop working. This is the intended outcome of uninstalling Magisk.

---

## CLI Command Reference

### Wizard (7-Step Interactive)

```bash
backrabbit magisk wizard                        # USB connection
backrabbit magisk wizard --test-mode            # Simulated device
backrabbit magisk wizard --offline <path>       # Local boot image
backrabbit magisk wizard --serial <ip:port>     # TCP connection
backrabbit magisk wizard --verbose              # Show raw ADB commands
```

### Magisk Operations

```bash
backrabbit magisk detect                        # Quick Magisk check
backrabbit magisk detect --serial 192.168.1.100:5555
backrabbit magisk uninstall                     # Direct uninstall (non-interactive)
backrabbit magisk uninstall --backup --reboot
backrabbit magisk uninstall --no-backup --no-reboot
```

### ADB Operations

```bash
backrabbit adb shell "ls /sdcard"
backrabbit adb pull /sdcard/boot.img C:\backup\boot.img
backrabbit adb push C:\cleaned.img /sdcard/cleaned.img
```

### Fastboot Operations

```bash
backrabbit fastboot flash boot C:\cleaned_boot.img
backrabbit fastboot reboot --mode recovery
```

### Firmware Operations

```bash
backrabbit firmware extract SAMFW.COM_SM-S928U1_TMB_S928U1UES6DZE2_fac.zip
backrabbit firmware extract firmware.tar.md5 --output C:\extracted
```

### Device Detection

```bash
backrabbit detect
backrabbit detect --verbose
```

---

## File Locations

| What | Where |
|------|-------|
| Backups | `Documents\BACKRabbit\Backups\YYYYMMDD_HHmmss\` |
| Cleaned images | `Documents\BACKRabbit\Backups\YYYYMMDD_HHmmss\cleaned_boot.img` |
| Reports | `Documents\BACKRabbit\Reports\YYYYMMDD_HHmmss_report.txt` |
| Mock boot images | Created in-memory or at pull destination |
| Extracted firmware | `.\extracted\` or `--output` directory |
| ADB keys | `%USERPROFILE%\.android\adbkey` (standard Android location) |

---

## Troubleshooting Quick Reference

| Problem | First Check | Second Check | Fallback |
|---------|-------------|--------------|----------|
| No device detected | USB cable (data, not charge-only) | USB driver installed | Use Test Mode |
| ADB unauthorized | Check phone screen for RSA prompt | Revoke USB debugging auth | Use TCP/IP or Wireless Debugging |
| Fastboot waiting | Correct mode (bootloader, not ADB) | Unlocked bootloader | Use Download Mode (Odin) |
| Flash rejected | AVB/verified boot enabled | vbmeta needs patching | Flash from Odin |
| Magisk not detected | Root access available? | Check /data/adb/magisk | Analyze boot.img offline |
| Uninstall fails | Stock backup exists? | ramdisk.cpio.orig present? | Extract from stock firmware |
| Boot-loop after flash | Force reboot (Vol Down + Power) | Enter Download Mode | Flash stock firmware with Odin |

---

## Supported Boot Image Formats

| Format | Support Level | Test Status | Devices Affected |
|--------|--------------|-------------|------------------|
| **AOSP v0** | ✅ Full | Round-trip verified | Legacy devices (pre-2015) |
| **AOSP v1** | ✅ Full | Round-trip verified | Android 6-8 devices |
| **AOSP v2** | ✅ Full | Round-trip verified | Android 9-11 devices |
| **AOSP v3** | ✅ Full | Round-trip verified | Android 12+ devices |
| **AOSP v4** | ✅ Full | Round-trip verified | Android 13+ devices (S23, S24, S25) |
| **Vendor Boot v3** | ✅ Full | Round-trip verified | Most Samsung S21-S25 |
| **Vendor Boot v4** | ✅ Full | Round-trip verified | Most Samsung S21-S25 |
| **Samsung PXA** | ✅ Full | Round-trip verified | Older Samsung PXA1088/PXA1908 |
| **Samsung boot.img** | ✅ Full | Real firmware verified | S21, S22, S23, S24, S25 |
| **Samsung init_boot.img** | ✅ Full | Real firmware verified | S23, S24, S25 (AOSP v4) |
| **Samsung vendor_boot.img** | ⚠️ Limited | F966U1 proprietary LZ4 variant | Z Fold 6, some flagship variants |
| **MediaTek DHTB** | ⚠️ Limited | 23-byte padding drift in repacker | MediaTek devices (rare) |
| **ChromeOS boot stub** | ❌ Unsupported | Parser does not detect | ChromeOS/Chromebook devices |

---

## Architecture: No Crutches, Pure C#

BACKRabbit v2.0 uses **zero external executables**. All compression, parsing, and protocol handling is pure C#:

| Operation | v1.0 Approach | v2.0 Approach |
|-----------|--------------|---------------|
| Gzip/Bzip2/XZ/LZMA | SharpCompress (NuGet) | SharpCompress (NuGet) — same, pure C# |
| LZ4/LZ4 Legacy | K4os.Compression.LZ4 (NuGet) | K4os.Compression.LZ4 (NuGet) — same, pure C# |
| 7z extraction | SevenZipSharp | ❌ Removed — not needed |
| GUI dialogs | VB.NET InputBox | ❌ Removed — Spectre.Console prompts |
| CLI parsing | System.CommandLine | System.CommandLine — same |
| Terminal output | Plain text | Spectre.Console (tables, progress bars, colors) |
| ADB protocol | AdbClient.cs (pure C#) | AdbClient.cs — same |
| GUI framework | Windows Forms | ❌ Deleted — CLI wizard replaces it |

**No lz4.exe, no xz.exe, no 7z.exe, no VB.NET, no Windows Forms. Everything is pure C# running on .NET 8.**

---

## Building From Source

```bash
git clone <repo>
cd BACKRabbit
dotnet restore BACKRabbit.slnx
dotnet build BACKRabbit.slnx -c Release
dotnet test BACKRabbit.slnx -c Release
```

### Publishing Single-File Executables

```bash
# Windows
dotnet publish BACKRabbit.CLI/BACKRabbit.CLI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist/win

# Linux
dotnet publish BACKRabbit.CLI/BACKRabbit.CLI.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o dist/linux

# macOS
dotnet publish BACKRabbit.CLI/BACKRabbit.CLI.csproj -c Release -r osx-x64 --self-contained true -p:PublishSingleFile=true -o dist/mac
```

Output: Single executable file — no DLLs, no framework install needed.

---

## Emergency Recovery

If your device boot-loops after flashing:

1. **Force reboot:** Hold Volume Down + Power for 10 seconds
2. **Enter Download Mode:** Hold Volume Up + Volume Down, plug in USB cable
3. **Flash stock firmware with Odin** (see 10-step guide in Workaround #5)
4. **Or use the emergency script:**
   ```bash
   python emergency-flasher/emergency_fix.py --device <model> --boot <stock_boot.img>
   ```

Stock firmware files for testing are in `staging/`:
- `staging/F966U1/` — Galaxy Z Fold 6
- `staging/S928U1/` — Galaxy S24 Ultra
- `staging/S936U1/` — Galaxy S25 Ultra

---

*Last updated: June 18, 2026 — v2.0 Ship*
*CLI-only, cross-platform, zero crutches, 42/50 tests passing (8 documented limitations)*