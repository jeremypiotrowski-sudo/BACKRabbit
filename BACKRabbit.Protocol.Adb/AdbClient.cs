using System.Buffers.Binary;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using BACKRabbit.Usb;

namespace BACKRabbit.Protocol.Adb;

/// <summary>
/// ADB Client - Complete Android Debug Bridge implementation
/// Supports USB and TCP transports, shell, sync, and file operations
/// Ported from Android's adb protocol specification
/// </summary>
public class AdbClient : IAdbClient
{
    private Stream? _transport;
    private TcpClient? _tcpClient; // Preserved for TLS upgrade (Android 14+ Wireless Debugging)
    private UsbDeviceManager? _usbManager;
    private bool _connected;
    private bool _usingServerProxy; // True when connected via ADB server (127.0.0.1:5037)
    private uint _localId = 1;
    internal readonly Dictionary<uint, AdbStream> _streams = new();
    
    // ADB Protocol constants
    private const uint A_VERSION = 0x01000000;
    private const int A_MAXDATA = 4096;
    private const string ADB_HOST = "host::";
    
    // Packet types (little-endian ASCII)
    private const uint A_SYNC = 0x434E5953;
    private const uint A_CNXN = 0x4E584E43;
    private const uint A_AUTH = 0x48545541;
    private const uint A_OPEN = 0x4E45504F;
    private const uint A_OKAY = 0x59414B4F;
    private const uint A_CLSE = 0x45534C43;
    private const uint A_WRTE = 0x45545257;
    
    // Auth types
    private const int ADB_AUTH_TOKEN = 1;
    private const int ADB_AUTH_SIGNATURE = 2;
    private const int ADB_AUTH_RSAPUBLICKEY = 3;
    
    // TLS (Android 14+ Wireless Debugging)
    private const uint A_STLS = 0x534C5453; // "STLS" in little-endian ASCII
    
    // Sync service constants
    private const uint ID_STAT = 0x54415453;
    private const uint ID_SEND = 0x444E4553;
    private const uint ID_RECV = 0x56434552;
    private const uint ID_LIST = 0x5453494C;
    private const uint ID_QUIT = 0x54495551;
    
    public bool IsConnected => _connected && _transport != null;
    public string? Serial { get; private set; }
    public string? DeviceModel { get; private set; }
    public string? AndroidVersion { get; private set; }
    
    public event EventHandler<string>? LogMessage;
    public event EventHandler? DeviceStateChanged;
    
    /// <summary>
    /// Connect via USB using UsbDeviceManager
    /// </summary>
    public async Task<bool> ConnectUsbAsync(UsbDeviceManager usb, CancellationToken ct = default)
    {
        _usbManager = usb;
        
        // Try Samsung-specific enumeration first for backward compatibility,
        // then fall back to cross-vendor Android ADB enumeration.
        var devices = usb.EnumerateSamsungDevices();
        var adbDevice = devices.FirstOrDefault(d => d.DeviceMode == DeviceMode.ADB);
        
        if (adbDevice == null)
        {
            devices = UsbDeviceManager.EnumerateAllAdbDevices();
            adbDevice = devices.FirstOrDefault(d => d.DeviceMode == DeviceMode.ADB);
        }
        
        if (adbDevice == null)
        {
            Log("No ADB device found");
            return false;
        }
        
        Log($"Found ADB device: VID={adbDevice.VendorId:X4} PID={adbDevice.ProductId:X4} ({adbDevice.Product})");
        
        if (!usb.OpenDevice(adbDevice.VendorId, adbDevice.ProductId))
        {
            Log("Failed to open ADB device");
            return false;
        }
        
        Serial = adbDevice.SerialNumber;
        if (string.IsNullOrEmpty(Serial))
            Serial = $"{adbDevice.VendorId:X4}:{adbDevice.ProductId:X4}";
        Log($"Opening USB transport for {Serial}");
        
        _transport = new UsbStream(usb);
        return await ConnectAsync(ct);
    }
    
    /// <summary>
    /// Connect via TCP (WiFi ADB on port 5555, or Wireless Debugging on any port).
    /// For Android 14+ Wireless Debugging (TLS), this method first tries direct TCP
    /// with TLS upgrade. If that fails, falls back to using the local ADB server
    /// (adb.exe on 127.0.0.1:5037) as a proxy — the server handles TLS transparently.
    /// </summary>
    public async Task<bool> ConnectTcpAsync(string host, int port = 5555, CancellationToken ct = default)
    {
        Log($"Connecting to {host}:{port}...");
        
        _tcpClient = new TcpClient();
        await _tcpClient.ConnectAsync(host, port, ct);
        _transport = _tcpClient.GetStream();
        Serial = $"{host}:{port}";
        
        var result = await ConnectAsync(ct);
        
        // If direct connection failed due to TLS (STLS response), try ADB server proxy
        if (!result && _tcpClient != null)
        {
            Log("Direct connection failed — trying ADB server proxy (127.0.0.1:5037)...");
            _tcpClient.Dispose();
            _tcpClient = null;
            _transport?.Dispose();
            _transport = null;
            
            return await ConnectViaAdbServerAsync(ct);
        }
        
        return result;
    }
    
