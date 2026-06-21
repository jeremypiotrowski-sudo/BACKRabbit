using BACKRabbit.Protocol.Firehose;
using System.Buffers.Binary;
using Xunit;

namespace BACKRabbit.Protocol.Firehose.Tests;

public class SaharaPacketTests
{
    [Fact]
    public void Serialize_RoundTrip_PreservesData()
    {
        var payload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var packet = new SaharaPacket(0x01, payload);
        var serialized = packet.Serialize();
        var parsed = SaharaPacket.Parse(serialized);
        Assert.Equal(0x01u, parsed.Command);
        Assert.Equal(payload, parsed.Payload.ToArray());
    }

    [Fact]
    public void Parse_TooShort_Throws()
    {
        var data = new byte[] { 0x00, 0x00, 0x00, 0x00 };
        Assert.Throws<SaharaProtocolException>(() => SaharaPacket.Parse(data));
    }
}

public class SaharaChipInfoTests
{
    [Fact]
    public void FromHelloRequest_UnfusedDevice()
    {
        var payload = new byte[48];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), 0x40000);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12, 4), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16, 4), 0x008600E1);
        var packet = new SaharaPacket(0x00, payload);
        var info = SaharaChipInfo.FromHelloRequest(packet);
        Assert.Equal(2u, info.Version);
        Assert.Equal(0x008600E1u, info.MsmId);
        Assert.False(info.IsFused);
    }

    [Fact]
    public void FromHelloRequest_FusedDevice()
    {
        var payload = new byte[48];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), 0x40000);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12, 4), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16, 4), 0x008600E1);
        new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x11, 0x22 }.CopyTo(payload.AsSpan(24, 8));
        var packet = new SaharaPacket(0x00, payload);
        var info = SaharaChipInfo.FromHelloRequest(packet);
        Assert.True(info.IsFused);
    }
}

public class SaharaStateMachineTests
{
    [Fact]
    public void ValidTransitions_ProceedWithoutError()
    {
        var sm = new SaharaStateMachine();
        sm.SetChipInfo(new SaharaChipInfo { Version = 2 });
        sm.TransitionTo(SaharaState.HelloSent);
        sm.TransitionTo(SaharaState.ImageUploading);
        sm.TransitionTo(SaharaState.ImageUploadComplete);
        sm.TransitionTo(SaharaState.CommandMode);
        sm.TransitionTo(SaharaState.Done);
        Assert.Equal(SaharaState.Done, sm.CurrentState);
    }

    [Fact]
    public void InvalidTransition_Throws()
    {
        var sm = new SaharaStateMachine();
        Assert.Throws<SaharaProtocolException>(() => sm.TransitionTo(SaharaState.CommandMode));
    }
}

public class LoaderDatabaseTests
{
    [Fact]
    public void FromFile_ParsesCorrectly()
    {
        var entry = LoaderEntry.FromFile("008600E1_AABBCCDDEEFF1122_SDM845.bin");
        Assert.NotNull(entry);
        Assert.Equal(0x008600E1u, entry!.MsmId);
    }
}

public class FirehoseDeviceDetectorTests
{
    [Fact]
    public void EdlProductIds_Contains9008()
    {
        Assert.Contains(0x9008, FirehoseDeviceDetector.EdlProductIds);
    }

    [Fact]
    public void EdlDeviceInfo_IsEdl_DetectsCorrectly()
    {
        var edl = new EdlDeviceInfo { ProductId = 0x9008 };
        Assert.True(edl.IsEdl);
    }
}
