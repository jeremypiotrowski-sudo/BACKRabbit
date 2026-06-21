using BACKRabbit.Protocol.Firehose.Rescue;
using System.Text.Json;

namespace BACKRabbit.Protocol.Firehose.Tests.Rescue;

public class RescueReportTests
{
    [Fact]
    public void RescueReport_ToJson_ProducesValidJson()
    {
        var report = new RescueReport
        {
            Device = new DeviceInfo { MsmId = 0x008600E1, SocModel = "SDM845", StorageType = "ufs" },
            Timestamp = new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc),
            Verdict = OverallVerdict.Clean,
        };
        report.Partitions.Add(new PartitionDiagnosis
        {
            PartitionName = "boot_a",
            Lun = 0,
            Status = "Normal",
            ActualHash = "abcdef1234567890",
        });

        var json = report.ToJson();

        Assert.NotNull(json);
        Assert.Contains("SDM845", json);
        Assert.Contains("boot_a", json);
        Assert.Contains("\"Verdict\": 0", json);

        // Verify it's valid JSON by deserializing
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("Device", out _));
        Assert.True(root.TryGetProperty("Verdict", out _));
        Assert.True(root.TryGetProperty("Partitions", out _));
    }

    [Fact]
    public void RescueReport_PrintSummary_DoesNotThrow()
    {
        var report = new RescueReport
        {
            Device = new DeviceInfo { MsmId = 0x008600E1, SocModel = "SDM845", StorageType = "ufs" },
            Verdict = OverallVerdict.Tampered,
        };
        report.Partitions.Add(new PartitionDiagnosis
        {
            PartitionName = "devinfo",
            Status = "Tampered",
            Anomalies = new List<string> { "bootloader lock bit set" },
        });
        report.FuseAudit = new QFuseAuditResult
        {
            TotalBlown = 3,
            TotalAvailable = 8,
            Fuses = new List<QFuseStatus>
            {
                new() { FuseName = "SECURE_BOOT_EN", IsBlown = true, Implication = "Secure boot locked" },
            },
        };
        report.Recommendations.Add("Restore devinfo from backup");

        // Should not throw
        var ex = Record.Exception(() => report.PrintSummary());
        Assert.Null(ex);
    }

    [Fact]
    public void PartitionDiagnosis_Statuses_AreCorrect()
    {
        var normal = new PartitionDiagnosis { PartitionName = "boot_a", Status = "Normal" };
        var tampered = new PartitionDiagnosis { PartitionName = "boot_b", Status = "Tampered" };
        var missing = new PartitionDiagnosis { PartitionName = "recovery", Status = "Missing" };
        var unknown = new PartitionDiagnosis { PartitionName = "xbl", Status = "Unknown" };

        Assert.Equal("Normal", normal.Status);
        Assert.Equal("Tampered", tampered.Status);
        Assert.Equal("Missing", missing.Status);
        Assert.Equal("Unknown", unknown.Status);
    }

    [Fact]
    public void QFuseStatus_Blown_ImplicationReturned()
    {
        var fuse = new QFuseStatus
        {
            FuseName = "OEM_SECURE_BOOT1_AUTH_EN",
            Address = 0x00780020,
            BitNumber = 0,
            IsBlown = true,
            Description = "Secure boot authentication enabled",
            Implication = "Device will only boot signed images. Cannot load unsigned code.",
        };

        Assert.True(fuse.IsBlown);
        Assert.Equal("OEM_SECURE_BOOT1_AUTH_EN", fuse.FuseName);
        Assert.Contains("signed images", fuse.Implication);
    }

    [Fact]
    public void RestoreAction_Verified_TrueWhenHashesMatch()
    {
        var action = new RestoreAction
        {
            PartitionName = "devinfo",
            Action = "Flashed",
            SourceFile = "backup/devinfo.img",
            PreRestoreHash = "abc123",
            PostRestoreHash = "abc123",
            Verified = true,
        };

        Assert.True(action.Verified);
        Assert.Equal("Flashed", action.Action);
        Assert.Equal("abc123", action.PreRestoreHash);
        Assert.Equal("abc123", action.PostRestoreHash);
    }

    [Fact]
    public void MagiskRemovalResult_MagiskFound_TrueWhenArtifactsPresent()
    {
        var result = new MagiskRemovalResult
        {
            BootPartition = "boot_a",
            MagiskFound = true,
            MagiskRemoved = true,
            OriginalHash = "def456",
            CleanHash = "789abc",
            VbmetaRestored = true,
        };

        Assert.True(result.MagiskFound);
        Assert.True(result.MagiskRemoved);
        Assert.True(result.VbmetaRestored);
        Assert.Equal("boot_a", result.BootPartition);
        Assert.NotEqual(result.OriginalHash, result.CleanHash);
    }
}