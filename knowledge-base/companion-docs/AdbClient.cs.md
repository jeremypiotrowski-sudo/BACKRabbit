# Companion Documentation: BACKRabbit.Protocol.Adb.AdbClient.cs

## Purpose
Complete Android Debug Bridge (ADB) client implementation supporting USB and TCP transports, shell commands, file sync (pull/push), RSA authentication, and Magisk status detection.

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                          AdbClient                                  │
├─────────────────────────────────────────────────────────────────────┤
│  ConnectUsbAsync() ──→ USB transport                               │
│  ConnectTcpAsync() ──→ WiFi ADB (port 5555)                        │
│       │                                                             │
│       └─→ ConnectAsync() ──→ CNXN handshake                         │
│            │                                                         │
│            ├─→ CNXN response ──→ Connected                         │
│            └─→ AUTH_TOKEN ──→ AuthenticateAsync()                  │
│                 │                                                   │
│                 ├─→ Sign token with private key                    │
│                 ├─→ Send AUTH_SIGNATURE                            │
│                 ├─→ CNXN ──→ Success                               │
│                 └─→ AUTH_TOKEN again ──→ Send RSAPUBLICKEY         │
│                      └─→ User accepts ──→ CNXN ──→ Success         │
│                                                                     │
│  OpenStreamAsync(destination) ──→ AdbStream                        │
│       │                                                             │
│       ├─→ "shell:cmd" → ExecuteShellAsync()                        │
│       ├─→ "shell:su -c cmd" → ExecuteShellRootAsync()             │
│       ├─→ "sync:" → PullFileAsync() / PushFileAsync()              │
│       └─→ "reboot:mode" → RebootAsync()                            │
└─────────────────────────────────────────────────────────────────────┘
```

## Public API

### Connection
```csharp
/// <summary>
/// Connect via USB using UsbDeviceManager
/// </summary>
public async Task<bool> ConnectUsbAsync(UsbDeviceManager usb, CancellationToken ct = default)

/// <summary>
/// Connect via TCP (WiFi ADB on port 5555)
/// </summary>
public async Task<bool> ConnectTcpAsync(string host, int port = 5555, CancellationToken ct = default)

/// <summary>
/// Close ADB connection
/// </summary>
public void Disconnect()
```

### Properties
```csharp
public bool IsConnected { get; }
public string? Serial { get; }
public string? DeviceModel { get; }
public string? AndroidVersion { get; }
```

### Events
```csharp
public event EventHandler<string>? LogMessage;
public event EventHandler? DeviceStateChanged;
```

### Shell Commands
```csharp
/// <summary>
/// Execute shell command and return output
/// </summary>
public async Task<string> ExecuteShellAsync(string command, CancellationToken ct = default)

/// <summary>
/// Execute shell command with root (su)
/// </summary>
public async Task<string> ExecuteShellRootAsync(string command, CancellationToken ct = default)

/// <summary>
/// Check if device has root access
/// </summary>
public async Task<bool> HasRootAsync(CancellationToken ct = default)

/// <summary>
/// Check if Magisk is installed
/// </summary>
public async Task<MagiskStatus> CheckMagiskStatusAsync(CancellationToken ct = default)

/// <summary>
/// Get all device properties (getprop)
/// </summary>
public async Task<Dictionary<string, string>> GetPropertiesAsync(CancellationToken ct = default)
```

### File Operations
```csharp
/// <summary>
/// Pull file from device
/// </summary>
public async Task<bool> PullFileAsync(string remotePath, string localPath, CancellationToken ct = default)

/// <summary>
/// Push file to device
/// </summary>
public async Task<bool> PushFileAsync(string localPath, string remotePath, CancellationToken ct = default)
```

### Device Control
```csharp
/// <summary>
/// Reboot device (normal, bootloader, recovery, edl, sideload)
/// </summary>
public async Task<bool> RebootAsync(string mode = "", CancellationToken ct = default)

/// <summary>
/// Reboot to bootloader
/// </summary>
public async Task<bool> RebootBootloaderAsync(CancellationToken ct = default)

/// <summary>
/// Reboot to recovery
/// </summary>
public async Task<bool> RebootRecoveryAsync(CancellationToken ct = default)

