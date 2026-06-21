using System.Text;
using BACKRabbit.MagiskCore.FormatDetection;
using BACKRabbit.MagiskCore.Compression;

// Use the static MagiskArtifacts class from Structures.Ramdisk (not the one in CpioArchive.cs)
using MagiskArtifactsStatic = BACKRabbit.MagiskCore.Structures.Ramdisk.MagiskArtifacts;

namespace BACKRabbit.MagiskCore.RamdiskEditor;

/// <summary>
/// Detects Magisk installation artifacts in a ramdisk
/// Based on Magisk source: rootdir.rs + magisk_patch.rs
/// </summary>
public class MagiskArtifactDetector
{
    /// <summary>
    /// Detect Magisk installation in a CPIO archive
    /// </summary>
    public MagiskDetectionResult Detect(CpioArchive ramdisk)
    {
        var result = new MagiskDetectionResult();

        foreach (var entry in ramdisk.Entries)
        {
            // Check for Magisk artifact paths
foreach (var path in MagiskArtifactsStatic.Paths)
{
    if (entry.Name.EndsWith(path) || entry.Name.Contains("overlay.d/sbin/"))
    {
        result.FoundArtifacts.Add(entry.Name);
        result.IsMagiskInstalled = true;
    }
}

// Check fstab files for verity/encryption patches
            if (entry.Name.StartsWith("fstab.") || entry.Name.Contains("fstab."))
            {
                var content = entry.GetData();
                var contentStr = Encoding.UTF8.GetString(content);
                if (MagiskArtifactsStatic.VerityPatterns.Any(p => contentStr.Contains(Encoding.UTF8.GetString(p))))
                    result.HasVerityPatches = true;
                if (MagiskArtifactsStatic.EncryptionPatterns.Any(p => contentStr.Contains(Encoding.UTF8.GetString(p))))
                    result.HasEncryptionPatches = true;
            }
        }

        // Check for .backup/.magisk config
        var backupConfig = ramdisk.GetEntry(".backup/.magisk");
        if (backupConfig != null)
        {
            result.BackupConfig = ParseBackupConfig(backupConfig.GetData());
            result.HasBackup = true;
        }

        // Check for stock backup (ramdisk.cpio.orig)
        var stockBackup = ramdisk.GetEntry("ramdisk.cpio.orig");
        if (stockBackup != null)
        {
            result.HasFullBackup = true;
        }

        // Check for init backup
        var initBackup = ramdisk.GetEntry(".backup/init.xz");
        if (initBackup != null)
        {
            result.HasInitBackup = true;
        }

        // R3: SELinux-permissive module detection
        // Scans for evdenis/selinux_permissive Magisk module which causes screen dimming
        foreach (var entry in ramdisk.Entries)
        {
            // Check for SELinux-permissive module paths
            foreach (var selinuxPath in MagiskArtifactsStatic.SelinuxPermissivePaths)
            {
                if (entry.Name.Contains(selinuxPath, StringComparison.OrdinalIgnoreCase))
                {
                    result.IsSelinuxPermissive = true;
                    result.SelinuxFindings.Add($"SELinux module path: {entry.Name}");
                }
            }

            // Scan init.rc files for Magisk injection patterns (activates dead InitRcPatterns)
            if (entry.Name.EndsWith("init.rc", StringComparison.OrdinalIgnoreCase) ||
                entry.Name.EndsWith("init.magisk.rc", StringComparison.OrdinalIgnoreCase))
            {
                var content = Encoding.UTF8.GetString(entry.GetData());
                foreach (var pattern in MagiskArtifactsStatic.InitRcPatterns)
                {
                    if (content.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        result.IsMagiskInstalled = true;
                        result.FoundArtifacts.Add($"init.rc pattern: {pattern}");
                    }
                }
            }

            // Scan for SELinux-permissive content patterns
            if (entry.Name.Contains("post-fs-data", StringComparison.OrdinalIgnoreCase) ||
                entry.Name.EndsWith(".sh", StringComparison.OrdinalIgnoreCase) ||
                entry.Name.EndsWith("init.rc", StringComparison.OrdinalIgnoreCase))
            {
                var content = Encoding.UTF8.GetString(entry.GetData());
                foreach (var pattern in MagiskArtifactsStatic.SelinuxPermissiveContentPatterns)
                {
                    if (content.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        result.IsSelinuxPermissive = true;
                        result.SelinuxFindings.Add($"SELinux pattern '{pattern}' in {entry.Name}");
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Parse .backup/.magisk config file
    /// Format: KEY=value pairs
    /// </summary>
    private MagiskBackupConfig ParseBackupConfig(byte[] data)
    {
        var config = new MagiskBackupConfig();
        var lines = Encoding.UTF8.GetString(data).Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var parts = line.Split('=', 2);
            if (parts.Length != 2) continue;

            var key = parts[0].Trim();
            var value = parts[1].Trim();

            switch (key)
            {
                case "KEEPVERITY":
                    config.KeepVerity = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    break;
                case "KEEPFORCEENCRYPT":
                    config.KeepForceEncrypt = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    break;
                case "SHA1":
                    config.Sha1 = value;
                    break;
                case "PREINITDEVICE":
                    config.PreinitDevice = value;
                    break;
                case "MAGISK_VERSION":
                    config.MagiskVersion = value;
                    break;
            }
        }

        return config;
    }

    /// <summary>
    /// Restore ramdisk from backup (preferred method)
    /// Priority: 1: 1) ramdisk.cpio.orig, 2) .backup/init.xz
    /// </summary>
    public CpioArchive RestoreFromBackup(CpioArchive ramdisk)
    {
        // Option 1: Use ramdisk.cpio.orig (full restore - CLEANEST)
        var orig = ramdisk.GetEntry("ramdisk.cpio.orig");
        if (orig != null)
        {
            return CpioArchive.Parse(orig.GetData());
        }

// Option 2: Use .backup/init.xz (restore init only)
        var backupInit = ramdisk.GetEntry(".backup/init.xz");
        if (backupInit != null)
        {
            using var compression = new CompressionEngine();
            var decompressed = compression.Decompress(backupInit.Data);

            var newRamdisk = ramdisk.Clone();
            newRamdisk.ReplaceEntry("init", decompressed);
            newRamdisk.RemoveDirectory("overlay.d");
            newRamdisk.RemoveDirectory(".backup");
            newRamdisk.RemoveEntry("ramdisk.cpio.orig");
            return newRamdisk;
        }

        // No backup available - fall back to surgical removal
        return SurgicalRemoval(ramdisk);
    }

    /// <summary>
    /// Surgical removal of Magisk when no backup exists
    /// This is less reliable than backup restoration
    /// </summary>
    public CpioArchive SurgicalRemoval(CpioArchive ramdisk)
    {
        var cleaned = ramdisk.Clone();

        // Remove Magisk directories and files
        cleaned.RemoveDirectory("overlay.d");
        cleaned.RemoveDirectory(".backup");
        cleaned.RemoveEntry("ramdisk.cpio.orig");
        cleaned.RemoveEntry("init.magisk.rc");
        cleaned.RemoveEntry("sepolicy.rules");

// Patch fstab files - restore verity/encryption flags
        foreach (var entry in cleaned.Entries.Where(e => e.Name.StartsWith("fstab.")))
        {
            var data = entry.GetData();
            data = RemovePatterns(data, MagiskArtifactsStatic.VerityPatterns);
            data = RemovePatterns(data, MagiskArtifactsStatic.EncryptionPatterns);
            
            // CRITICAL: Restore required flags that Magisk removes
            data = RestoreFstabFlags(data, null);
            entry.SetData(data);
        }

        // Restore init.rc - remove magisk service entries
        var initRc = cleaned.GetEntry("init.rc");
        if (initRc != null)
        {
            var lines = Encoding.UTF8.GetString(initRc.GetData()).Split('\n');
            var filtered = lines.Where(l =>
                !l.Contains("magisk", StringComparison.OrdinalIgnoreCase) &&
                !l.Contains("overlay.d", StringComparison.OrdinalIgnoreCase) &&
                !l.Contains("exec u:r:magisk:s0")).ToArray();
            initRc.SetData(Encoding.UTF8.GetBytes(string.Join('\n', filtered)));
        }

        return cleaned;
    }

    /// <summary>
    /// Surgical removal with explicit fstab flag restoration
    /// </summary>
    /// <param name="ramdisk">Ramdisk to clean</param>
    /// <param name="originalFstabBackup">Optional original fstab backup for exact flag restoration</param>
    public CpioArchive SurgicalRemovalWithFlagRestoration(CpioArchive ramdisk, byte[]? originalFstabBackup = null)
    {
        var cleaned = ramdisk.Clone();

        // Remove Magisk directories and files
        cleaned.RemoveDirectory("overlay.d");
        cleaned.RemoveDirectory(".backup");
        cleaned.RemoveEntry("ramdisk.cpio.orig");
        cleaned.RemoveEntry("init.magisk.rc");
        cleaned.RemoveEntry("sepolicy.rules");

// Patch fstab files - restore verity/encryption flags AND add back required flags
        foreach (var entry in cleaned.Entries.Where(e => e.Name.StartsWith("fstab.")))
        {
            var data = entry.GetData();
            data = RemovePatterns(data, MagiskArtifactsStatic.VerityPatterns);
            data = RemovePatterns(data, MagiskArtifactsStatic.EncryptionPatterns);
            
            // CRITICAL: Restore required flags that Magisk removes
            data = RestoreFstabFlags(data, originalFstabBackup);
            entry.SetData(data);
        }

        // Restore init.rc - remove magisk service entries
        var initRc = cleaned.GetEntry("init.rc");
        if (initRc != null)
        {
            var lines = Encoding.UTF8.GetString(initRc.GetData()).Split('\n');
            var filtered = lines.Where(l =>
                !l.Contains("magisk", StringComparison.OrdinalIgnoreCase) &&
                !l.Contains("overlay.d", StringComparison.OrdinalIgnoreCase) &&
                !l.Contains("exec u:r:magisk:s0")).ToArray();
            initRc.SetData(Encoding.UTF8.GetBytes(string.Join('\n', filtered)));
        }

        return cleaned;
    }

    /// <summary>
    /// Restores critical boot flags in fstab entries after Magisk removal.
    /// Magisk removes 'avb', 'verify', and 'fsverity' flags from fstab mount options.
    /// </summary>
    /// <param name="fstabData">Current fstab content</param>
    /// <param name="originalBackup">Optional original fstab backup for exact flag restoration</param>
    /// <returns>Modified fstab content with flags restored</returns>
    public static byte[] RestoreFstabFlags(byte[] fstabData, byte[]? originalBackup)
    {
        if (fstabData == null || fstabData.Length == 0)
            return fstabData;

        var content = Encoding.UTF8.GetString(fstabData);
        var lines = content.Split('\n');
        var result = new List<string>();
        var requiredFlags = new[] { "avb", "verify", "fsverity" };

        // If we have original backup, extract flags from it
        var originalFlags = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        if (originalBackup != null && originalBackup.Length > 0)
        {
            try
            {
                var originalContent = Encoding.UTF8.GetString(originalBackup);
                foreach (var line in originalContent.Split('\n'))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                        continue;

                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 4)
                    {
                        var mountPoint = parts[1];
                        var flags = parts.Length > 3 ? parts[3].Split(',', StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();
                        originalFlags[mountPoint] = flags.ToHashSet(StringComparer.OrdinalIgnoreCase);
                    }
                }
            }
            catch
            {
                // Fall back to default flag restoration
            }
        }

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                result.Add(line);
                continue;
            }

            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4)
            {
                result.Add(line);
                continue;
            }

            // Parse fstab entry: device mount_point fstype flags
            var device = parts[0];
            var mountPoint = parts[1];
            var fstype = parts[2];
            var flags = parts.Length > 3 ? parts[3].Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase) : new HashSet<string>();
            var rest = parts.Length > 4 ? string.Join(" ", parts[4..]) : string.Empty;

            // Remove Magisk-injected flags
            var magiskFlags = new[] { "magisk", "wait", "recoveryonly", "earlycon", "nomagic" };
            foreach (var mf in magiskFlags)
                flags.Remove(mf);

            // Restore required flags from original or defaults
            if (originalFlags.TryGetValue(mountPoint, out var origFlags))
            {
                foreach (var flag in requiredFlags.Where(f => origFlags.Contains(f)))
                    flags.Add(flag);
            }
            else
            {
                // Default flag restoration based on mount point
                if (mountPoint == "/system" || mountPoint == "/vendor" || mountPoint == "/product" || mountPoint == "/system_ext")
                {
                    flags.Add("avb");
                    flags.Add("verify");
                }
                if (mountPoint == "/data")
                {
                    flags.Add("fsverity");
                }
                // Ensure verify is present for all non-data partitions
                if (mountPoint != "/data" && !flags.Contains("verify"))
                {
                    flags.Add("verify");
                }
            }

            // Reconstruct line
            var newLine = $"{device}\t{mountPoint}\t{fstype}\t{string.Join(",", flags.OrderBy(f => f))}";
            if (!string.IsNullOrWhiteSpace(rest))
                newLine += $" {rest}";

            result.Add(newLine);
        }

