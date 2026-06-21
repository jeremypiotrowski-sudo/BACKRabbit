using BACKRabbit.Protocol.Firehose.Rescue;

namespace BACKRabbit.Protocol.Firehose.Tests.Rescue;

public class QFuseDatabaseTests
{
    [Fact]
    public void Database_HasEntries_ForKnownSocs_SDM845()
    {
        var fuses = QFuseDatabase.GetFuses("SDM845");
        Assert.NotNull(fuses);
        Assert.NotEmpty(fuses);
        Assert.True(fuses.Count >= 8, $"SDM845 should have at least 8 fuses, got {fuses.Count}");
    }

    [Fact]
    public void Database_HasEntries_ForKnownSocs_SM8550()
    {
        var fuses = QFuseDatabase.GetFuses("SM8550");
        Assert.NotNull(fuses);
        Assert.NotEmpty(fuses);
        Assert.True(fuses.Count >= 6, $"SM8550 should have at least 6 fuses, got {fuses.Count}");
    }

    [Fact]
    public void Database_HasEntries_ForKnownSocs_MSM8937()
    {
        var fuses = QFuseDatabase.GetFuses("MSM8937");
        Assert.NotNull(fuses);
        Assert.NotEmpty(fuses);
        Assert.True(fuses.Count >= 5, $"MSM8937 should have at least 5 fuses, got {fuses.Count}");
    }

    [Fact]
    public void Database_Lookup_ReturnsCorrectFuseCount_SDM845()
    {
        var fuses = QFuseDatabase.GetFuses("SDM845");
        // SDM845 has 10 fuses defined
        Assert.Equal(10, fuses.Count);
    }

    [Fact]
    public void Database_UnknownSoc_FallsBackToGeneric()
    {
        var fuses = QFuseDatabase.GetFuses("UNKNOWN_SOC_XYZ");
        Assert.NotNull(fuses);
        Assert.NotEmpty(fuses);
        // Generic fallback has 8 fuses
        Assert.Equal(8, fuses.Count);
    }

    [Fact]
    public void Database_NullSoc_FallsBackToGeneric()
    {
        var fuses = QFuseDatabase.GetFuses(null);
        Assert.NotNull(fuses);
        Assert.NotEmpty(fuses);
        Assert.Equal(8, fuses.Count);
    }

    [Fact]
    public void FuseDefinition_Addresses_AreValidHex()
    {
        var fuses = QFuseDatabase.GetFuses("SDM845");
        foreach (var fuse in fuses)
        {
            Assert.True(fuse.Address > 0, $"Fuse {fuse.FuseName} has invalid address 0x{fuse.Address:X8}");
            Assert.True(fuse.Address >= 0x00700000, $"Fuse {fuse.FuseName} address 0x{fuse.Address:X8} is below expected QFPROM range");
        }
    }

    [Fact]
    public void FuseDefinition_BitNumbers_InRange()
    {
        var fuses = QFuseDatabase.GetFuses("SDM845");
        foreach (var fuse in fuses)
        {
            Assert.True(fuse.BitNumber >= 0, $"Fuse {fuse.FuseName} has negative bit number");
            Assert.True(fuse.BitNumber <= 63, $"Fuse {fuse.FuseName} bit number {fuse.BitNumber} exceeds 63");
        }
    }

    [Fact]
    public void GetSocModel_KnownMsmIds_ReturnCorrectModel()
    {
        Assert.Equal("SDM845", QFuseDatabase.GetSocModel(0x008600E1));
        Assert.Equal("SM8550", QFuseDatabase.GetSocModel(0x008700E1));
        Assert.Equal("SM8650", QFuseDatabase.GetSocModel(0x008800E1));
        Assert.Equal("MSM8998", QFuseDatabase.GetSocModel(0x007000E1));
        Assert.Equal("MSM8937", QFuseDatabase.GetSocModel(0x006900E1));
    }

    [Fact]
    public void GetSocModel_UnknownMsmId_ReturnsNull()
    {
        Assert.Null(QFuseDatabase.GetSocModel(0xDEADBEEF));
        Assert.Null(QFuseDatabase.GetSocModel(0));
    }
}