/// <summary>
/// Reboot to Download Mode (Samsung)
/// </summary>
public async Task<bool> RebootDownloadAsync(CancellationToken ct = default)
```

## ADB Protocol Implementation

### Packet Structure (24-byte header + data)
```csharp
public class AdbPacket
{
    public uint type;          // Command (CNXN, AUTH, OPEN, OKAY, CLSE, WRTE)
    public uint arg0;          // Argument 0
    public uint arg1;          // Argument 1
    public uint data_length;   // Data payload length
    public uint checksum;      // XOR checksum of data
    public uint magic;         // type ^ 0xFFFFFFFF
    public byte[]? data;       // Payload
}
```

### Packet Types (ASCII in little-endian)
| Type | Hex | ASCII | Direction | Purpose |
|------|-----|-------|-----------|---------|
| A_SYNC | 0x434E5953 | "SYNC" | Both | Sync protocol |
| A_CNXN | 0x4E584E43 | "CNXN" | Both | Connect |
| A_AUTH | 0x48545541 | "AUTH" | Both | Authentication |
| A_OPEN | 0x4E45504F | "OPEN" | Host→Dev | Open stream |
| A_OKAY | 0x59414B4F | "OKAY" | Dev→Host | Stream OK |
| A_CLSE | 0x45534C43 | "CLSE" | Both | Close stream |
| A_WRTE | 0x45545257 | "WRTE" | Both | Write data |

### Connection Handshake
```csharp
// 1. Host sends CNXN
var cnxn = CreatePacket(A_CNXN, A_VERSION, A_MAXDATA, "host::");
//    type=CNXN, arg0=0x01000000, arg1=4096, data="host::"

// 2. Device responds:
//    - CNXN (authorized): type=CNXN, data="device::"
//    - AUTH (needs auth): type=AUTH, arg0=1 (TOKEN), data=20 random bytes
```

### RSA Authentication Flow
```csharp
// 1. Device sends AUTH_TOKEN (20 random bytes)
// 2. Host signs with RSA private key (PKCS#1 v1.5, SHA256)
// 3. Host sends AUTH_SIGNATURE with 256-byte signature
// 4. Device verifies:
//    a) If known key: responds CNXN → Success
//    b) If unknown key: responds AUTH_TOKEN again
//       i) Host sends AUTH_RSAPUBLICKEY (key name + base64 DER key)
//       ii) User taps "Allow" on device
//       iii) Device responds CNXN → Success
```

### Stream Protocol (OPEN/OKAY/WRTE/CLSE)
```csharp
// Open stream:
Host: OPEN(local_id, 0, "destination")
Device: OKAY(local_id, remote_id)

// Data transfer:
Host: WRTE(local_id, remote_id, data)
Device: OKAY(remote_id, local_id)

// Close:
Either: CLSE(id, other_id)
```

### Sync Protocol (Pull/Push)
```csharp
// Pull (RECV):
Host: RECV<4-byte len><path>
Device: DATA<4-byte size><data>... (repeated)
Device: DONE<timestamp>

// Push (SEND):
Host: SEND<path>,<mode>
Device: OKAY
Host: DATA<4-byte size><data>... (repeated)
Host: DONE<timestamp>
Device: OKAY
```

## Data Structures

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

### AdbException
```csharp
public class AdbException : Exception { ... }
```

## Usage Examples

### USB Connection
```csharp
var usb = new UsbDeviceManager();
var adb = new AdbClient();

adb.LogMessage += (s, msg) => Console.WriteLine(msg);

if (await adb.ConnectUsbAsync(usb)) {
    Console.WriteLine($"Connected: {adb.Serial}");
    Console.WriteLine($"Device: {adb.DeviceModel} (Android {adb.AndroidVersion})");
}
```

### TCP Connection (WiFi ADB)
```csharp
var adb = new AdbClient();
if (await adb.ConnectTcpAsync("192.168.1.100", 5555)) {
    Console.WriteLine("Connected via WiFi");
}
```

### Shell Commands
```csharp
// Regular shell
var output = await adb.ExecuteShellAsync("ls -la /data/local/tmp");
Console.WriteLine(output);

// Root shell (requires root/Magisk)
var rootOutput = await adb.ExecuteShellRootAsync("cat /proc/version");
Console.WriteLine(rootOutput);

// Check root
if (await adb.HasRootAsync()) {
    Console.WriteLine("Device has root");
}
```

### Magisk Detection
```csharp
var status = await adb.CheckMagiskStatusAsync();
if (status.IsInstalled) {
    Console.WriteLine($"Magisk {status.Version} installed");
    Console.WriteLine($"Modules: {status.ModuleCount}");
    Console.WriteLine($"Database: {status.HasDatabase}");
}
```

### File Transfer
```csharp
// Pull file from device
await adb.PullFileAsync("/data/adb/magisk.db", "magisk.db");

// Push file to device
await adb.PushFileAsync("boot_patched.img", "/data/local/tmp/boot_patched.img");
```

### Reboot Control
```csharp
// Normal reboot
await adb.RebootAsync();