    /// <summary>
    /// Connect via local ADB server proxy (127.0.0.1:5037).
    /// The ADB server handles all transport complexity (TLS, auth, etc.) and
    /// provides a clean ADB stream to the device. This is the standard approach
    /// used by most ADB tools — they don't implement the full protocol directly.
    /// 
    /// Protocol: send "host:transport:<serial>" to ADB server, receive OKAY/FAIL,
    /// then the stream becomes a direct transport to the device.
    /// </summary>
    private async Task<bool> ConnectViaAdbServerAsync(CancellationToken ct)
    {
        try
        {
            var serverClient = new TcpClient();
            await serverClient.ConnectAsync("127.0.0.1", 5037, ct);
            var serverStream = serverClient.GetStream();
            
            // Request device transport from ADB server
            // Format: "host:transport:<serial>" (4-byte hex length + ASCII payload)
            var request = $"host:transport:{Serial}";
            var requestBytes = Encoding.ASCII.GetBytes(request);
            var lengthHex = $"{requestBytes.Length:X4}";
            var lengthBytes = Encoding.ASCII.GetBytes(lengthHex);
            
            await serverStream.WriteAsync(lengthBytes, ct);
            await serverStream.WriteAsync(requestBytes, ct);
            await serverStream.FlushAsync(ct);
            
            // Read response: 4 bytes "OKAY" or "FAIL" + 4-byte hex length + message
            var statusBuffer = new byte[4];
            var read = await serverStream.ReadAsync(statusBuffer, 0, 4, ct);
            if (read < 4)
            {
                Log("ADB server: no response");
                serverClient.Dispose();
                return false;
            }
            
            var status = Encoding.ASCII.GetString(statusBuffer, 0, 4);
            
            if (status == "OKAY")
            {
                Log("ADB server: transport connected");
                // The server stream is now a direct ADB transport to the device.
                // The ADB server has already completed the full handshake (CNXN, auth, TLS).
                // After host:transport OKAY, the stream is ready for OPEN commands directly.
                // No CNXN packet is forwarded — the server bridges the raw transport.
                _tcpClient = serverClient;
                _transport = serverStream;
                _connected = true;
                _usingServerProxy = true;
                
                Log("ADB connected via server proxy");
                await PopulateDeviceInfoAsync(ct);
                DeviceStateChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }
            else if (status == "FAIL")
            {
                // Read error message length and content
                var lenBuffer = new byte[4];
                read = await serverStream.ReadAsync(lenBuffer, 0, 4, ct);
                if (read == 4)
                {
                    var msgLen = int.Parse(Encoding.ASCII.GetString(lenBuffer, 0, 4), 
                        System.Globalization.NumberStyles.HexNumber);
                    var msgBuffer = new byte[msgLen];
                    await serverStream.ReadAsync(msgBuffer, 0, msgLen, ct);
                    var errorMsg = Encoding.ASCII.GetString(msgBuffer);
                    Log($"ADB server: FAIL — {errorMsg}");
                }
                else
                {
                    Log("ADB server: FAIL (no message)");
                }
                serverClient.Dispose();
                return false;
            }
            else
            {
                Log($"ADB server: unexpected response: {status}");
                serverClient.Dispose();
                return false;
            }
        }
        catch (Exception ex)
        {
            Log($"ADB server proxy failed: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Establish ADB connection handshake.
    /// Handles three response types:
    ///   A_STLS (0x534C5453) — Android 14+ Wireless Debugging requires TLS encryption
    ///   A_AUTH + ADB_AUTH_TOKEN — Standard RSA authentication
    ///   A_CNXN — Direct connection (no auth needed, rare)
    /// </summary>
    private async Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        Log("Sending CNXN (connect)...");
        
        // AOSP adb.cpp sends features string: "host::features=cmd,stat_v2,ls_v2,shell_v2,abb_exec,fixed_push_mkdir,abb,apex"
        // Without features, some Android 14+ devices reject shell commands even if auth passes
        var identity = $"host::features=cmd,stat_v2,ls_v2,shell_v2,abb_exec,fixed_push_mkdir,abb,apex";
        var cnxn = CreatePacket(A_CNXN, A_VERSION, A_MAXDATA, identity);
        await WritePacketAsync(cnxn, ct);
        
        var response = await ReadPacketAsync(ct);
        
        if (response == null)
        {
            Log("No response from device");
            return false;
        }
        
        // Android 14+ Wireless Debugging: device demands TLS encryption
        // Response is "STLS" (0x534C5453) — wrap stream in SslStream, re-send CNXN
        if (response.type == A_STLS)
        {
            Log("Device requires TLS encryption (Android 14+ Wireless Debugging)");
            return await UpgradeToTlsAndReconnectAsync(ct);
        }
        
        if (response.type == A_AUTH && response.arg0 == ADB_AUTH_TOKEN)
        {
            Log("Authentication required (ADB_AUTH_TOKEN)");
            // Guard against null token data (CS8604)
            if (response.data == null || response.data.Length == 0)
            {
                Log("AUTH_TOKEN received but token data is null/empty — cannot authenticate");
                return false;
            }
            return await AuthenticateAsync(response.data, ct);
        }
        
        if (response.type == A_CNXN)
        {
            _connected = true;
            Log("ADB connected successfully");
            await PopulateDeviceInfoAsync(ct);
            DeviceStateChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        
        Log($"Unexpected response: 0x{response.type:X4}");
        return false;
    }
    
    /// <summary>
    /// Upgrade plain TCP connection to TLS, then re-send CNXN over encrypted channel.
    /// Android 14+ Wireless Debugging mandates TLS. The device sends A_STLS after
    /// receiving the first CNXN. We wrap the NetworkStream in SslStream, authenticate
    /// as client (no certificate required — ADB uses anonymous TLS), then restart
    /// the handshake over the encrypted stream.
    /// Reference: AOSP system/core/adb/tls/tls_connection.cpp
    /// </summary>
    private async Task<bool> UpgradeToTlsAndReconnectAsync(CancellationToken ct)
    {
        if (_tcpClient == null || _transport == null)
        {
            Log("Cannot upgrade to TLS — no TCP client available");
            return false;
        }
        
        Log("Upgrading to TLS...");
        
        // Wrap the raw NetworkStream in SslStream
        // ADB uses anonymous TLS — no client certificate, no server certificate validation
        // Android 14 adbd uses TLS 1.2 with specific cipher suites
        var sslStream = new SslStream(
            _transport,
            leaveInnerStreamOpen: true, // Keep inner NetworkStream alive — we manage it via _tcpClient
            userCertificateValidationCallback: (sender, certificate, chain, errors) =>
            {
                // ADB uses self-signed certificates — accept any server cert
                if (certificate != null)
                    Log($"TLS server certificate: {certificate.Subject}");
                return true;
            },
            userCertificateSelectionCallback: null // No client certificate needed
        );
        
        try
        {
            // Android adbd uses BoringSSL with TLS 1.2 minimum, all cipher suites.
            // .NET SslStream on Windows uses SChannel — need compatible parameters.
            // Try TLS 1.2 explicitly (most compatible with Android 14 adbd).
            var options = new SslClientAuthenticationOptions
            {
                TargetHost = "", // Empty — ADB doesn't validate hostname
                EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12,
                CertificateRevocationCheckMode = X509RevocationMode.Online,
                EncryptionPolicy = EncryptionPolicy.RequireEncryption
            };
            await sslStream.AuthenticateAsClientAsync(options, ct);
            Log("TLS handshake complete");
        }
        catch (Exception ex)
        {
            Log($"TLS handshake failed: {ex.Message}");
            return false;
        }
        
        // Replace transport with encrypted stream
        _transport = sslStream;
        
        // Re-send CNXN over TLS
        Log("Re-sending CNXN over TLS...");
        var identity = $"host::features=cmd,stat_v2,ls_v2,shell_v2,abb_exec,fixed_push_mkdir,abb,apex";
        var cnxn = CreatePacket(A_CNXN, A_VERSION, A_MAXDATA, identity);
        await WritePacketAsync(cnxn, ct);
        
        var response = await ReadPacketAsync(ct);
        
        if (response == null)
        {
            Log("No response from device after TLS upgrade");
            return false;
        }
        
        if (response.type == A_AUTH && response.arg0 == ADB_AUTH_TOKEN)
        {
            Log("Authentication required (ADB_AUTH_TOKEN) over TLS");
            if (response.data == null || response.data.Length == 0)
            {
                Log("AUTH_TOKEN received but token data is null/empty");
                return false;
            }
            return await AuthenticateAsync(response.data, ct);
        }
        
        if (response.type == A_CNXN)
        {
            _connected = true;
            Log("ADB connected successfully over TLS");
            await PopulateDeviceInfoAsync(ct);
            DeviceStateChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        
        Log($"Unexpected response after TLS: 0x{response.type:X4}");
        return false;
    }
    
    /// <summary>
    /// Authenticate with RSA key pair
    /// </summary>
    private async Task<bool> AuthenticateAsync(byte[] token, CancellationToken ct)
    {
        Log("Performing RSA authentication...");
        
        var (privateKey, publicKey, publicKeyName) = AdbKeyManager.GetKeyPair();
        var signature = AdbKeyManager.SignToken(privateKey, token);
        
        Log("Sending AUTH_SIGNATURE...");
        var authSig = CreatePacket(A_AUTH, ADB_AUTH_SIGNATURE, 0, signature);
        await WritePacketAsync(authSig, ct);
        
        var response = await ReadPacketAsync(ct);
        
        if (response?.type == A_CNXN)
        {
            _connected = true;
            Log("ADB authenticated and connected");
            await PopulateDeviceInfoAsync(ct);
            return true;
        }
        
        if (response?.type == A_AUTH && response.arg0 == ADB_AUTH_TOKEN)
        {
            Log("Sending public key...");
            var publicKeyData = Encoding.UTF8.GetBytes(publicKeyName + "\0" + Convert.ToBase64String(publicKey));
            var authKey = CreatePacket(A_AUTH, ADB_AUTH_RSAPUBLICKEY, 0, publicKeyData);
            await WritePacketAsync(authKey, ct);
            
            Log("Waiting for user to accept RSA key on device...");
            for (int i = 0; i < 30; i++)
            {
                await Task.Delay(1000, ct);
                response = await ReadPacketAsync(ct);
                if (response?.type == A_CNXN)
                {
                    _connected = true;
                    Log("ADB authenticated with new key");
                    await PopulateDeviceInfoAsync(ct);
                    return true;
                }
            }
        }
        
        Log("Authentication failed");
        return false;
    }
    
    /// <summary>
    /// Populate device information
    /// </summary>
    private async Task PopulateDeviceInfoAsync(CancellationToken ct)
    {
        try
        {
            var props = await GetPropertiesAsync(ct);
            DeviceModel = props.TryGetValue("ro.product.model", out var m) ? m : "Unknown";
            AndroidVersion = props.TryGetValue("ro.build.version.release", out var v) ? v : "Unknown";
            Log($"Device: {DeviceModel}, Android {AndroidVersion}");
        }
        catch (Exception ex)
        {
            Log($"Failed to get device info: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Open ADB stream (shell, sync, framebuffer, etc.)
    /// </summary>
    public async Task<AdbStream?> OpenStreamAsync(string destination, CancellationToken ct = default)
    {
        if (!_connected) throw new InvalidOperationException("Not connected to ADB");
        
        var localId = _localId++;
        Log($"Opening stream: {destination} (localId={localId})");
        
        var open = CreatePacket(A_OPEN, localId, 0, destination);
        await WritePacketAsync(open, ct);
        
        var response = await ReadPacketAsync(ct);
        
        if (response?.type == A_OKAY)
        {
            var stream = new AdbStream(this, localId, response.arg0);
            _streams[localId] = stream;
            return stream;
        }
        
        if (response?.type == A_CLSE)
        {
            Log($"Stream closed by device: {destination}");
            return null;
        }
        
        Log($"Failed to open stream: 0x{response?.type:X4}");
        return null;
    }
    
    /// <summary>
    /// Execute shell command and return output.
    /// When connected via ADB server proxy, uses the server's host:shell service
    /// directly (avoids the ADB packet protocol which doesn't work over bridged transport).
    /// When connected directly, uses the standard ADB OPEN shell: protocol.
    /// </summary>
    public async Task<string> ExecuteShellAsync(string command, CancellationToken ct = default)
    {
        Log($"Executing shell: {command}");
        
        if (_usingServerProxy)
        {
            return await ExecuteShellViaServerAsync(command, ct);
        }
        
        var stream = await OpenStreamAsync($"shell:{command}", ct);
        if (stream == null) throw new AdbException("Failed to open shell stream");
        
        var output = new StringBuilder();
        
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var data = await stream.ReadAsync(ct);
                if (data == null || data.Length == 0) break;
                output.Append(Encoding.UTF8.GetString(data));
            }
        }
        finally
        {
            await stream.CloseAsync(ct);
        }
        
        Log($"Shell output: {output.Length} bytes");
        return output.ToString();
    }
    
    /// <summary>
    /// Execute shell command via ADB server's host:shell service.
    /// This is the standard approach used by most ADB tools — they don't implement
    /// the full ADB packet protocol, they just use the server's services.
    /// 
    /// Protocol: send "host:shell:<command>" to ADB server, receive OKAY/FAIL,
    /// then read raw shell output until connection closes.
    /// </summary>
    /// <summary>
    /// Execute shell command via ADB server's host:transport + shell: protocol.
    /// Two-step on a single connection:
    ///   1. Send "host:transport:<serial>" → receive OKAY
    ///   2. Send "shell:<command>" → receive OKAY → read shell output
    /// This is the standard approach used by most ADB tools.
    /// </summary>
    private async Task<string> ExecuteShellViaServerAsync(string command, CancellationToken ct)
    {
        try
        {
            // Open a fresh connection to the ADB server for this shell command
            using var serverClient = new TcpClient();
            await serverClient.ConnectAsync("127.0.0.1", 5037, ct);
            var serverStream = serverClient.GetStream();
            
            // Step 1: Request device transport
            var transportRequest = $"host:transport:{Serial}";
            await SendAdbServerRequestAsync(serverStream, transportRequest, ct);
            
            var transportStatus = await ReadAdbServerStatusAsync(serverStream, ct);
            if (transportStatus != "OKAY")
            {
                Log($"ADB server transport: {transportStatus}");
                return "";
            }
            
            // Step 2: Request shell command on the same connection
            var shellRequest = $"shell:{command}";
            await SendAdbServerRequestAsync(serverStream, shellRequest, ct);
            
            var shellStatus = await ReadAdbServerStatusAsync(serverStream, ct);
            if (shellStatus != "OKAY")
            {
                Log($"ADB server shell: {shellStatus}");
                return "";
            }
            
            // Shell output follows directly on the stream
            var output = new StringBuilder();
            var buffer = new byte[4096];
            
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var bytesRead = await serverStream.ReadAsync(buffer, 0, buffer.Length, ct);
                    if (bytesRead == 0) break;
                    output.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
                }
            }
            catch (IOException)
            {
                // Stream closed by server — normal end of shell output
            }
            
            Log($"Shell output: {output.Length} bytes");
            return output.ToString();
        }
        catch (Exception ex)
        {
            Log($"ADB server shell failed: {ex.Message}");
            return "";
        }
    }
    
    /// <summary>
    /// Send a request to the ADB server: 4-byte hex length + ASCII payload.
    /// </summary>
    private async Task SendAdbServerRequestAsync(Stream stream, string request, CancellationToken ct)
    {
        var requestBytes = Encoding.ASCII.GetBytes(request);
        var lengthHex = $"{requestBytes.Length:X4}";
        var lengthBytes = Encoding.ASCII.GetBytes(lengthHex);
        
        await stream.WriteAsync(lengthBytes, ct);
        await stream.WriteAsync(requestBytes, ct);
        await stream.FlushAsync(ct);
    }
    
    /// <summary>
    /// Read ADB server status: 4 bytes "OKAY" or "FAIL".
    /// If FAIL, reads and logs the error message.
    /// </summary>
    private async Task<string> ReadAdbServerStatusAsync(Stream stream, CancellationToken ct)
    {
        var statusBuffer = new byte[4];
        var read = await stream.ReadAsync(statusBuffer, 0, 4, ct);
        if (read < 4) return "NO_RESPONSE";
        
        var status = Encoding.ASCII.GetString(statusBuffer, 0, 4);
        
        if (status == "FAIL")
        {
            // Read error message length and content
            var lenBuffer = new byte[4];
            read = await stream.ReadAsync(lenBuffer, 0, 4, ct);
            if (read == 4)
            {
                var msgLen = int.Parse(Encoding.ASCII.GetString(lenBuffer, 0, 4),
                    System.Globalization.NumberStyles.HexNumber);
                var msgBuffer = new byte[msgLen];
                await stream.ReadAsync(msgBuffer, 0, msgLen, ct);
                var errorMsg = Encoding.ASCII.GetString(msgBuffer);
                Log($"ADB server: FAIL — {errorMsg}");
            }
        }
        
        return status;
    }
    
    /// <summary>
    /// Execute shell command with root (su)
    /// </summary>
    public async Task<string> ExecuteShellRootAsync(string command, CancellationToken ct = default)
    {
        return await ExecuteShellAsync($"su -c \"{command.Replace("\"", "\\\"")}\"", ct);
    }
    
    /// <summary>
    /// IAdbClient interface method — execute as root, tries direct first, falls back to su -c.
    /// </summary>
    public async Task<string> ExecuteRootShellAsync(string command, CancellationToken ct = default)
    {
        var direct = await ExecuteShellAsync(command, ct);
        if (direct.Contains("Permission denied"))
            return await ExecuteShellRootAsync(command, ct);
        return direct;
    }
    
    /// <summary>
    /// Wait for device ADB to become available after reboot.
    /// Polls every second until device responds or timeout.
    /// </summary>
    public async Task<bool> WaitForDeviceAsync(int timeoutMs = 60000, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var result = await ExecuteShellAsync("echo DEVICE_READY", ct);
                if (result.Contains("DEVICE_READY"))
                {
                    Log($"Device ready after {sw.ElapsedMilliseconds}ms");
                    return true;
                }
            }
            catch
            {
                // Device not reachable yet — expected during boot
            }
            await Task.Delay(1000, ct);
        }
        Log($"Device wait timed out after {timeoutMs}ms");
        return false;
    }
    
    /// <summary>
    /// Check if device has root access
    /// </summary>
    public async Task<bool> HasRootAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await ExecuteShellAsync("su -c 'id'", ct);
            return result.Contains("uid=0");
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Check if Magisk is installed or was previously installed.
    /// Uses three detection layers:
    ///   1. magisk -c (binary check — fails if binary was wiped by factory reset)
    ///   2. /data/adb existence (forensic trace — survives factory reset but may be locked)
    ///   3. /data/adb readability (SELinux state — "Permission denied" = Magisk WAS here)
    /// </summary>
    public async Task<MagiskStatus> CheckMagiskStatusAsync(CancellationToken ct = default)
    {
        var status = new MagiskStatus();
        
        try
        {
            // Layer 1: Standard Magisk binary detection
            var magiskC = await ExecuteShellAsync("magisk -c", ct);
            var trimmed = magiskC.Trim();
            status.IsInstalled = !string.IsNullOrEmpty(trimmed) && !trimmed.Contains("not found") && !trimmed.Contains("No such file") && !trimmed.Contains("inaccessible");
            
            if (status.IsInstalled)
            {
                // Parse version from magisk -c output (format: "version:variant")
                var colonIdx = trimmed.IndexOf(':');
                status.Version = colonIdx > 0 ? trimmed.Substring(0, colonIdx) : trimmed;
                
                // Get module count
                try
                {
                    var modules = await ExecuteShellAsync("ls /data/adb/modules", ct);
                    if (!modules.Contains("No such file") && !modules.Contains("Permission denied"))
                        status.ModuleCount = modules.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
                }
                catch { /* modules directory may not exist */ }
                
                // Check database
                try
                {
                    var dbCheck = await ExecuteShellAsync("ls -la /data/adb/magisk.db", ct);
                    status.HasDatabase = !dbCheck.Contains("No such file");
                }
                catch { /* db may not exist */ }
            }
            
            // Layer 2: Forensic filesystem trace — /data/adb existence
            // Stock phones do NOT have /data/adb. If it exists, Magisk was installed at some point.
            // Factory reset wipes the binary but the directory structure survives (root:root, SELinux locked).
            //
            // CRITICAL: On SELinux Enforcing devices, "test -d /data/adb" returns "NOT_FOUND" even when
            // the directory EXISTS because test(1) can't traverse /data/ (mode 0771, root:system).
            // We use "ls -d" instead — it returns "Permission denied" when the directory exists
            // but is locked, and "No such file or directory" when it genuinely doesn't exist.
            try
            {
                var adbLs = await ExecuteShellAsync("ls -d /data/adb 2>&1", ct);
                var lsTrimmed = adbLs.Trim();
                
                // "Permission denied" = directory EXISTS but SELinux blocks access
                // "No such file or directory" = directory genuinely doesn't exist
                status.HasAdbDirectory = lsTrimmed.Contains("Permission denied") || 
                                         (lsTrimmed.Length > 0 && !lsTrimmed.Contains("No such file"));
                status.IsAdbReadable = !lsTrimmed.Contains("Permission denied") && status.HasAdbDirectory;
                
                if (status.HasAdbDirectory)
                {
                    // If /data/adb exists but magisk -c failed, this is post-reset residual state
                    if (!status.IsInstalled)
                    {
                        status.IsResidual = true;
                        status.ResidualEvidence = status.IsAdbReadable 
                            ? "/data/adb exists and is readable but magisk binary is missing (partial uninstall?)"
                            : "/data/adb exists but is LOCKED by SELinux — Magisk was installed, factory reset wiped binary but left directory structure";
                    }
                }
            }
            catch { /* filesystem checks may fail */ }
        }
        catch (Exception ex)
        {
            Log($"Magisk check error: {ex.Message}");
        }
        
        return status;
    }
    
    /// <summary>
    /// Get all device properties
    /// </summary>
    public async Task<Dictionary<string, string>> GetPropertiesAsync(CancellationToken ct = default)
    {
        var output = await ExecuteShellAsync("getprop", ct);
        var properties = new Dictionary<string, string>();
        
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var match = System.Text.RegularExpressions.Regex.Match(line, @"\[(.+?)\]: \[(.+?)\]");
            if (match.Success)
            {
                properties[match.Groups[1].Value] = match.Groups[2].Value;
            }
        }
        
        return properties;
    }
    
    /// <summary>
    /// Check bootloader lock state using device properties
    /// Samsung: ro.boot.other.locked=1 (locked) or 0 (unlocked)
    /// Generic: ro.boot.flashing.locked, ro.boot.verifiedbootstate (green=locked, orange=unlocked)
    /// </summary>
    public async Task<BootloaderLockStatus> CheckBootloaderLockStatusAsync(CancellationToken ct = default)
    {
        var props = await GetPropertiesAsync(ct);
        var status = new BootloaderLockStatus { RawProperties = props };
        
        // Samsung-specific: ro.boot.other.locked
        if (props.TryGetValue("ro.boot.other.locked", out var samsungLocked))
        {
            status.LockProperty = $"ro.boot.other.locked={samsungLocked}";
            status.IsLocked = samsungLocked == "1";
            status.IsUnlocked = samsungLocked == "0";
        }
        // Generic: ro.boot.flashing.locked
        else if (props.TryGetValue("ro.boot.flashing.locked", out var flashingLocked))
        {
            status.LockProperty = $"ro.boot.flashing.locked={flashingLocked}";
            status.IsLocked = flashingLocked == "1";
            status.IsUnlocked = flashingLocked == "0";
        }
        // Fallback: verifiedbootstate
        if (!status.IsUnlocked && props.TryGetValue("ro.boot.verifiedbootstate", out var vbState))
        {
            status.VerifiedBootState = vbState;
            if (vbState == "orange")
            {
                status.IsUnlocked = true;
                status.IsLocked = false;
            }
        }
        
        return status;
    }
    
    /// <summary>
    /// Pull file from device
    /// </summary>
    public async Task<bool> PullFileAsync(string remotePath, string localPath, CancellationToken ct = default)
    {
        Log($"Pulling {remotePath} -> {localPath}");
        
        var stream = await OpenStreamAsync("sync:", ct);
        if (stream == null) throw new AdbException("Failed to open sync stream");
        
        try
        {
            var commandStr = $"RECV{remotePath.Length:D4}{remotePath}";
            var command = Encoding.ASCII.GetBytes(commandStr);
            await stream.WriteAsync(command, ct);
            
            using var fileStream = File.Create(localPath);
            var header = new byte[8];
            
            while (!ct.IsCancellationRequested)
            {
                var headerData = await stream.ReadExactAsync(8, ct);
                if (headerData == null || headerData.Length < 8) break;
                
                var id = BinaryPrimitives.ReadUInt32LittleEndian(headerData.AsSpan(0, 4));
                var size = BinaryPrimitives.ReadUInt32LittleEndian(headerData.AsSpan(4, 4));
                
                if (id == ID_QUIT) break;
                if (id != 0x41415444) continue;
                
                var data = await stream.ReadExactAsync((int)size, ct);
                if (data != null)
                {
                    fileStream.Write(data, 0, data.Length);
                }
            }
            
            Log($"Pull complete: {localPath}");
            return true;
        }
        finally
        {
            await stream.CloseAsync(ct);
        }
    }
    
    /// <summary>
    /// Push file to device
    /// </summary>
    public async Task<bool> PushFileAsync(string localPath, string remotePath, CancellationToken ct = default)
    {
        Log($"Pushing {localPath} -> {remotePath}");
        
        var stream = await OpenStreamAsync("sync:", ct);
        if (stream == null) throw new AdbException("Failed to open sync stream");
        
        try
        {
            var mode = 0x1A4;
            var commandStr = $"SEND{remotePath},{mode}";
            var command = Encoding.ASCII.GetBytes(commandStr);
            var header = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), ID_SEND);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4, 4), (uint)command.Length);
            
            await stream.WriteAsync(header, ct);
            await stream.WriteAsync(command, ct);
            
            using var fileStream = File.OpenRead(localPath);
            var buffer = new byte[4096];
            int bytesRead;
            
            while ((bytesRead = await fileStream.ReadAsync(buffer, ct)) > 0)
            {
                var dataHeader = new byte[8];
                BinaryPrimitives.WriteUInt32LittleEndian(dataHeader.AsSpan(0, 4), 0x41415444);
                BinaryPrimitives.WriteUInt32LittleEndian(dataHeader.AsSpan(4, 4), (uint)bytesRead);
                
                await stream.WriteAsync(dataHeader, ct);
                await stream.WriteAsync(buffer[..bytesRead], ct);
            }
            
            var doneHeader = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(doneHeader.AsSpan(0, 4), 0x454E4F44);
            BinaryPrimitives.WriteUInt32LittleEndian(doneHeader.AsSpan(4, 4), (uint)DateTimeOffset.Now.ToUnixTimeSeconds());
            await stream.WriteAsync(doneHeader, ct);
            
            await stream.ReadAsync(ct);
            
            Log($"Push complete: {remotePath}");
            return true;
        }
        finally
        {
            await stream.CloseAsync(ct);
        }
    }
    
    /// <summary>
    /// Reboot device
    /// </summary>
    public async Task<bool> RebootAsync(string mode = "", CancellationToken ct = default)
    {
        var command = mode switch
        {
            "bootloader" => "reboot:bootloader",
            "recovery" => "reboot:recovery",
            "edl" => "reboot:edl",
            "sideload" => "reboot:sideload",
            _ => "reboot:"
        };
        
        Log($"Rebooting device (mode: {mode ?? "normal"})");
        
        try
        {
            await ExecuteShellAsync(command, ct);
            return true;
        }
        catch
        {
            _connected = false;
            Log("Device disconnected (expected during reboot)");
            return true;
        }
    }
    
    /// <summary>
    /// Reboot to bootloader
    /// </summary>
    public async Task<bool> RebootBootloaderAsync(CancellationToken ct = default)
    {
        return await RebootAsync("bootloader", ct);
    }
    
    /// <summary>
    /// Reboot to recovery
    /// </summary>
    public async Task<bool> RebootRecoveryAsync(CancellationToken ct = default)
    {
        return await RebootAsync("recovery", ct);
    }
    
    /// <summary>
    /// Reboot to Download Mode (Samsung)
    /// </summary>
    public async Task<bool> RebootDownloadAsync(CancellationToken ct = default)
    {
        var result1 = await ExecuteShellRootAsync("reboot download", ct);
        var result2 = await RebootAsync("bootloader", ct);
        return result1 != null || result2;
    }
    
    /// <summary>
    /// Close ADB connection
    /// </summary>
    public void Disconnect()
    {
        foreach (var stream in _streams.Values)
        {
            stream.Dispose();
        }
        _streams.Clear();
        
        _transport?.Dispose();
        _transport = null;
        _tcpClient?.Dispose();
        _tcpClient = null;
        _connected = false;
        
        Log("ADB disconnected");
        DeviceStateChanged?.Invoke(this, EventArgs.Empty);
    }
    
    #region Packet Helpers
    
    internal AdbPacket CreatePacket(uint type, uint arg0, uint arg1, string data)
    {
        return CreatePacket(type, arg0, arg1, Encoding.UTF8.GetBytes(data));
    }
    
    internal AdbPacket CreatePacket(uint type, uint arg0, uint arg1, byte[]? data = null)
    {
        var packet = new AdbPacket
        {
            type = type,
            arg0 = arg0,
            arg1 = arg1,
            data_length = (uint)(data?.Length ?? 0),
            data = data
        };
        
        packet.checksum = CalculateChecksum(data);
        packet.magic = packet.type ^ 0xFFFFFFFF;
        
        return packet;
    }
    
    private uint CalculateChecksum(byte[]? data)
    {
        if (data == null || data.Length == 0) return 0;
        
        uint checksum = 0;
        foreach (var b in data)
        {
            checksum ^= b;
        }
        return checksum;
    }
    
    internal async Task WritePacketAsync(AdbPacket packet, CancellationToken ct)
    {
        if (_transport == null) throw new InvalidOperationException("Not connected");
        
        var header = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), packet.type);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4, 4), packet.arg0);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8, 4), packet.arg1);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12, 4), packet.data_length);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16, 4), packet.checksum);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20, 4), packet.magic);
        
        await _transport.WriteAsync(header, ct);
        
        if (packet.data != null && packet.data.Length > 0)
        {
            await _transport.WriteAsync(packet.data, ct);
        }
        
        await _transport.FlushAsync(ct);
    }
    
    /// <summary>
    /// Read exactly <paramref name="count"/> bytes from transport, looping until full.
    /// TCP streams can return partial data — this guarantees a complete read.
    /// </summary>
    private async Task<byte[]?> ReadExactAsync(int count, CancellationToken ct)
    {
        if (_transport == null) return null;
        var buffer = new byte[count];
        int offset = 0;
        while (offset < count)
        {
            var read = await _transport.ReadAsync(buffer.AsMemory(offset, count - offset), ct);
            if (read == 0) return null; // Stream closed
            offset += read;
        }
        return buffer;
    }

    internal async Task<AdbPacket?> ReadPacketAsync(CancellationToken ct)
    {
        if (_transport == null) return null;
        
        var header = await ReadExactAsync(24, ct);
        if (header == null) return null;
        
        var packet = new AdbPacket
        {
            type = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0, 4)),
            arg0 = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4, 4)),
            arg1 = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8, 4)),
            data_length = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12, 4)),
            checksum = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(16, 4)),
            magic = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(20, 4))
        };
        
        if (packet.data_length > 0)
        {
            packet.data = await ReadExactAsync((int)packet.data_length, ct);
            if (packet.data == null)
            {
                Log($"Failed to read packet data: expected {packet.data_length} bytes");
                return null;
            }
        }
        
        return packet;
    }
    
    private void Log(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        LogMessage?.Invoke(this, $"[{timestamp}] ADB: {message}");
    }
    
    #endregion
    
    public void Dispose()
    {
        Disconnect();
        _usbManager?.Dispose();
    }
}

