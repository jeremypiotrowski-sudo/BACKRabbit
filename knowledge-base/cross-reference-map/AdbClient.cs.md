# Cross-Reference Map: BACKRabbit.Protocol.Adb.AdbClient.cs ↔ Android/Magisk Sources

## Overview
| Property | Value |
|----------|-------|
| **BACKRabbit File** | `BACKRabbit.Protocol.Adb/AdbClient.cs` |
| **Android Source** | `system/core/adb/adb.h`, `system/core/adb/transport.cpp`, `system/core/adb/adb_auth.cpp` |
| **Magisk Source** | `scripts/adb.sh`, `native/src/adb/adb.rs` |
| **Total Lines (BACKRabbit)** | 783 |

---

## Version-by-Version Cross-Reference

### Android ADB Protocol (system/core/adb/)
| BACKRabbit Line(s) | Android Source | Function |
|---|---|---|
| 22-47 | `adb.h` | Protocol constants (A_SYNC, A_CNXN, A_AUTH, A_OPEN, A_OKAY, A_CLSE, A_WRTE) |
| 59-83 | `transport.cpp` | `ConnectUsbAsync()` - USB transport |
| 88-98 | `transport.cpp` | `ConnectTcpAsync()` - TCP transport |
| 103-136 | `adb.h`/`transport.cpp` | `ConnectAsync()` - CNXN handshake |
| 141-186 | `adb_auth.cpp` | `AuthenticateAsync()` - RSA auth |
| 209-236 | `adb.h` | `OpenStreamAsync()` - OPEN protocol |
| 241-266 | `adb.h` | `ExecuteShellAsync()` - shell: protocol |
| 271-274 | `adb.h` | `ExecuteShellRootAsync()` - su wrapper |
| 347-388 | `sync.cpp` | `PullFileAsync()` - sync: RECV |
| 393-440 | `sync.cpp` | `PushFileAsync()` - sync: SEND |
| 445-495 | `adb.h` | `RebootAsync()` - reboot: protocol |
| 518-611 | `adb.h` | Packet creation/parsing |
| 624-662 | `transport.cpp` | `UsbStream` - USB transport |
| 667-748 | `adb.h` | `AdbStream` - data stream |

### Magisk Sources (scripts/adb.sh)
| BACKRabbit Line(s) | Magisk Script | Notes |
|---|---|---|
| 59-83 | `scripts/adb.sh` | `adb_connect_usb()` |
| 88-98 | `scripts/adb.sh` | `adb_connect_tcp()` |
| 103-136 | `scripts/adb.sh` | `adb_handshake()` |
| 141-186 | `scripts/adb.sh` | `adb_authenticate()` |
| 241-266 | `scripts/adb.sh` | `adb_shell()` |
| 347-388 | `scripts/adb.sh` | `adb_pull()` |
| 393-440 | `scripts/adb.sh` | `adb_push()` |
| 445-495 | `scripts/adb.sh` | `adb_reboot()` |

---

## Detailed Function Mapping

### ConnectUsbAsync() - Lines 59-83
**Android Equivalent:** `usb_transport_open()` in transport.cpp

```cpp
// Android (system/core/adb/transport.cpp)
int usb_transport_open(const char* serial, int* fd) {
    // Find device by serial
    // Open USB endpoints (bulk in/out)
    // Return file descriptor
}
```

**BACKRabbit mapping:** Uses `UsbDeviceManager` to enumerate and open Samsung devices.

### ConnectAsync() - Lines 103-136
**Android Equivalent:** ADB handshake in transport.cpp

```cpp
// ADB Protocol Handshake
// 1. Host sends A_CNXN with version=0x01000000, maxdata=4096, identity="host::"
// 2. Device responds with A_CNXN (success) or A_AUTH (auth required)
// 3. If A_AUTH: perform RSA authentication
// 4. On A_CNXN: connection established
```

**BACKRabbit mapping:** Lines 105-132 direct protocol implementation.

### AuthenticateAsync() - Lines 141-186
**Android Equivalent:** `adb_auth()` in adb_auth.cpp

