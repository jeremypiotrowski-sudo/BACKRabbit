# Cross-Reference Map: BACKRabbit.Protocol.Fastboot.FastbootClient.cs ↔ Android/Magisk Sources

## Overview
| Property | Value |
|----------|-------|
| **BACKRabbit File** | `BACKRabbit.Protocol.Fastboot/FastbootClient.cs` |
| **Android Source** | `system/core/fastboot/fastboot.cpp`, `system/core/fastboot/transport.cpp` |
| **Magisk Source** | `scripts/flash.sh`, `native/src/fastboot/fastboot.rs` |
| **Total Lines (BACKRabbit)** | 327 |

---

## Version-by-Version Cross-Reference

### Android Fastboot Protocol (system/core/fastboot/)
| BACKRabbit Line(s) | Android Source | Function |
|---|---|---|
| 31-57 | `fastboot.cpp` | `ConnectAsync()` - USB connect |
| 66-86 | `fastboot.cpp` | `SendCommandAsync()` - command protocol |
| 88-94 | `fastboot.cpp` | `GetVarAsync()` - getvar: protocol |
| 121-160 | `fastboot.cpp` | `FlashAsync()` - flash: + DATA chunks |
| 162-217 | `fastboot.cpp` | `FlashSparseAsync()` - sparse image flash |
| 219-224 | `fastboot.cpp` | `EraseAsync()` - erase: protocol |
| 226-231 | `fastboot.cpp` | `SetActiveAsync()` - set_active: A/B slots |
| 233-255 | `fastboot.cpp` | `RebootAsync()` variants |
| 257-269 | `fastboot.cpp` | `UnlockBootloaderAsync()` / `LockBootloaderAsync()` |
| 271-282 | `fastboot.cpp` | `BootAsync()` - boot: protocol |
| 296-301 | `fastboot.cpp` | `CreateDataHeader()` - DATAxxxx format |

### Magisk Sources (scripts/flash.sh)
| BACKRabbit Line(s) | Magisk Script | Notes |
|---|---|---|
| 31-57 | `scripts/flash.sh` | `fastboot_connect()` |
| 121-160 | `scripts/flash.sh` | `fastboot_flash()` |
| 162-217 | `scripts/flash.sh` | `fastboot_flash_sparse()` |
| 219-224 | `scripts/flash.sh` | `fastboot_erase()` |
| 226-231 | `scripts/flash.sh` | `fastboot_set_active()` |
| 257-262 | `scripts/flash.sh` | `fastboot_unlock()` |

---

## Detailed Function Mapping

### ConnectAsync() - Lines 31-57
**Android Equivalent:** `FastbootDevice::connect()` in transport.cpp

```cpp
// Android (system/core/fastboot/transport.cpp)
unique_ptr<FastbootDevice> FastbootDevice::connect() {
    // USB bulk endpoints (IN/OUT)
    // Samsung: VID 0x04E8, PID varies
    // Standard: VID 0x18D1 (Google), PID 0xD00D (fastboot)
    
    auto dev = find_device(VID, PID);
    return make_unique<FastbootDevice>(dev);
}
```

**BACKRabbit mapping:** Lines 35-51 - Uses UsbDeviceManager to find Samsung fastboot device (VID 0x04E8, mode=Fastboot)

### SendCommandAsync() - Lines 66-86
**Android Equivalent:** `FastbootDevice::send_command()` in fastboot.cpp

```cpp
// Android fastboot protocol: command\0
// Response: OKAY\0 | FAIL<reason>\0 | DATA<size>\0
int FastbootDevice::send_command(const string& cmd) {
    write(cmd + "\0");
    return read_response();
}
```

**BACKRabbit mapping:** Lines 73-85 - Exact protocol: ASCII command + null, read response, check OKAY/FAIL

### GetVarAsync() - Lines 88-94
**Android Equivalent:** `fastboot_getvar()` in fastboot.cpp

```cpp
// Protocol: "getvar:variable\0"
// Response: "OKAY:value\0"
string FastbootDevice::getvar(const string& var) {
    return send_command("getvar:" + var);
}
```

**BACKRabbit mapping:** Lines 90-93 - Exact protocol

### FlashAsync() - Lines 121-160
**Android Equivalent:** `fastboot_flash()` in fastboot.cpp

