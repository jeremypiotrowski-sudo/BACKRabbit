using BACKRabbit.MagiskCore.Parser;
using BACKRabbit.MagiskCore.RamdiskEditor;
using BACKRabbit.MagiskCore.SamsungKernel;
using BACKRabbit.MagiskCore.Repacker;
using BACKRabbit.MagiskCore.Compression;
using BACKRabbit.MagiskCore.FormatDetection;

using AvbRestorerClass = BACKRabbit.MagiskCore.AvbRestorer.AvbRestorer;
using AvbRestoreResult = BACKRabbit.MagiskCore.AvbRestorer.AvbRestoreResult;

namespace BACKRabbit.MagiskCore.Services;

/// <summary>
/// Main Magisk Uninstallation Service
/// 
/// Provides end-to-end Magisk removal for Samsung devices (S24/S25/Z Fold 7)
/// with init_boot partition support (GKI 2.0).
/// 
/// UNINSTALLATION METHODS (in order of preference):
/// 
/// 1. STOCK FIRMWARE FLASH (Recommended - 100% reliable)
///    - Download official Samsung firmware
///    - Extract stock init_boot/boot image
///    - Flash via Odin/Download Mode
///    - Result: Complete factory state
/// 
/// 2. BACKUP RESTORATION (If Magisk created backup)
///    - Magisk saves: ramdisk.cpio.orig, .backup/init.xz
///    - Restore from backup
///    - Repack and flash
///    - Result: Pre-Magisk state
/// 
/// 3. SURGICAL REMOVAL (Last resort - may not work on all devices)
///    - Remove Magisk artifacts manually
///    - Patch fstab files
///    - Restore AVB flags
///    - Result: May have residual modifications
/// </summary>
public class MagiskUninstaller
{
    private readonly BootImageParser _bootParser = new();
    private readonly BootImageRepacker _repacker = new();
    private readonly MagiskArtifactDetector _detector = new();
    private readonly AvbRestorerClass _avbRestorer = new();
    private readonly SamsungKernelPatcher _kernelPatcher = new();

    /// <summary>
    /// Complete Magisk uninstallation workflow
    /// </summary>
    public async Task<UninstallResult> UninstallAsync(UninstallOptions options)
    {
        var result = new UninstallResult();
        var steps = new List<string>();

        try
        {
            // Step 1: Load boot/init_boot image
            steps.Add("Loading boot image...");
            var bootImage = _bootParser.Parse(options.BootImagePath);
            result.OriginalImage = bootImage;

            // Step 2: Extract and analyze ramdisk
            steps.Add("Extracting ramdisk...");
            var ramdiskArchive = _bootParser.ExtractRamdiskArchive(bootImage);
            var detection = _detector.Detect(ramdiskArchive);
            result.DetectionResult = detection;

            if (!detection.IsMagiskInstalled)
            {
                result.Success = true;
                result.Message = "No Magisk installation detected";
                result.Steps = steps;
                return result;
            }

            // Step 3: Choose restoration method
            CpioArchive? restoredRamdisk = null;
            string method = "";

            if (detection.HasFullBackup && options.PreferBackupRestore)
            {
                // Method A: Full backup restoration
                steps.Add("Using full backup restoration (ramdisk.cpio.orig)...");
                restoredRamdisk = _detector.RestoreFromBackup(ramdiskArchive);
                method = "Backup Restoration (Full)";
            }
            else if (detection.HasInitBackup && options.PreferBackupRestore)
            {
                // Method B: Init backup restoration
                steps.Add("Using init backup restoration (.backup/init.xz)...");
                restoredRamdisk = _detector.RestoreFromBackup(ramdiskArchive);
                method = "Backup Restoration (Init)";
            }
            else if (options.ForceStockFirmware)
            {
                // Method C: Stock firmware (requires external firmware)
                steps.Add("Stock firmware method selected - requires firmware download...");
                result.RequiresStockFirmware = true;
                result.Message = "Stock firmware required. Use FirmwareDownloader to obtain.";
                result.Steps = steps;
                return result;
            }
            else
            {
                // Method D: Surgical removal
                steps.Add("No backup available - using surgical removal...");
                restoredRamdisk = _detector.SurgicalRemoval(ramdiskArchive);
                method = "Surgical Removal";
            }

            // Step 4: Restore kernel (if patched)
            byte[]? restoredKernel = null;
            if (options.RestoreKernel && bootImage.KernelSize > 0)
            {
                steps.Add("Analyzing kernel for patches...");
                var kernel = _bootParser.ExtractKernel(bootImage);
                var kernelAnalysis = _kernelPatcher.Analyze(kernel);
                
                if (!kernelAnalysis.IsStock)
                {
                    steps.Add("Restoring stock kernel state...");
                    restoredKernel = _kernelPatcher.RestoreStock(kernel, kernelAnalysis);
                    result.KernelAnalysis = kernelAnalysis;
                }
            }

            // Step 5: Repack boot image
            steps.Add("Repacking boot image...");
            byte[] newRamdisk;
            if (restoredRamdisk != null)
            {
                var rawRamdisk = restoredRamdisk.Serialize();
using var compression = new CompressionEngine();
                newRamdisk = compression.Compress(rawRamdisk, CompressionEngine.CompressionFormat.Gzip);
            }
            else
            {
                newRamdisk = _bootParser.ExtractRamdisk(bootImage);
            }

            var repackedImage = _repacker.Repack(bootImage, newRamdisk, restoredKernel);
            result.RepackedImage = repackedImage;

            // Step 6: Restore AVB flags
            steps.Add("Restoring AVB verification flags...");
            var avbResult = _avbRestorer.RestoreVerificationFlags(repackedImage);
            if (avbResult.Success && avbResult.PatchedImage != null)
            {
                result.RepackedImage = avbResult.PatchedImage;
                result.AvbRestored = true;
            }
            result.AvbResult = avbResult;

            // Step 7: Generate output
            steps.Add("Generating output...");
            if (options.OutputPath != null)
            {
                File.WriteAllBytes(options.OutputPath, result.RepackedImage);
                result.OutputPath = options.OutputPath;
            }

            result.Success = true;
            result.Method = method;
            result.Message = $"Magisk uninstalled successfully using {method}";
            result.Steps = steps;

            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Uninstallation failed: {ex.Message}";
            result.Steps = steps;
            result.Exception = ex;
            return result;
        }
    }

