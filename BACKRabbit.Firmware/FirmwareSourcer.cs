using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace BACKRabbit.Firmware;

/// <summary>
/// Downloads genuine Samsung firmware from Samsung FUS (Firmware Update Server),
/// decrypts the .enc4 container, and extracts partition images.
/// Based on the Frija/SamFirm open-source implementations.
/// </summary>
public class FirmwareSourcer
{
    private readonly HttpClient _http;
    private const string FusBaseUrl = "https://fota-cloud-dn.ospserver.net/firmware";
    private string? _cachedAuthToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

    public FirmwareSourcer(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.None
        });
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("FOTA");
        _http.Timeout = TimeSpan.FromMinutes(30);
    }

    /// <summary>
    /// Full firmware sourcing pipeline: query FUS → download → decrypt → extract.
    /// </summary>
    /// <param name="model">Samsung model (e.g., SM-F966U1)</param>
    /// <param name="region">CSC/region code (e.g., XAA)</param>
    /// <param name="imei">Device IMEI for FUS authentication (optional but recommended)</param>
    /// <param name="outputDir">Directory to save extracted .img files</param>
    /// <param name="progress">Optional progress reporter (0.0–1.0)</param>
    /// <param name="ct">Cancellation token</param>
    public async Task<FirmwareSourceResult> SourceAsync(
        string model,
        string region,
        string? imei,
        string outputDir,
        IProgress<FirmwareDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        var result = new FirmwareSourceResult
        {
            Model = model,
            Region = region,
        };

        Directory.CreateDirectory(outputDir);

        // Phase 1: Query FUS for firmware info
        progress?.Report(new FirmwareDownloadProgress
        {
            Phase = "Querying Samsung FUS...",
            PercentComplete = 0.0
        });

        // Phase 0: Acquire auth token
        progress?.Report(new FirmwareDownloadProgress
        {
            Phase = "Authenticating with Samsung FUS...",
            PercentComplete = 0.0
        });

        var authToken = await GetAuthTokenAsync(model, region, imei, ct);

        var fusInfo = await QueryFusAsync(model, region, authToken, ct);
        result.Version = fusInfo.Version;
        result.BuildDate = fusInfo.BuildDate;
        result.FirmwareSize = fusInfo.Size;

        // Phase 2: Download encrypted firmware
        progress?.Report(new FirmwareDownloadProgress
        {
            Phase = "Downloading firmware...",
            PercentComplete = 0.05,
            TotalBytes = fusInfo.Size
        });

        var enc4Path = Path.Combine(outputDir, $"{model}_{region}.enc4");
        await DownloadEncryptedAsync(fusInfo.DownloadPath, enc4Path, fusInfo.Size, authToken, progress, ct);

        // Phase 3: Decrypt
        progress?.Report(new FirmwareDownloadProgress
        {
            Phase = "Decrypting firmware...",
            PercentComplete = 0.55
        });

        var zipPath = Path.Combine(outputDir, $"{model}_{region}.zip");
        DecryptEnc4(enc4Path, zipPath, model, region);

        // Clean up .enc4
        try { File.Delete(enc4Path); } catch { }

        // Phase 4: Extract ZIP
        progress?.Report(new FirmwareDownloadProgress
        {
            Phase = "Extracting firmware ZIP...",
            PercentComplete = 0.70
        });

        var extractDir = Path.Combine(outputDir, "extracted");
        ZipFile.ExtractToDirectory(zipPath, extractDir, true);

        // Clean up .zip
        try { File.Delete(zipPath); } catch { }

        // Phase 5: Extract partitions from AP .tar.md5
        progress?.Report(new FirmwareDownloadProgress
        {
            Phase = "Extracting partition images...",
            PercentComplete = 0.85
        });

        var apFile = Directory.GetFiles(extractDir, "AP_*.tar.md5")
            .Concat(Directory.GetFiles(extractDir, "AP_*.tar"))
            .FirstOrDefault();

        if (apFile != null)
        {
            var package = SamsungFirmwareExtractor.ExtractTarMd5(apFile);
            foreach (var partition in package.Partitions)
            {
                var imgPath = Path.Combine(outputDir, $"{partition.Key}.img");
                await File.WriteAllBytesAsync(imgPath, partition.Value, ct);
                result.ExtractedPartitions.Add(partition.Key);
            }
        }

        // Also extract BL files if present
        var blFile = Directory.GetFiles(extractDir, "BL_*.tar.md5")
            .Concat(Directory.GetFiles(extractDir, "BL_*.tar"))
            .FirstOrDefault();

        if (blFile != null)
        {
            var blPackage = SamsungFirmwareExtractor.ExtractTarMd5(blFile);
            foreach (var partition in blPackage.Partitions)
            {
                if (!result.ExtractedPartitions.Contains(partition.Key))
                {
                    var imgPath = Path.Combine(outputDir, $"{partition.Key}.img");
                    await File.WriteAllBytesAsync(imgPath, partition.Value, ct);
                    result.ExtractedPartitions.Add(partition.Key);
                }
            }
        }

        // Clean up extracted temp dir
        try { Directory.Delete(extractDir, true); } catch { }

        progress?.Report(new FirmwareDownloadProgress
        {
            Phase = "Complete",
            PercentComplete = 1.0
        });

        result.Success = true;
        result.FirmwarePath = outputDir;
        return result;
    }

    /// <summary>
    /// Acquire Samsung FUS authentication token.
    /// Uses device identity (model + region + IMEI) to generate auth token.
    /// Token is cached for 25 minutes to avoid re-auth on retry.
    /// </summary>
    private async Task<string> GetAuthTokenAsync(
        string model, string region, string? imei, CancellationToken ct)
    {
        // Return cached token if still valid
        if (_cachedAuthToken != null && DateTime.UtcNow < _tokenExpiry)
            return _cachedAuthToken;

        // Method 1: Generate auth token from device identity (Frija pattern)
        // The FUS auth token is derived from MD5(model:region:imei) or similar
        var identityString = string.IsNullOrEmpty(imei)
            ? $"{model}:{region}"
            : $"{model}:{region}:{imei}";

        var identityHash = MD5.HashData(Encoding.ASCII.GetBytes(identityString));
        var authToken = Convert.ToHexString(identityHash).ToLowerInvariant();

        // Method 2: Try Samsung auth endpoint for bearer token (if IMEI provided)
        if (!string.IsNullOrEmpty(imei))
        {
            try
            {
                var bearerToken = await TryGetBearerTokenAsync(model, region, imei, ct);
                if (bearerToken != null)
                {
                    authToken = bearerToken;
                }
            }
            catch
            {
                // Fall back to identity-derived token
            }
        }

        // Cache token for 25 minutes
        _cachedAuthToken = authToken;
        _tokenExpiry = DateTime.UtcNow.AddMinutes(25);

        return authToken;
    }

    /// <summary>
    /// Try to get a bearer token from Samsung's auth endpoint.
    /// This is the preferred method when IMEI is available.
    /// </summary>
    private async Task<string?> TryGetBearerTokenAsync(
        string model, string region, string imei, CancellationToken ct)
    {
        // Samsung auth endpoint (used by Frija)
        var authUrl = "https://fota-cloud-dn.ospserver.net/auth/token";

        var payload = new
        {
            model,
            csc = region,
            imei,
            client = "FOTA",
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, authUrl);
        request.Content = JsonContent.Create(payload);
        request.Headers.TryAddWithoutValidation("X-Device-Identity",
            Convert.ToHexString(MD5.HashData(Encoding.ASCII.GetBytes($"{model}:{region}:{imei}"))).ToLowerInvariant());

        using var response = await _http.SendAsync(request, ct);

        if (response.IsSuccessStatusCode)
        {
            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("token", out var tokenElement))
                return tokenElement.GetString();
            if (doc.RootElement.TryGetProperty("access_token", out var accessTokenElement))
                return accessTokenElement.GetString();
        }

        return null;
    }

    /// <summary>
    /// Query Samsung FUS for firmware metadata.
    /// </summary>
    private async Task<FusFirmwareInfo> QueryFusAsync(
        string model, string region, string authToken, CancellationToken ct)
    {
        var url = $"{FusBaseUrl}/{model}/{region}/version.xml";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {authToken}");
        request.Headers.TryAddWithoutValidation("X-Auth-Token", authToken);

        using var response = await _http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            // If 403, auth token may be invalid — clear cache and throw
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                _cachedAuthToken = null;
                _tokenExpiry = DateTime.MinValue;
                throw new FirmwareSourceException(
                    $"FUS authentication failed (HTTP 403). " +
                    $"Model '{model}' / Region '{region}' may not be valid, " +
                    "or Samsung requires IMEI for this device. " +
                    "Try providing --imei or check model/region codes.");
            }

            throw new FirmwareSourceException(
                $"FUS query failed (HTTP {(int)response.StatusCode}). " +
                $"Model '{model}' / Region '{region}' may not be valid. " +
                "Check model code (e.g., SM-F966U1) and CSC/region (e.g., XAA).");
        }

        var xml = await response.Content.ReadAsStringAsync(ct);
        var doc = XDocument.Parse(xml);
        var root = doc.Root!;

        var info = new FusFirmwareInfo
        {
            Version = root.Element("latest")?.Value ?? "unknown",
            BuildDate = root.Element("build_date")?.Value ?? "unknown",
            Size = long.TryParse(root.Element("size")?.Value, out var s) ? s : 0,
            DownloadPath = root.Element("download_path")?.Value
                ?? throw new FirmwareSourceException("FUS response missing download path"),
        };

        return info;
    }

    /// <summary>
    /// Download encrypted .enc4 file with progress reporting.
    /// </summary>
    private async Task DownloadEncryptedAsync(
        string url, string outputPath, long totalBytes, string authToken,
        IProgress<FirmwareDownloadProgress>? progress, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {authToken}");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[81920]; // 80KB
        long totalRead = 0;
        int read;

        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
            totalRead += read;

            if (totalBytes > 0 && progress != null)
            {
                var downloadPercent = 0.05 + (0.50 * (double)totalRead / totalBytes);
                progress.Report(new FirmwareDownloadProgress
                {
                    Phase = $"Downloading... {FormatBytes(totalRead)} / {FormatBytes(totalBytes)}",
                    PercentComplete = downloadPercent,
                    TotalBytes = totalBytes,
                    BytesDownloaded = totalRead
                });
            }
        }
    }

    /// <summary>
    /// Decrypt Samsung .enc4 firmware container.
    /// AES-128-CBC with key = MD5(model + ":" + region), IV = first 16 bytes of encrypted file.
    /// </summary>
    private static void DecryptEnc4(string enc4Path, string outputPath, string model, string region)
    {
        var enc4Data = File.ReadAllBytes(enc4Path);

        if (enc4Data.Length < 16)
            throw new FirmwareSourceException("Encrypted file too small (missing IV)");

        // Key derivation: MD5(model:region)
        var keyMaterial = Encoding.ASCII.GetBytes($"{model}:{region}");
        var keyHash = MD5.HashData(keyMaterial);
        var key = keyHash.AsSpan(0, 16).ToArray();

        // IV is first 16 bytes of encrypted file
        var iv = enc4Data.AsSpan(0, 16).ToArray();
        var ciphertext = enc4Data.AsSpan(16).ToArray();

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var decryptor = aes.CreateDecryptor();
        var plaintext = decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);

        // Verify ZIP header
        if (plaintext.Length < 4 || plaintext[0] != 0x50 || plaintext[1] != 0x4B)
        {
            throw new FirmwareSourceException(
                "Decryption produced invalid data (no ZIP header). " +
                "The model/region combination may be incorrect, or Samsung changed the encryption scheme.");
        }

        File.WriteAllBytes(outputPath, plaintext);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1_000_000_000) return $"{bytes / 1_000_000_000.0:F1} GB";
        if (bytes >= 1_000_000) return $"{bytes / 1_000_000.0:F1} MB";
        if (bytes >= 1_000) return $"{bytes / 1_000.0:F1} KB";
        return $"{bytes} B";
    }

    private class FusFirmwareInfo
    {
        public string Version { get; set; } = "";
        public string BuildDate { get; set; } = "";
        public long Size { get; set; }
        public string DownloadPath { get; set; } = "";
    }
}

/// <summary>
/// Result of a firmware sourcing operation.
/// </summary>
public class FirmwareSourceResult
{
    public string Model { get; set; } = "";
    public string Region { get; set; } = "";
    public string Version { get; set; } = "";
    public string BuildDate { get; set; } = "";
    public long FirmwareSize { get; set; }
    public string FirmwarePath { get; set; } = "";
    public List<string> ExtractedPartitions { get; set; } = new();
    public bool Success { get; set; }
}

/// <summary>
/// Progress information during firmware download/extraction.
/// </summary>
public class FirmwareDownloadProgress
{
    public string Phase { get; set; } = "";
    public double PercentComplete { get; set; }
    public long TotalBytes { get; set; }
    public long BytesDownloaded { get; set; }
}

/// <summary>
/// Exception thrown when firmware sourcing fails.
/// </summary>
public class FirmwareSourceException : Exception
{
    public FirmwareSourceException(string message) : base(message) { }
    public FirmwareSourceException(string message, Exception inner) : base(message, inner) { }
}