```cpp
// Android (system/core/adb/adb_auth.cpp)
int adb_authenticate(int fd, RSA* key) {
    // 1. Receive A_AUTH with ADB_AUTH_TOKEN (20-byte token)
    // 2. Sign token with private key → A_AUTH with ADB_AUTH_SIGNATURE
    // 3. If device accepts: A_CNXN (success)
    // 4. If new key needed: Send A_AUTH with ADB_AUTH_RSAPUBLICKEY
    // 5. Wait for user confirmation on device (30s timeout)
}
```

**BACKRabbit mapping:** Lines 145-182 with `AdbKeyManager` for RSA operations.

### ExecuteShellAsync() - Lines 241-266
**Android Equivalent:** `shell` service in adb.h

```cpp
// ADB Service Protocol
// Open stream: "shell:<command>"
// Read WRTE packets until CLSE
```

**BACKRabbit mapping:** Lines 245-262 opens "shell:command" stream, reads all WRTE packets.

### PullFileAsync() - Lines 347-388
**Android Equivalent:** `sync.cpp` RECV command

```cpp
// Sync Protocol (sync.cpp)
// 1. Open "sync:" stream
// 2. Send: "RECV<4-digit-len><path>" (e.g., "RECV0007/data/file")
// 3. Receive packets with ID (0x41415444 = "DATA") + size + data
// 4. End on ID_QUIT (0x54495551)
```

**BACKRabbit mapping:** Lines 356-379 exact sync protocol implementation.

### PushFileAsync() - Lines 393-440
**Android Equivalent:** `sync.cpp` SEND command

```cpp
// Sync Protocol (sync.cpp)
// 1. Open "sync:" stream
// 2. Send SEND header with "SEND<path>,<mode>"
// 2. Send: ID_SEND (0x444E4553) + size + command
// 3. Send data chunks: ID_DATA (0x41415444) + size + data
// 4. Send ID_DONE (0x454E4F44) + timestamp
// 5. Read response
```

**BACKRabbit mapping:** Lines 403-429 exact sync protocol implementation.

---

## ADB Protocol Constants

### Packet Types (Little-endian ASCII)
| Constant | Hex | ASCII | Description |
|---|---|---|---|
| `A_SYNC` | 0x434E5953 | "SYNC" | Sync protocol |
| `A_CNXN` | 0x4E584E43 | "CNXN" | Connect |
| `A_AUTH` | 0x48545541 | "AUTH" | Authentication |
| `A_OPEN` | 0x4E45504F | "OPEN" | Open stream |
| `A_OKAY` | 0x59414B4F | "OKAY" | OK response |
| `A_CLSE` | 0x45534C43 | "CLSE" | Close stream |
| `A_WRTE` | 0x45545257 | "WRTE" | Write data |

### Auth Types
| Constant | Value | Description |
|---|---|---|
| `ADB_AUTH_TOKEN` | 1 | Device sends token to sign |
| `ADB_AUTH_SIGNATURE` | 2 | Host sends signature |
| `ADB_AUTH_RSAPUBLICKEY` | 3 | Host sends public key |

### Sync IDs
| Constant | Hex | ASCII | Description |
|---|---|---|---|
| `ID_STAT` | 0x54415453 | "STAT" | File stat |
| `ID_SEND` | 0x444E4553 | "SEND" | Send file |
| `ID_RECV` | 0x56434552 | "RECV" | Receive file |
| `ID_LIST` | 0x5453494C | "LIST" | List directory |
| `ID_QUIT` | 0x54495551 | "QUIT" | Quit sync |
| `ID_DATA` | 0x41415444 | "DATA" | Data payload |
| `ID_DONE` | 0x454E4F44 | "DONE" | Transfer done |

---

## Data Structures

### AdbPacket
```csharp
public class AdbPacket
{
    public uint type;         // Packet type (A_CNXN, A_OPEN, etc.)
    public uint arg0;         // Argument 0 (local_id, auth_type, etc.)
    public uint arg1;         // Argument 1 (remote_id, etc.)
    public uint data_length;  // Data length
    public uint checksum;     // XOR checksum of data
    public uint magic;        // type ^ 0xFFFFFFFF
    public byte[]? data;      // Payload
}
```

