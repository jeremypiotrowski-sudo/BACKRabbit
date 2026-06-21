using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace BACKRabbit.Protocol.Firehose;

public class EdlDeviceInfo
{
    public string DevicePath { get; init; } = "";
    public int VendorId { get; init; }
    public int ProductId { get; init; }
    public string Description { get; init; } = "";
    public string TransportType { get; init; } = "usb";
    public bool IsEdl => FirehoseDeviceDetector.EdlProductIds.Contains(ProductId);
    public bool IsCrashDump => ProductId == 0x900E;
    public override string ToString() => $"EDLDevice({Description}, PID=0x{ProductId:X4})";
}

public class FirehoseDeviceDetector
{
    public static readonly HashSet<int> EdlProductIds = new() { 0x9008, 0x900E, 0x901D };
    public const int QualcommVendorId = 0x05C6;

    public static List<EdlDeviceInfo> EnumerateDevices()
    {
        // WMI-based detection requires System.Management package.
        // For now, return empty list. Real implementation will use LibUsbDotNet.
        return new List<EdlDeviceInfo>();
    }
}