// Reboot to bootloader (fastboot)
await adb.RebootBootloaderAsync();

// Reboot to recovery
await adb.RebootRecoveryAsync();

// Samsung Download Mode
await adb.RebootDownloadAsync();
```

### Device Properties
```csharp
var props = await adb.GetPropertiesAsync();
foreach (var (key, value) in props) {
    if (key.Contains("version") || key.Contains("model") || key.Contains("product")) {
        Console.WriteLine($"[{key}]: {value}");
    }
}
```

## Integration with BACKRabbit

### Magisk Uninstall via ADB
```csharp
var adb = new AdbClient();
await adb.ConnectUsbAsync(usb);

// 1. Check Magisk status
var status = await adb.CheckMagiskStatusAsync();

// 2. Pull current boot image
await adb.PullFileAsync("/dev/block/by-name/boot", "boot.img");

// 3. Process with MagiskUninstaller
var uninstaller = new MagiskUninstaller();
var result = await uninstaller.UninstallAsync(new UninstallOptions
{
    BootImagePath = "boot.img",
    OutputPath = "boot_stock.img"
});

// 4. Push cleaned image
await adb.PushFileAsync("boot_stock.img", "/data/local/tmp/boot_stock.img");

// 5. Flash via root shell
await adb.ExecuteShellRootAsync("dd if=/data/local/tmp/boot_stock.img of=/dev/block/by-name/boot");

// 6. Reboot
await adb.RebootAsync();
```

### Samsung Firmware Flash via ADB
```csharp
// Reboot to download mode for Odin
await adb.RebootDownloadAsync();

// Or use fastboot (after RebootBootloaderAsync)
var fastboot = new FastbootClient();
await fastboot.ConnectUsbAsync(usb);
await fastboot.FlashAsync("boot", "boot_stock.img");
await fastboot.FlashAsync("init_boot", "init_boot_stock.img");
await fastboot.RebootAsync();
```

## Limitations & Known Gaps

1. **Sync LIST/STAT** - Directory listing and file stat not implemented
2. **JDWP** - Java debug wire protocol not supported
3. **Framebuffer** - Screen capture (`framebuffer:`) not implemented
4. **Reverse Port Forward** - `reverse:local:remote` not implemented
5. **MDNS Discovery** - Device discovery via mDNS not implemented
6. **Wireless Pairing** - Android 11+ pairing protocol (adb pair) not implemented
7. **Multiple Devices** - No device selection when multiple connected
8. **Encryption** - No transport encryption (ADB over TLS)

## Related Files

| File | Relationship |
|------|--------------|
| `UsbDeviceManager.cs` | USB transport backend |
| `AdbKeyManager.cs` | RSA key management |
| `MagiskUninstaller.cs` | Uses ADB for device operations |
| `FastbootClient.cs` | Fastboot protocol (bootloader) |
| `DownloadModeFlasher.cs` | Samsung Download Mode |

## References

1. **Android ADB Protocol** - `system/core/adb/protocol.txt`
2. **Android ADB Transport** - `system/core/adb/transport.cpp`
3. **Android ADB Auth** - `system/core/adb/auth.cpp`
4. **Android ADB Sync** - `system/core/adb/sync.cpp`
5. **Android ADB Shell** - `system/core/adb/shell.cpp`
6. **Magisk ADB Scripts** - `scripts/adb_install.sh`

## Testing Recommendations

### Unit Tests
```csharp
[Test] void ConnectUsb_ValidDevice_HandshakeSuccess()
[Test] void ConnectTcp_ValidHost_HandshakeSuccess()
[Test] void Authenticate_ExistingKey_SignatureVerified()
[Test] void Authenticate_NewKey_UserAcceptance()
[Test] void ExecuteShell_SimpleCommand_ReturnsOutput()
[Test] void ExecuteShellRoot_RootCommand_ReturnsRootOutput()
[Test] void HasRoot_MagiskInstalled_ReturnsTrue()
[Test] void CheckMagiskStatus_MagiskInstalled_ReturnsVersion()
[Test] void PullFile_ValidPath_DownloadsFile()
[Test] void PushFile_ValidPath_UploadsFile()
[Test] void RebootAsync_Normal_DeviceReconnects()
[Test] void RebootBootloader_EntersFastboot()
[Test] void GetProperties_ParsesKeyValuePairs()
```

### Integration Tests (Requires Physical Device)
```csharp
[Test] void USB_AuthorizedDevice_Connects()
[Test] void TCP_WiFiADB_Connects()
[Test] void FullUninstallViaADB_RemovesMagisk()
```

## Cross-Reference
See `knowledge-base/cross-reference-map/AdbClient.cs.md` for line-by-line source mapping.