```cpp
// Protocol:
// 1. "flash:partition\0"
// 2. "DATAxxxx\0" + data (chunked, max 1MB)
// 3. Wait for OKAY/FAIL
bool FastbootDevice::flash(const string& partition, const vector<uint8_t>& data) {
    send_command("flash:" + partition);
    for (chunk : chunks) {
        send_data(chunk);
    }
    return read_response() == "OKAY";
}
```

**BACKRabbit mapping:** Lines 125-159 - Chunked at MAX_TRANSFER_SIZE (1MB), DATAxxxx hex size header

### FlashSparseAsync() - Lines 162-217
**Android Equivalent:** `flash_sparse_image()` in fastboot.cpp + SparseImage parsing

```cpp
// Uses SparseImage class to parse, then:
// For each chunk:
//   RAW: send DATA + raw data
//   FILL: expand pattern, send DATA + expanded data
//   DONT_CARE: skip blocks
bool FastbootDevice::flash_sparse(const string& partition, const SparseImage& img) {
    send_command("flash:" + partition);
    for (chunk : img.chunks) {
        switch (chunk.type) {
            case CHUNK_TYPE_RAW:
                send_data(chunk.data);
                break;
            case CHUNK_TYPE_FILL:
                expand_and_send(chunk.pattern, chunk.blocks);
                break;
            case CHUNK_TYPE_DONT_CARE:
                skip_blocks(chunk.blocks);
                break;
        }
    }
    return read_response() == "OKAY";
}
```

**BACKRabbit mapping:** Lines 166-216 - Direct port using SparseImage.Parse()

### UnlockBootloaderAsync() - Lines 257-262
**Android Equivalent:** `fastboot_flashing_unlock()` in fastboot.cpp

```cpp
// Protocol: "flashing:unlock\0"
// Requires user confirmation on device screen
// WIPES DATA!
bool FastbootDevice::unlock() {
    return send_command("flashing:unlock") == "OKAY";
}
```

**BACKRabbit mapping:** Lines 259-261 - Exact protocol with warning

---

## Fastboot Protocol Constants

### Response Codes
| Constant | Value | Description |
|---|---|---|
| `RESPONSE_OKAY` | "OKAY" | Success |
| `RESPONSE_FAIL` | "FAIL" | Failure (followed by :reason) |
| `RESPONSE_DATA` | "DATA" | Data phase (followed by hex size) |

### Standard Commands
| Command | Format | Description |
|---|---|---|
| Flash | `flash:<partition>` | Flash partition |
| Erase | `erase:<partition>` | Erase partition |
| Get Variable | `getvar:<name>` | Query variable |
| Set Active Slot | `set_active:<slot>` | A/B slot switch |
| Reboot | `reboot` | Normal reboot |
| Reboot Bootloader | `reboot-bootloader` | Back to fastboot |
| Reboot Recovery | `reboot-recovery` | Recovery mode |
| Boot | `boot` | Boot image (no flash) |
| Unlock | `flashing:unlock` | Unlock bootloader (wipes) |
| Lock | `flashing:lock` | Lock bootloader |

### Standard Variables
| Variable | Description |
|---|---|
| `product` | Product name (e.g., "q5q" for S24) |
| `serialno` | Serial number |
| `version` | Fastboot version |
| `version-bootloader` | Bootloader version |
| `current-slot` | Current A/B slot (_a/_b) |
| `slot-suffixes` | Available slots (_a,_b) |
| `partition-type:<name>` | Partition filesystem |
| `has-slot:<name>` | Whether partition is A/B |
| `unlocked` | Bootloader unlocked (yes/no) |
| `off-mode-charge` | Off-mode charging support |

---

## Data Structures

### FastbootProgressEventArgs
```csharp
public class FastbootProgressEventArgs : EventArgs
{
    public string Partition { get; set; } = "";
    public int BytesSent { get; set; }
    public int TotalBytes { get; set; }
    public int Percentage { get; set; }
}
```

### FastbootException
```csharp
public class FastbootException : Exception
{
    public FastbootException(string message) : base(message) { }
}
```

---

## Usage Examples

### Connect to Samsung Fastboot
```csharp
var usb = new UsbDeviceManager();
var fastboot = new FastbootClient();

var connected = await fastboot.ConnectAsync(usb);
if (connected) {
    Console.WriteLine($"Serial: {fastboot.Serial}");
    Console.WriteLine($"Product: {fastboot.Product}");
    Console.WriteLine($"Slot: {fastboot.CurrentSlot}");
}
```

