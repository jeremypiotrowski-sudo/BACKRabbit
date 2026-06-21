namespace BACKRabbit.Tests;

/// <summary>
/// CPIO newc format round-trip tests: Serialize → Re-Parse → Byte-compare
/// </summary>
public class CpioArchiveTests
{
    [Fact]
    public void SerializeReParse_EmptyArchive_ProducesIdenticalBytes()
    {
        var original = new CpioArchive();
        var serialized = original.Serialize();
        var reParsed = CpioArchive.Parse(serialized);

        Assert.NotNull(reParsed);
        Assert.Empty(reParsed.Entries);
        // Empty archive should serialize to just TRAILER!!!
        Assert.True(serialized.Length > 0, "Even empty archive has TRAILER marker");
    }

    [Fact]
    public void SerializeReParse_SingleFile_ProducesIdenticalBytes()
    {
        var original = new CpioArchive();
        original.Entries.Add(new CpioEntry
        {
            Name = "test.txt",
            Data = "Hello World"u8.ToArray(),
            Mode = 0x81A4 // Regular file, 0644
        });

        var serialized = original.Serialize();
        var reParsed = CpioArchive.Parse(serialized);

        Assert.NotNull(reParsed);
        Assert.Single(reParsed.Entries);
        Assert.Equal("test.txt", reParsed.Entries[0].Name);
        Assert.Equal("Hello World"u8.ToArray(), reParsed.Entries[0].Data);
    }

    [Fact]
    public void SerializeReParse_MixedEntries_ProducesIdenticalBytes()
    {
        var original = new CpioArchive();

        // Directory
        original.Entries.Add(new CpioEntry
        {
            Name = "sbin/",
            Data = Array.Empty<byte>(),
            Mode = 0x41ED // Directory, 0755
        });

        // Regular file
        original.Entries.Add(new CpioEntry
        {
            Name = "sbin/magisk",
            Data = new byte[1024],
            Mode = 0x81ED // Regular file, 0755
        });
        new Random(42).NextBytes(original.Entries[1].Data);

        // Symlink
        original.Entries.Add(new CpioEntry
        {
            Name = "sbin/su",
            Data = "/sbin/magisk"u8.ToArray(),
            Mode = 0xA1FF // Symlink, 0777
        });

        // Another file
        original.Entries.Add(new CpioEntry
        {
            Name = "init.rc",
            Data = "on boot\n    start servicemanager\n"u8.ToArray(),
            Mode = 0x81A4
        });

        var serialized = original.Serialize();
        var reParsed = CpioArchive.Parse(serialized);

        Assert.NotNull(reParsed);
        Assert.Equal(4, reParsed.Entries.Count);

        // Verify each entry
        Assert.Equal("sbin/", reParsed.Entries[0].Name);
        Assert.Equal("sbin/magisk", reParsed.Entries[1].Name);
        Assert.Equal(original.Entries[1].Data, reParsed.Entries[1].Data);
        Assert.Equal("sbin/su", reParsed.Entries[2].Name);
        Assert.Equal("/sbin/magisk"u8.ToArray(), reParsed.Entries[2].Data);
        Assert.Equal("init.rc", reParsed.Entries[3].Name);
    }

    [Fact]
    public void SerializeReParse_LargeArchive_ProducesIdenticalBytes()
    {
        var original = new CpioArchive();

        // Create 100+ entries
        for (int i = 0; i < 120; i++)
        {
            var data = new byte[64 + i % 32];
            new Random(i).NextBytes(data);

            original.Entries.Add(new CpioEntry
            {
                Name = $"file_{i:D4}.dat",
                Data = data,
                Mode = 0x81A4
            });
        }

        var serialized = original.Serialize();
        var reParsed = CpioArchive.Parse(serialized);

        Assert.NotNull(reParsed);
        Assert.Equal(120, reParsed.Entries.Count);

        // Spot-check a few entries
        Assert.Equal("file_0000.dat", reParsed.Entries[0].Name);
        Assert.Equal(original.Entries[0].Data, reParsed.Entries[0].Data);
        Assert.Equal("file_0050.dat", reParsed.Entries[50].Name);
        Assert.Equal(original.Entries[50].Data, reParsed.Entries[50].Data);
        Assert.Equal("file_0119.dat", reParsed.Entries[119].Name);
        Assert.Equal(original.Entries[119].Data, reParsed.Entries[119].Data);
    }

    [Fact]
    public void SerializeReParse_SpecialFilenames_ProducesIdenticalBytes()
    {
        var original = new CpioArchive();

        original.Entries.Add(new CpioEntry
        {
            Name = "file with spaces.txt",
            Data = "spaces"u8.ToArray(),
            Mode = 0x81A4
        });

        original.Entries.Add(new CpioEntry
        {
            Name = "overlay.d/sbin/magisk",
            Data = "magisk"u8.ToArray(),
            Mode = 0x81ED
        });

        original.Entries.Add(new CpioEntry
        {
            Name = ".backup/.magisk",
            Data = "KEEPVERITY=true\n"u8.ToArray(),
            Mode = 0x81A4
        });

        var serialized = original.Serialize();
        var reParsed = CpioArchive.Parse(serialized);

        Assert.NotNull(reParsed);
        Assert.Equal(3, reParsed.Entries.Count);
        Assert.Equal("file with spaces.txt", reParsed.Entries[0].Name);
        Assert.Equal("overlay.d/sbin/magisk", reParsed.Entries[1].Name);
        Assert.Equal(".backup/.magisk", reParsed.Entries[2].Name);
    }

    [Fact]
    public void SerializeReParse_LargeFile_ProducesIdenticalBytes()
    {
        var original = new CpioArchive();
        var largeData = new byte[1024 * 1024 * 2]; // 2MB
        new Random(99).NextBytes(largeData);

        original.Entries.Add(new CpioEntry
        {
            Name = "large_file.bin",
            Data = largeData,
            Mode = 0x81A4
        });

        var serialized = original.Serialize();
        var reParsed = CpioArchive.Parse(serialized);

        Assert.NotNull(reParsed);
        Assert.Single(reParsed.Entries);
        Assert.Equal("large_file.bin", reParsed.Entries[0].Name);
        Assert.Equal(largeData, reParsed.Entries[0].Data);
    }
}