/// <summary>
/// USB Stream wrapper for LibUsbDotNet
/// </summary>
public class UsbStream : Stream
{
    private readonly UsbDeviceManager _usb;
    
    public UsbStream(UsbDeviceManager usb)
    {
        _usb = usb;
    }
    
    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    
    public override int Read(byte[] buffer, int offset, int count)
    {
        var tempBuffer = new byte[count];
        var bytesRead = _usb.Read(tempBuffer);
        Array.Copy(tempBuffer, 0, buffer, offset, bytesRead);
        return bytesRead;
    }
    
    public override void Write(byte[] buffer, int offset, int count)
    {
        var tempBuffer = new byte[count];
        Array.Copy(buffer, offset, tempBuffer, 0, count);
        _usb.Write(tempBuffer);
    }
    
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
    }
}

/// <summary>
/// ADB Stream for data transfer
/// </summary>
public class AdbStream : IDisposable
{
    private readonly AdbClient _client;
    private readonly uint _localId;
    private readonly uint _remoteId;
    private bool _closed;
    
    public AdbStream(AdbClient client, uint localId, uint remoteId)
    {
        _client = client;
        _localId = localId;
        _remoteId = remoteId;
    }
    
    public async Task<byte[]?> ReadAsync(CancellationToken ct = default)
    {
        if (_closed) throw new ObjectDisposedException(nameof(AdbStream));
        
        var packet = await _client.ReadPacketAsync(ct);
        
        if (packet?.type == 0x45545257)
        {
            var okay = _client.CreatePacket(0x59414B4F, _localId, _remoteId, Array.Empty<byte>());
            await _client.WritePacketAsync(okay, ct);
            return packet.data;
        }
        
        if (packet?.type == 0x45534C43)
        {
            _closed = true;
            return null;
        }
        
        return null;
    }
    