### Flash Stock Partitions (Samsung S24/S25)
```csharp
// Extract from firmware first
var extractor = new SamsungFirmwareExtractor();
var ap = extractor.ExtractTarMd5("AP_*.tar.md5");

// Flash critical partitions
await fastboot.FlashAsync("boot", ap.GetPartition("boot")!);
await fastboot.FlashAsync("init_boot", ap.GetPartition("init_boot")!);
await fastboot.FlashAsync("vbmeta", ap.GetPartition("vbmeta")!);
await fastboot.FlashAsync("vbmeta_system", ap.GetPartition("vbmeta_system")!);
await fastboot.FlashAsync("vbmeta_vendor", ap.GetPartition("vbmeta_vendor")!);
await fastboot.FlashAsync("dtbo", ap.GetPartition("dtbo")!);

// Flash sparse super.img
var super = ap.GetPartition("super");
if (super != null) {
    await fastboot.FlashSparseAsync("super", super);
}
```

### A/B Slot Management
```csharp
// Check slots
var vars = await fastboot.GetAllVarsAsync();
Console.WriteLine($"Current: {vars["current-slot"]}");
Console.WriteLine($"Available: {vars["slot-suffixes"]}");

// Switch slot
await fastboot.SetActiveAsync("_b");
```

### Boot Image (await fastboot.RebootAsync());

---

## Samsung-Specific Notes

### Samsung Fastboot VID/PID
- **VID**: 0x04E8 (Samsung)
- **PID**: Varies by device (0x6860, 0x6862, etc.)
- **Mode**: Fastboot (not Download Mode)

### Samsung Partition Layout (GKI 2.0 - S24/S25)
| Partition | Type | Notes |
|---|---|---|
| `boot` | v4 header | Kernel only |
| `init_boot` | vendor v3/v4 | Ramdisk only |
| `vbmeta` | AVB | Boot vbmeta |
| `vbmeta_system` | AVB | System vbmeta |
| `vbmeta_vendor` | AVB | Vendor vbmeta |
| `dtbo` | DTB overlay | Hardware config |
| `super` | Sparse | Dynamic partitions |

### Samsung Flash Order
```bash
fastboot flash boot boot.img
fastboot flash init_boot init_boot.img
fastboot flash vbmeta vbmeta.img
fastboot flash vbmeta_system vbmeta_system.img
fastboot flash vbmeta_vendor vbmeta_vendor.img
fastboot flash dtbo dtbo.img
fastboot flash super super.img  # sparse
fastboot reboot
```

---

## Missing/Partial Implementations

1. **OEM Commands** - `oem <cmd>` not implemented
2. **Partition Resize** - `resize:<partition> <size>` not implemented
3. **Logical Partition Flash** - Individual dynamic partitions not supported
4. **AVB Key Operations** - `avb_custom_key` not implemented
5. **Slot Copy** - `update` command not implemented
6. **GetVar All** - Some variables may fail silently (try/catch empty)

---

## Test Coverage Needed

| Test Case | Status |
|---|---|
| ConnectAsync_SamsungFastboot_Connects | ❌ |
| SendCommandAsync_ValidCommand_ReturnsOKAY | ❌ |
| GetVarAsync_Product_ReturnsProduct | ❌ |
| FlashAsync_SmallImage_Flashes | ❌ |
| FlashSparseAsync_SuperImg_Flashes | ❌ |
| EraseAsync_Userdata_Erases | ❌ |
| SetActiveAsync_SlotA_Switches | ❌ |
| RebootAsync_Normal_Reboots | ❌ |
| UnlockBootloaderAsync_Confirmed_Unlocks | ❌ |
| BootAsync_BootImage_Boots | ❌ |

---

## Source File References

**BACKRabbit Source:** `BACKRabbit.Protocol.Fastboot/FastbootClient.cs` (327 lines)

**Android Sources:**
- `system/core/fastboot/fastboot.cpp` - Main protocol
- `system/core/fastboot/transport.cpp` - USB transport
- `system/core/fastboot/sparse.cpp` - Sparse image (referenced)

**Magisk Sources (in knowledge-base):**
- `scripts/flash.sh` - Shell implementation
- `native/src/fastboot/fastboot.rs` - Rust implementation