using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using BACKRabbit.Usb;

namespace BACKRabbit.Protocol.Firehose;

public class WinUsbTransport : IDeviceTransport
{
    private readonly string _devicePath;
    private readonly int _vid;
    private readonly int _pid;
    private readonly bool _isSerial;
    private UsbDeviceManager? _usbManager;
    private SerialPort? _serialPort;
    private FileStream? _serialStream;
    private bool _connected;

    public bool IsConnected => _connected;
    public string DevicePath => _devicePath;

    public WinUsbTransport(int vid, int pid)
    {
        _vid = vid; _pid = pid;
        _devicePath = $"USB\\VID_{vid:X4}&PID_{pid:X4}";
        _isSerial = false;
    }

    public WinUsbTransport(string devicePath)
    {
        _devicePath = devicePath;
        _isSerial = devicePath.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                 || devicePath.StartsWith("/dev/tty", StringComparison.OrdinalIgnoreCase)
                 || devicePath.StartsWith("/dev/cu.", StringComparison.OrdinalIgnoreCase);
        if (!_isSerial)
        {
            var vi = devicePath.IndexOf("VID_", StringComparison.OrdinalIgnoreCase);
            var pi = devicePath.IndexOf("PID_", StringComparison.OrdinalIgnoreCase);
            if (vi >= 0 && pi >= 0)
            {
                int.TryParse(devicePath.AsSpan(vi + 4, 4), System.Globalization.NumberStyles.HexNumber, null, out _vid);
                int.TryParse(devicePath.AsSpan(pi + 4, 4), System.Globalization.NumberStyles.HexNumber, null, out _pid);
            }
        }
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_isSerial) await ConnectSerialAsync(ct); else await ConnectUsbAsync(ct);
        _connected = true;
    }

    private Task ConnectSerialAsync(CancellationToken ct) => Task.Run(() =>
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _serialPort = new SerialPort(_devicePath) { BaudRate = 115200, DataBits = 8, Parity = Parity.None, StopBits = StopBits.One, ReadTimeout = 5000, WriteTimeout = 5000 };
            _serialPort.Open();
        }
        else
        {
            _serialStream = new FileStream(_devicePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 4096, true);
        }
    }, ct);

    private Task ConnectUsbAsync(CancellationToken ct) => Task.Run(() =>
    {
        if (_vid == 0 || _pid == 0) throw new InvalidOperationException("No VID/PID. Use WinUsbTransport(int vid, int pid) or path with VID_xxxx&PID_xxxx.");
        _usbManager = new UsbDeviceManager();
        if (!_usbManager.Open(_vid, _pid)) throw new InvalidOperationException($"Failed to open USB VID=0x{_vid:X4} PID=0x{_pid:X4}. Check EDL mode and WinUSB driver.");
    }, ct);

    public Task DisconnectAsync() => Task.Run(() =>
    {
        _connected = false;
        if (_serialPort != null) { try { _serialPort.Close(); } catch { } try { _serialPort.Dispose(); } catch { } _serialPort = null; }
        if (_serialStream != null) { try { _serialStream.Dispose(); } catch { } _serialStream = null; }
        if (_usbManager != null) { try { _usbManager.Dispose(); } catch { } _usbManager = null; }
    });

    public Task SendPacketAsync(SaharaPacket packet, CancellationToken ct = default) => SendRawAsync(packet.Serialize(), ct);

    public async Task<SaharaPacket> ReceivePacketAsync(CancellationToken ct = default)
    {
        var header = await ReceiveRawAsync(8, ct);
        if (header.Length < 8) throw new SaharaProtocolException("Failed to receive packet header");
        uint cmd = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0, 4));
        uint len = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4, 8));
        int pl = (int)len - 8;
        return new SaharaPacket(cmd, pl > 0 ? await ReceiveRawAsync(pl, ct) : Array.Empty<byte>());
    }

    public Task SendRawAsync(byte[] data, CancellationToken ct = default) => Task.Run(() =>
    {
        if (_serialPort != null) _serialPort.Write(data, 0, data.Length);
        else if (_serialStream != null) _serialStream.Write(data, 0, data.Length);
        else if (_usbManager != null) { int w = _usbManager.Write(data, data.Length); if (w != data.Length) throw new InvalidOperationException($"USB write incomplete: {w}/{data.Length}"); }
        else throw new InvalidOperationException("Transport not connected");
    }, ct);

    public Task<byte[]> ReceiveRawAsync(int maxLength, CancellationToken ct = default) => Task.Run(() =>
    {
        var buf = new byte[maxLength]; int read;
        if (_serialPort != null) { int avail = _serialPort.BytesToRead; if (avail <= 0) { try { read = _serialPort.Read(buf, 0, Math.Min(maxLength, 1)); } catch (TimeoutException) { return Array.Empty<byte>(); } } else read = _serialPort.Read(buf, 0, Math.Min(maxLength, avail)); }
        else if (_serialStream != null) read = _serialStream.Read(buf, 0, maxLength);
        else if (_usbManager != null) read = _usbManager.Read(buf, buf.Length);
        else throw new InvalidOperationException("Transport not connected");
        if (read <= 0) return Array.Empty<byte>();
        var r = new byte[read]; Array.Copy(buf, r, read); return r;
    }, ct);
}
