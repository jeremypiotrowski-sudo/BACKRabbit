using System.Runtime.InteropServices;
using System.Text;

namespace BACKRabbit.MagiskCore.Structures.Ramdisk;

/// <summary>
/// CPIO newc format header (110 bytes)
/// Used for Android ramdisk archives
/// Magic: "070701" for newc format
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
public struct CpioNewcHeader
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public byte[] magic;     // "070701"
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] ino;       // inode number (hex ASCII)
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] mode;      // file mode (hex ASCII)
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] uid;       // user ID (hex ASCII)
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] gid;       // group ID (hex ASCII)
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] nlink;     // link count (hex ASCII)
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] mtime;     // modification time (hex ASCII)
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] filesize;  // file size in bytes (hex ASCII)
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] maj;       // major device number (hex ASCII)
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] min;       // minor device number (hex ASCII)
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] rmaj;      // redev major (hex ASCII)
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] rmin;      // redev minor (hex ASCII)
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] namesize;  // name size including null (hex ASCII)
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] chksum;    // checksum (hex ASCII)

    public const int SIZE = 110;
}

/// <summary>
/// CPIO constants and helpers
/// </summary>
public static class CpioConstants
{
    public static readonly byte[] MAGIC_NEWC = "070701"u8.ToArray();
    public static readonly byte[] TRAILER = "TRAILER!!!"u8.ToArray();
    public const int HEADER_SIZE = 110;
    public const int ALIGNMENT = 4;
}

/// <summary>
/// Represents a single entry in a CPIO archive
/// </summary>
public class CpioEntry
{
    public string Name { get; set; } = string.Empty;
    public uint Mode { get; set; }
    public uint Uid { get; set; }
    public uint Gid { get; set; }
    public uint Mtime { get; set; }
    public uint FileSize { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Check if entry is a directory
    /// </summary>
    public bool IsDirectory => (Mode & 0x1F000) == 0x4000;   // S_IFDIR = 0o040000

    /// <summary>
    /// Check if entry is a regular file
    /// </summary>
    public bool IsRegularFile => (Mode & 0x1F000) == 0x8000; // S_IFREG = 0o100000

    /// <summary>
    /// Check if entry is a symlink
    /// </summary>
    public bool IsSymlink => (Mode & 0x1F000) == 0xA000;      // S_IFLNK = 0o120000

    /// <summary>
    /// Get data as string (for text files)
    /// </summary>
    public string GetDataAsString() => Encoding.UTF8.GetString(Data);
}

/// <summary>
/// Magisk artifact paths to detect in ramdisk
/// From Magisk source: rootdir.rs + magisk_patch.rs
/// </summary>
public static class MagiskArtifacts
{
    /// <summary>
    /// Known Magisk file/directory paths in ramdisk
    /// </summary>
    public static readonly string[] Paths =
    [
        "overlay.d/sbin/magisk.xz",
        "overlay.d/sbin/stub.xz",
        "overlay.d/sbin/init-ld.xz",
        ".backup/.magisk",
        ".backup/init.xz",
        ".backup/stock_init.xz",
        "ramdisk.cpio.orig",
        "init.magisk.rc",
        "sepolicy.rules",
        "overlay.d/sbin/magiskpolicy.xz",
        "overlay.d/sbin/resetprop.xz"
    ];

    /// <summary>
    /// Patterns that indicate verity/encryption has been disabled
    /// (from Magisk's patch_verity and patch_encryption functions)
    /// </summary>
    public static readonly byte[][] VerityPatterns =
    [
        "verifyatboot"u8.ToArray(),
        "verify"u8.ToArray(),
        "avb_keys"u8.ToArray(),
        "avb"u8.ToArray(),
        "support_scfs"u8.ToArray(),
        "fsverity"u8.ToArray()
    ];

    /// <summary>
    /// Patterns that indicate encryption has been modified
    /// </summary>
    public static readonly byte[][] EncryptionPatterns =
    [
        "forceencrypt"u8.ToArray(),
        "forcefdeorfbe"u8.ToArray(),
        "fileencryption"u8.ToArray()
    ];

    /// <summary>
    /// init.rc patterns that indicate Magisk injection
    /// </summary>
    public static readonly string[] InitRcPatterns =
    [
        "exec u:r:magisk:s0",
        "magisk --post-fs-data",
        "magisk --service",
        "magisk --boot-complete",
        "overlay.d",
        "/dev/magisk"
    ];

    /// <summary>
    /// SELinux-permissive module detection patterns
    /// These indicate the evdenis/selinux_permissive Magisk module
    /// which causes screen dimming on Samsung devices
    /// </summary>
    public static readonly string[] SelinuxPermissivePaths =
    [
        "selinux_permissive",
        "sepolicy.rule",
        "post-fs-data.sh"
    ];

    public static readonly string[] SelinuxPermissiveContentPatterns =
    [
        "setenforce 0",
        "permissive=1",
        "enforcing=0",
        "selinux=permissive"
    ];
}