### MagiskStatus
```csharp
public class MagiskStatus
{
    public bool IsInstalled { get; set; }
    public string Version { get; set; } = "";
    public bool HasDatabase { get; set; }
    public int ModuleCount { get; set; }
    public List<string> Modules { get; set; } = new();
}
```

---

## Usage Examples

### USB Connection (Samsung)
```csharp
var usb = new UsbDeviceManager();
var adb = new AdbClient();

var connected = await adb.ConnectUsbAsync(usb);
if (connected) {
    Console.WriteLine($"Connected to {adb.Serial}");
    Console.WriteLine($"Model: {adb.DeviceModel}");
    Console.WriteLine($"Android: {adb.AndroidVersion}");
}
```

### TCP Connection (WiFi ADB)
```csharp
var adb = new AdbClient();
var connected = await adb.ConnectTcpAsync("192.168.1.100", 5555);
```

### Execute Commands
```csharp
// Regular shell
var output = await adb.ExecuteShellAsync("ls /data/local/tmp");

// Root shell (requires su/Magisk)
var rootOutput = await adb.ExecuteShellRootAsync("dd if=/dev/block/by-name/boot of=/data/local/tmp/boot.img");
```

### File Transfer
```csharp
// Pull boot image from device
await adb.PullFileAsync("/dev/block/by-name/boot", "boot.img");

// Push modified image
await adb.PushFileAsync("boot_stock.img", "/data/local/tmp/boot_stock.img");
```

### Reboot Modes
```csharp
await adb.RebootAsync();              // Normal reboot
await adb.RebootBootloaderAsync();    // Fastboot mode
await adb.RebootRecoveryAsync();      // Recovery mode
await adb.RebootDownloadAsync();      // Samsung Download Mode
```

### Check Magisk Status
```csharp
var magisk = await adb.CheckMagiskStatusAsync();
if (magisk.IsInstalled) {
    Console.WriteLine($"Magisk {magisk.Version} installed");
    Console.WriteLine($"Modules: {magisk.ModuleCount}");
}
```

---

## Missing/Partial Implementations

1. **Sync LIST** - Directory listing not implemented
2. **Sync STAT** - File stat not implemented
3. **Framebuffer** - Screen capture not implemented
4. **Port Forwarding** - `adb forward` not implemented
5. **Key Generation** - Relies on AdbKeyManager (separate file)
6. **Multiple Devices** - Single device at a time
7. **APK Install** - Not implemented

---

## Test Coverage Needed

| Test Case | Status |
|---|---|
| ConnectUsbAsync_SamsungDevice_Connects | ❌ |
| ConnectTcpAsync_Localhost_Connects | ❌ |
| AuthenticateAsync_ValidKey_Succeeds | ❌ |
| ExecuteShellAsync_Ls_ReturnsOutput | ❌ |
| ExecuteShellRootAsync_SuWorks_ReturnsRoot | ❌ |
| PullFileAsync_ExistingFile_Downloads | ❌ |
| PushFileAsync_LocalFile_Uploads | ❌ |
| RebootAsync_Normal_Reboots | ❌ |
| CheckMagiskStatusAsync_Installed_Detects | ❌ |
| GetPropertiesAsync_ReturnsProps | ❌ |

---

## Source File References

**BACKRabbit Source:** `BACKRabbit.Protocol.Adb/AdbClient.cs` (783 lines)

**Android Sources:**
- `system/core/adb/adb.h` - Protocol definitions
- `system/core/adb/transport.cpp` - USB/TCP transport
- `system/core/adb/adb_auth.cpp` - Authentication
- `system/core/adb/sync.cpp` - Sync protocol

**Magisk Sources (in knowledge-base):**
- `scripts/adb.sh` - Shell implementation
- `native/src/adb/adb.rs` - Rust implementation