    /// <summary>
    /// Detect Magisk installation without modifying
    /// </summary>
    public MagiskDetectionResult Detect(string bootImagePath)
    {
        var bootImage = _bootParser.Parse(bootImagePath);
        var ramdiskArchive = _bootParser.ExtractRamdiskArchive(bootImage);
        return _detector.Detect(ramdiskArchive);
    }

    /// <summary>
    /// Analyze kernel for Samsung security patches
    /// </summary>
    public KernelAnalysisResult AnalyzeKernel(string bootImagePath)
    {
        var bootImage = _bootParser.Parse(bootImagePath);
        var kernel = _bootParser.ExtractKernel(bootImage);
        return _kernelPatcher.Analyze(kernel);
    }

    /// <summary>
    /// Check AVB status
    /// </summary>
    public AvbRestoreResult CheckAvbStatus(string bootImagePath)
    {
        var data = File.ReadAllBytes(bootImagePath);
        return _avbRestorer.RestoreVerificationFlags(data);
    }
}

/// <summary>
/// Uninstallation options
/// </summary>
public class UninstallOptions
{
    /// <summary>
    /// Path to boot/init_boot image
    /// </summary>
    public string BootImagePath { get; set; } = "";

    /// <summary>
    /// Output path for cleaned image (null = memory only)
    /// </summary>
    public string? OutputPath { get; set; }

    /// <summary>
    /// Prefer backup restoration over surgical removal
    /// </summary>
    public bool PreferBackupRestore { get; set; } = true;

    /// <summary>
    /// Require stock firmware (most reliable method)
    /// </summary>
    public bool ForceStockFirmware { get; set; } = false;

    /// <summary>
    /// Attempt to restore stock kernel state
    /// </summary>
    public bool RestoreKernel { get; set; } = true;

    /// <summary>
    /// Create backup before uninstalling
    /// </summary>
    public bool CreateBackup { get; set; } = true;
}

/// <summary>
/// Uninstallation result
/// </summary>
public class UninstallResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string? Method { get; set; }
    public List<string> Steps { get; set; } = new();
    public MagiskDetectionResult? DetectionResult { get; set; }
    public KernelAnalysisResult? KernelAnalysis { get; set; }
    public AvbRestoreResult? AvbResult { get; set; }
    public bool AvbRestored { get; set; }
    public BootImage? OriginalImage { get; set; }
    public byte[]? RepackedImage { get; set; }
    public string? OutputPath { get; set; }
    public bool RequiresStockFirmware { get; set; }
    public Exception? Exception { get; set; }
}