    public async Task<byte[]?> ReadExactAsync(int length, CancellationToken ct = default)
    {
        var data = new byte[length];
        var offset = 0;
        
        while (offset < length)
        {
            var chunk = await ReadAsync(ct);
            if (chunk == null) break;
            
            var copySize = Math.Min(chunk.Length, length - offset);
            Array.Copy(chunk, 0, data, offset, copySize);
            offset += copySize;
        }
        
        return offset > 0 ? data : null;
    }
    
    public async Task WriteAsync(byte[] data, CancellationToken ct = default)
    {
        if (_closed) throw new ObjectDisposedException(nameof(AdbStream));
        
        var packet = _client.CreatePacket(0x45545257, _localId, _remoteId, data);
        await _client.WritePacketAsync(packet, ct);
    }
    
    public async Task CloseAsync(CancellationToken ct = default)
    {
        if (_closed) return;
        
        var close = _client.CreatePacket(0x45534C43, _localId, _remoteId, Array.Empty<byte>());
        await _client.WritePacketAsync(close, ct);
        _closed = true;
        
        _client._streams.Remove(_localId);
    }
    
    public void Dispose()
    {
        try
        {
            CloseAsync().Wait();
        }
        catch { }
    }
}

/// <summary>
/// ADB Packet structure
/// </summary>
public class AdbPacket
{
    public uint type;
    public uint arg0;
    public uint arg1;
    public uint data_length;
    public uint checksum;
    public uint magic;
    public byte[]? data;
}

/// <summary>
/// Magisk status information
/// </summary>
public class MagiskStatus
{
    public bool IsInstalled { get; set; }
    public string Version { get; set; } = "";
    public bool HasDatabase { get; set; }
    public int ModuleCount { get; set; }
    public List<string> Modules { get; set; } = new();
    
    // Forensic fields — detect residual Magisk traces after factory reset
    public bool HasAdbDirectory { get; set; }
    public bool IsAdbReadable { get; set; }
    public bool IsResidual { get; set; }
    public string ResidualEvidence { get; set; } = "";
}

/// <summary>
/// ADB Exception
/// </summary>
public class AdbException : Exception
{
    public AdbException(string message) : base(message) { }
    public AdbException(string message, Exception inner) : base(message, inner) { }
}