using LibUsbDotNet;
using LibUsbDotNet.Info;
using LibUsbDotNet.Main;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace BACKRabbit.Usb;

/// <summary>
/// USB Device Manager - wraps LibUsbDotNet for Samsung device communication
/// Handles Download Mode, ADB, and Fastboot USB interfaces
/// FIXED: Updated for LibUsbDotNet 2.2.8 API
/// </summary>
public class UsbDeviceManager : IDisposable
{
    private UsbDevice? _device;
    private bool _disposed;

public bool IsOpen => _device?.IsOpen == true;

    /// <summary>
    /// Alias for IsOpen for compatibility with existing code
    /// </summary>
    public bool IsConnected => IsOpen;

    public UsbDevice? Device => _device;

/// <summary>
    /// Write data to USB bulk endpoint
    /// </summary>
    public int Write(byte[] buffer, int timeout = 5000)
    {
        if (_device == null || !_device.IsOpen)
            throw new InvalidOperationException("Device not open");

        try
        {
            // Use the first OUT endpoint (bulk endpoint 0x02)
            var writer = _device.OpenEndpointWriter(WriteEndpointID.Ep02);
            if (writer == null)
                throw new InvalidOperationException("Could not open endpoint writer");

            var ec = writer.Write(buffer, timeout, out int transferred);
            return (int)transferred;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"USB Write failed: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Read data from USB bulk endpoint
    /// </summary>
    public int Read(byte[] buffer, int timeout = 5000)
    {
        if (_device == null || !_device.IsOpen)
            throw new InvalidOperationException("Device not open");

        try
        {
            // Use the first IN endpoint (bulk endpoint 0x81)
            var reader = _device.OpenEndpointReader(ReadEndpointID.Ep01);
            if (reader == null)
                throw new InvalidOperationException("Could not open endpoint reader");

            var ec = reader.Read(buffer, timeout, out int transferred);
            return (int)transferred;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"USB Read failed: {ex.Message}");
            return 0;
        }
    }

    // Samsung USB Vendor ID
    public const int SAMSUNG_VID = 0x04E8;

    // Samsung Product IDs for different modes
    public static readonly int[] DOWNLOAD_MODE_PIDS = [0x685D, 0x6860, 0x6862, 0x6864, 0x6601];
    public static readonly int[] ADB_PIDS = [0x6866, 0x6867, 0x6868, 0x6869];
    public static readonly int[] FASTBOOT_PIDS = [0x686A, 0x686B, 0x686C];

public event EventHandler<UsbDeviceEventArgs>? DeviceConnected;
    public event EventHandler? DeviceDisconnected;

    public UsbDeviceManager()
    {
    }

    /// <summary>
    /// Finds Samsung devices in Download Mode (VID: 04E8, PID: 6601)
    /// </summary>
    public static UsbDevice? FindSamsungDownloadMode()
    {
        var deviceFinder = new UsbDeviceFinder(0x04E8, 0x6601);
        return UsbDevice.OpenUsbDevice(deviceFinder);
    }

    /// <summary>
    /// Lists all connected USB devices with Samsung filtering
    /// </summary>
    public static List<UsbDeviceInfo> ListDevices(bool samsungOnly = false)
    {
        var devices = new List<UsbDeviceInfo>();

        foreach (var device in UsbDevice.AllDevices)
        {
            if (device is not UsbDevice usbDevice) continue;

            // FIX: Use Descriptor.VendorID/Descriptor.ProductID
            var vid = usbDevice.Info.Descriptor.VendorID;
            var pid = usbDevice.Info.Descriptor.ProductID;

            if (samsungOnly && vid != 0x04E8) continue;

            devices.Add(usbDevice.Info);
        }

        return devices;
    }

    /// <summary>
    /// Gets device information as formatted string
    /// </summary>
    public static string GetDeviceInfo(UsbDeviceInfo deviceInfo)
    {
        // FIX: Use correct property names
        return $"""
            Vendor ID: 0x{deviceInfo.Descriptor.VendorID:X4}
            Product ID: 0x{deviceInfo.Descriptor.ProductID:X4}
            Manufacturer: {deviceInfo.ManufacturerString ?? "Unknown"}
            Product: {deviceInfo.ProductString ?? "Unknown"}
            Serial: {deviceInfo.SerialString ?? "Unknown"}
            Configuration Count: {deviceInfo.Descriptor.ConfigurationCount}
            """;
    }

/// <summary>
    /// Gets endpoint addresses from device (use UsbDevice directly, not UsbDeviceInfo)
    /// </summary>
    public static List<byte> GetEndpointAddresses(UsbDevice device)
    {
        var endpoints = new List<byte>();

        try
        {
            if (device.Configs?.Count > 0)
            {
                var config = device.Configs[0];
                foreach (var iface in config.InterfaceInfoList)
                {
                    foreach (var endpoint in iface.EndpointInfoList)
                    {
                        // FIX: Use Descriptor.EndpointID
                        endpoints.Add(endpoint.Descriptor.EndpointID);
                    }
                }
            }
        }
        catch
        {
            // Ignore if Configs not accessible
        }

        return endpoints;
    }

    /// <summary>
    /// Enumerate all USB devices and find Samsung devices
    /// </summary>
    public List<BackRabbitUsbDeviceInfo> EnumerateSamsungDevices()
    {
        var devices = new List<BackRabbitUsbDeviceInfo>();

        var deviceList = UsbDevice.AllDevices;

        foreach (object obj in deviceList)
        {
            if (obj is UsbRegistry usbRegistry)
            {
                if (usbRegistry.Vid == SAMSUNG_VID)
                {
                    var info = new BackRabbitUsbDeviceInfo
                    {
                        VendorId = (ushort)usbRegistry.Vid,
                        ProductId = (ushort)usbRegistry.Pid,
                        SerialNumber = "",
                        Manufacturer = "",
                        Product = usbRegistry.Name ?? "",
                        DeviceMode = GetDeviceMode(usbRegistry.Pid),
                        IsOpen = false
                    };
                    devices.Add(info);
                }
            }
        }

        return devices;
    }

    /// <summary>
    /// Open Samsung device by serial number
    /// </summary>
    public bool OpenDevice(string serialNumber)
    {
        var deviceList = UsbDevice.AllDevices;

        foreach (object obj in deviceList)
        {
            if (obj is UsbRegistry usbRegistry)
            {
                if (usbRegistry.Vid == SAMSUNG_VID)
                {
                    var usbDevice = usbRegistry.Device;
                    if (usbDevice != null)
                    {
                        return OpenDeviceInternal(usbDevice);
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Open Samsung device by VID/PID
    /// </summary>
    public bool OpenDevice(int productId)
    {
        var deviceList = UsbDevice.AllDevices;

        foreach (object obj in deviceList)
        {
            if (obj is UsbRegistry usbRegistry)
            {
                if (usbRegistry.Vid == SAMSUNG_VID && usbRegistry.Pid == productId)
                {
                    var usbDevice = usbRegistry.Device;
                    if (usbDevice != null)
                    {
                        return OpenDeviceInternal(usbDevice);
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Open specific USB device
    /// </summary>
    private bool OpenDeviceInternal(UsbDevice usbDevice)
    {
        try
        {
            _device = usbDevice;
            if (_device == null) return false;

            if (!_device.Open())
            {
                CloseDevice();
                return false;
            }

            DeviceConnected?.Invoke(this, new UsbDeviceEventArgs(GetDeviceInfo()));
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"USB Open Error: {ex.Message}");
            CloseDevice();
            return false;
        }
    }

    /// <summary>
    /// Opens device for communication by VID/PID
    /// </summary>
    public bool Open(int vid, int pid)
    {
        try
        {
            var deviceFinder = new UsbDeviceFinder(vid, pid);
            _device = UsbDevice.OpenUsbDevice(deviceFinder);

            if (_device == null)
                return false;

            if (!_device.IsOpen)
                _device.Open();

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to open device: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Close current device
    /// </summary>
    public void CloseDevice()
    {
        if (_device != null)
        {
            if (_device.IsOpen)
                _device.Close();
            _device = null;
        }
    }

    /// <summary>
    /// Sends control transfer to device
    /// </summary>
    public bool ControlTransfer(
        byte requestType,
        byte request,
        ushort value,
        ushort index,
        byte[] buffer,
        out int transferred,
        int timeout = 5000)
    {
        if (_device == null || !_device.IsOpen)
        {
            transferred = 0;
            return false;
        }

        try
        {
            // FIX: Use UsbSetupPacket + 4-parameter signature
            var setupPacket = new UsbSetupPacket(requestType, request, (short)value, (short)index, (short)buffer.Length);

            bool result = _device.ControlTransfer(
                ref setupPacket,
                buffer,
                buffer.Length,
                out transferred);

            return result;
        }
        catch (Exception ex)
        {
            transferred = 0;
            Console.WriteLine($"ControlTransfer failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Control transfer (for Download Mode protocol) - legacy method signature
    /// </summary>
    public int ControlTransfer(byte requestType, byte request, ushort value,
                               ushort index, byte[]? buffer = null, int timeout = 5000)
    {
        if (_device == null) throw new InvalidOperationException("Device not open");

        int transferred = 0;
        if (buffer != null)
        {
            ControlTransfer(requestType, request, value, index, buffer, out transferred, timeout);
        }
        else
        {
            // Empty buffer control transfer
            var setupPacket = new UsbSetupPacket(requestType, request, (short)value, (short)index, 0);
            _device.ControlTransfer(ref setupPacket, Array.Empty<byte>(), 0, out transferred);
        }
        return transferred;
    }

    /// <summary>
    /// Get device information
    /// </summary>
    public BackRabbitUsbDeviceInfo GetDeviceInfo()
    {
        if (_device == null) throw new InvalidOperationException("Device not open");

        var info = _device.Info;
        return new BackRabbitUsbDeviceInfo
        {
            VendorId = (ushort)info.Descriptor.VendorID,
            ProductId = (ushort)info.Descriptor.ProductID,
            SerialNumber = info.SerialString ?? "",
            Manufacturer = info.ManufacturerString ?? "",
            Product = info.ProductString ?? "",
            DeviceMode = GetDeviceMode(info.Descriptor.ProductID),
            IsOpen = _device.IsOpen
        };
    }

    private DeviceMode GetDeviceMode(int productId)
    {
        if (DOWNLOAD_MODE_PIDS.Contains(productId)) return DeviceMode.DownloadMode;
        if (ADB_PIDS.Contains(productId)) return DeviceMode.ADB;
        if (FASTBOOT_PIDS.Contains(productId)) return DeviceMode.Fastboot;
        return DeviceMode.Unknown;
    }

public void Dispose()
    {
        if (!_disposed)
        {
            CloseDevice();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// USB Device information (BACKRabbit specific)
/// </summary>
public class BackRabbitUsbDeviceInfo
{
    public ushort VendorId { get; set; }
    public ushort ProductId { get; set; }
    public string SerialNumber { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string Product { get; set; } = "";
    public DeviceMode DeviceMode { get; set; }
    public bool IsOpen { get; set; }

    public string DisplayName => $"{Product} ({SerialNumber})";
}

/// <summary>
/// Device connection mode
/// </summary>
public enum DeviceMode
{
    Unknown,
    DownloadMode,
    ADB,
    Fastboot,
    Charging
}

/// <summary>
/// USB Device event args
/// </summary>
public class UsbDeviceEventArgs : EventArgs
{
    public BackRabbitUsbDeviceInfo Device { get; set; }

    public UsbDeviceEventArgs(BackRabbitUsbDeviceInfo device)
    {
        Device = device;
    }
}

/// <summary>
/// USB Exception
/// </summary>
public class UsbException : Exception
{
    public UsbException(string message) : base(message) { }
    public UsbException(string message, Exception inner) : base(message, inner) { }
}