        return Encoding.UTF8.GetBytes(string.Join('\n', result));
    }

    /// <summary>
    /// Remove pattern bytes from data
    /// </summary>
    private byte[] RemovePatterns(byte[] data, byte[][] patterns)
    {
        var result = data;
        foreach (var pattern in patterns)
        {
            var dataStr = Encoding.UTF8.GetString(result);
            var lines = dataStr.Split('\n');
            var filtered = lines.Where(l => !l.Contains(Encoding.UTF8.GetString(pattern))).ToArray();
            result = Encoding.UTF8.GetBytes(string.Join('\n', filtered));
        }
        return result;
    }
}

/// <summary>
/// Result of Magisk detection
/// </summary>
public class MagiskDetectionResult
{
    public bool IsMagiskInstalled { get; set; }
    public List<string> FoundArtifacts { get; set; } = new();
    public bool HasBackup { get; set; }
    public bool HasFullBackup { get; set; }  // ramdisk.cpio.orig
    public bool HasInitBackup { get; set; }  // .backup/init.xz
    public MagiskBackupConfig? BackupConfig { get; set; }
    public bool HasVerityPatches { get; set; }
    public bool HasEncryptionPatches { get; set; }
    public string? DetectedVersion { get; set; }
    public bool IsSelinuxPermissive { get; set; }
    public List<string> SelinuxFindings { get; set; } = new();

    public string Summary
    {
        get
        {
            if (!IsMagiskInstalled) return "No Magisk installation detected";
            
            var sb = new StringBuilder();
            sb.AppendLine($"Magisk detected: {DetectedVersion ?? "unknown version"}");
            sb.AppendLine($"Artifacts found: {FoundArtifacts.Count}");
            if (HasFullBackup) sb.AppendLine("Full backup available (ramdisk.cpio.orig)");
            if (HasInitBackup) sb.AppendLine("Init backup available (.backup/init.xz)");
            if (HasVerityPatches) sb.AppendLine("Verity patches detected in fstab");
            if (HasEncryptionPatches) sb.AppendLine("Encryption patches detected in fstab");
            return sb.ToString();
        }
    }
}

/// <summary>
/// Magisk .backup/.magisk config
/// </summary>
public class MagiskBackupConfig
{
    public bool KeepVerity { get; set; }
    public bool KeepForceEncrypt { get; set; }
    public string? Sha1 { get; set; }
    public string? PreinitDevice { get; set; }
    public string? MagiskVersion { get; set; }
}