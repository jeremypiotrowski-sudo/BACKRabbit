import os

f = os.path.join(os.path.dirname(__file__), 'BACKRabbit.Protocol.Firehose', 'Rescue', 'PartitionRestorer.cs')
with open(f, 'r', encoding='utf-8') as fh:
    c = fh.read()

# Add a _dryRun field and update constructor
c = c.replace(
    '    private readonly IFirehoseClient _client;\n    private readonly string _backupDir;\n    private readonly RescueReport _report;\n    private readonly bool _force;',
    '    private readonly IFirehoseClient _client;\n    private readonly string _backupDir;\n    private readonly RescueReport _report;\n    private readonly bool _force;\n    private readonly bool _dryRun;'
)

c = c.replace(
    '    public PartitionRestorer(IFirehoseClient client, string backupDir, RescueReport report, bool force = false)\n    {\n        _client = client;\n        _backupDir = backupDir;\n        _report = report;\n        _force = force;\n    }',
    '    public PartitionRestorer(IFirehoseClient client, string backupDir, RescueReport report, bool force = false, bool dryRun = false)\n    {\n        _client = client;\n        _backupDir = backupDir;\n        _report = report;\n        _force = force;\n        _dryRun = dryRun;\n    }'
)

method = '''    /// <summary>
    /// Sparse repair: for each MISMATCH partition in the audit, write ONLY the
    /// differing sectors from the stock image. Verify every written sector after write.
    /// In dry-run mode: log planned writes but make ZERO WritePartitionBlocksAsync calls.
    /// CONVENTION: DifferingSectorAddresses stores byte offsets; we convert to sector
    /// numbers by dividing by 512 before passing to WritePartitionBlocksAsync.
    /// </summary>
    public async Task<SparseRepairResult> SparseRepairAsync(
        FullGptAuditResult audit, CancellationToken ct = default)
    {
        var result = new SparseRepairResult { DryRun = _dryRun };
        const int sectorSize = 512;

        foreach (var entry in audit.Entries)
        {
            if (entry.Status != GptAuditStatus.Mismatch)
                continue;

            var partResult = new SparsePartitionResult
            {
                PartitionName = entry.PartitionName,
            };

            // a. Check blocklist
            if (_neverRestore.Contains(entry.PartitionName) && !_force)
            {
                partResult.Status = SparseRepairStatus.Blocklisted;
                partResult.ErrorMessage = "BLOCKLISTED - SKIPPED (device-unique partition, use --force to override)";
                Console.WriteLine($"  [SPARSE] {entry.PartitionName}: BLOCKLISTED - SKIPPED");
                result.Partitions.Add(partResult);
                continue;
            }

            // b. Read stock image
            var stockPath = Path.Combine(_backupDir, $"{entry.PartitionName}.img");
            if (!File.Exists(stockPath))
            {
                partResult.Status = SparseRepairStatus.Skipped;
                partResult.ErrorMessage = "NO STOCK IMAGE - SKIPPED";
                Console.WriteLine($"  [SPARSE] {entry.PartitionName}: NO STOCK IMAGE - SKIPPED");
                result.Partitions.Add(partResult);
                continue;
            }

            var stockData = await File.ReadAllBytesAsync(stockPath, ct);

            if (_dryRun)
            {
                // Log planned writes but make ZERO calls
                partResult.Status = SparseRepairStatus.Repaired; // would be repaired
                partResult.SectorsWritten = entry.DifferingSectorCount;
                partResult.WrittenSectorAddresses = new List<long>(entry.DifferingSectorAddresses);
                Console.WriteLine($"  [DRY-RUN] [SPARSE] {entry.PartitionName}: Would write {entry.DifferingSectorCount} sectors");

                foreach (var addr in entry.DifferingSectorAddresses)
                {
                    long sectorNumber = addr / sectorSize;
                    Console.WriteLine($"    [DRY-RUN] Would write sector {sectorNumber} (offset {addr})");
                }

                result.Partitions.Add(partResult);
                continue;
            }

            // c. Write each differing sector
            Console.WriteLine($"  [SPARSE] {entry.PartitionName}: Writing {entry.DifferingSectorCount} differing sectors...");
            int writtenCount = 0;

            foreach (var addr in entry.DifferingSectorAddresses)
            {
                long sectorNumber = addr / sectorSize;
                int byteOffset = (int)addr;

                // Extract 512-byte block from stock image
                if (byteOffset + sectorSize > stockData.Length)
                {
                    Console.WriteLine($"    WARNING: Sector at offset {addr} beyond stock image size - skipping");
                    continue;
                }

                var blockData = new byte[sectorSize];
                Array.Copy(stockData, byteOffset, blockData, 0, sectorSize);

                await _client.WritePartitionBlocksAsync(
                    entry.PartitionName, blockData, sectorNumber,
                    lun: 0, sectorSize: sectorSize, ct: ct);

                partResult.WrittenSectorAddresses.Add(addr);
                writtenCount++;
            }

            partResult.SectorsWritten = writtenCount;
            Console.WriteLine($"  [SPARSE] {entry.PartitionName}: {writtenCount} sectors written, verifying...");

            // d. Read back and verify every written sector
            var deviceData = await _client.ReadPartitionAsync(entry.PartitionName, 0, ct: ct);
            int verifiedCount = 0;
            bool allMatch = true;

            foreach (var addr in partResult.WrittenSectorAddresses)
            {
                int byteOffset = (int)addr;
                if (byteOffset + sectorSize > deviceData.Length ||
                    byteOffset + sectorSize > stockData.Length)
                {
                    allMatch = false;
                    Console.WriteLine($"    VERIFY FAIL: Sector at offset {addr} out of bounds on readback");
                    continue;
                }

                bool sectorMatches = true;
                for (int b = 0; b < sectorSize; b++)
                {
                    if (deviceData[byteOffset + b] != stockData[byteOffset + b])
                    {
                        sectorMatches = false;
                        break;
                    }
                }

                if (sectorMatches)
                {
                    verifiedCount++;
                }
                else
                {
                    allMatch = false;
                    Console.WriteLine($"    VERIFY FAIL: Sector at offset {addr} does not match stock after write");
                }
            }

            partResult.SectorsVerified = verifiedCount;

            if (allMatch && verifiedCount == writtenCount)
            {
                partResult.Status = SparseRepairStatus.Repaired;
                Console.WriteLine($"  [SPARSE] {entry.PartitionName}: VERIFIED - all {verifiedCount} sectors match stock");
            }
            else
            {
                partResult.Status = SparseRepairStatus.VerificationFailed;
                partResult.ErrorMessage = $"Verification failed: {verifiedCount}/{writtenCount} sectors verified";
                Console.WriteLine($"  [SPARSE] {entry.PartitionName}: VERIFICATION FAILED - {verifiedCount}/{writtenCount} sectors verified");
            }

            result.Partitions.Add(partResult);
        }

        Console.WriteLine($"  [SPARSE] Summary: {result.TotalPartitionsRepaired} repaired, {result.TotalPartitionsFailed} failed, {result.TotalPartitionsSkipped} skipped, {result.TotalSectorsWritten} total sectors written");
        return result;
    }

'''

c = c.replace(
    '    private static string ComputeSha256(byte[] data)',
    method + '    private static string ComputeSha256(byte[] data)'
)

with open(f, 'w', encoding='utf-8') as fh:
    fh.write(c)
print('Done - PartitionRestorer.cs updated')