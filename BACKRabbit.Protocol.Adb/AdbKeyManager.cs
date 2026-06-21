using System.Security.Cryptography;
using System.Text;

namespace BACKRabbit.Protocol.Adb;

/// <summary>
/// ADB RSA Key Manager
/// Generates and manages RSA key pairs for ADB authentication
/// Compatible with Android's adbkey format
/// </summary>
public static class AdbKeyManager
{
    private static readonly string KeyDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 
        ".android");
    
    private static readonly string PrivateKeyPath = Path.Combine(KeyDirectory, "adbkey");
    private static readonly string PublicKeyPath = Path.Combine(KeyDirectory, "adbkey.pub");
    
    /// <summary>
    /// Get or generate RSA key pair
    /// </summary>
    /// <summary>
    /// Decode PEM to DER bytes. Handles both PKCS#1 (RSA PRIVATE KEY) and PKCS#8 (PRIVATE KEY).
    /// </summary>
    private static byte[]? DecodePem(byte[] pemBytes)
    {
        var pemText = Encoding.ASCII.GetString(pemBytes);
        if (!pemText.StartsWith("-----BEGIN"))
            return pemBytes; // Already DER
        
        // Extract Base64 body between header and footer
        var lines = pemText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var base64 = new StringBuilder();
        bool inBody = false;
        foreach (var line in lines)
        {
            if (line.StartsWith("-----END"))
                break;
            if (inBody)
                base64.Append(line.Trim());
            if (line.StartsWith("-----BEGIN"))
                inBody = true;
        }
        
        try
        {
            return Convert.FromBase64String(base64.ToString());
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Get or generate RSA key pair. Returns public key in ANDROID_PUBKEY_MODULUS format
    /// (what ADB expects), not SSH format. Converts existing SSH-format adbkey.pub automatically.
    /// </summary>
    public static (byte[] privateKey, byte[] publicKey, string publicKeyName) GetKeyPair()
    {
        Directory.CreateDirectory(KeyDirectory);
        
        if (File.Exists(PrivateKeyPath) && File.Exists(PublicKeyPath))
        {
            var privateKeyRaw = File.ReadAllBytes(PrivateKeyPath);
            var privateKey = DecodePem(privateKeyRaw);
            
            var publicKeyLine = File.ReadAllText(PublicKeyPath);
            var parts = publicKeyLine.Split(' ');
            
            if (parts.Length >= 2 && privateKey != null)
            {
                // Existing adbkey.pub is in SSH format ("ssh-rsa <base64> <name>")
                // Parse SSH format → extract RSAParameters → re-encode as ANDROID_PUBKEY_MODULUS
                var sshEncoded = Convert.FromBase64String(parts[1]);
                var rsaParams = ParseSshRsaPublicKey(sshEncoded);
                var name = parts.Length > 2 ? parts[2] : "user@host";
                
                if (rsaParams != null)
                {
                    var androidPubkey = EncodeAndroidPubkeyModulus(rsaParams.Value);
                    return (privateKey, androidPubkey, name);
                }
                
                // Fallback: return SSH format if parsing fails (shouldn't happen)
                return (privateKey, sshEncoded, name);
            }
        }
        
        return GenerateNewKeyPair();
    }
    
    /// <summary>
    /// Generate new RSA key pair
    /// </summary>
    private static (byte[] privateKey, byte[] publicKey, string publicKeyName) GenerateNewKeyPair()
    {
        using var rsa = RSA.Create(2048);
        
        var privateKey = rsa.ExportPkcs8PrivateKey();
        var publicKeyParams = rsa.ExportParameters(false);
        var publicKey = EncodeSshRsaPublicKey(publicKeyParams);
        
        var userName = Environment.UserName;
        var hostName = Environment.MachineName;
        var publicKeyName = $"{userName}@{hostName}";
        
        File.WriteAllBytes(PrivateKeyPath, privateKey);
        File.WriteAllText(PublicKeyPath, $"ssh-rsa {Convert.ToBase64String(publicKey)} {publicKeyName}\n");
        
        return (privateKey, publicKey, publicKeyName);
    }
    
    /// <summary>
    /// Encode RSA public key in ANDROID_PUBKEY_MODULUS format (what ADB expects).
    /// Format: 4 bytes modulus_size_words (uint32 LE) + 4 bytes exponent (uint32 LE) + N bytes modulus (big-endian).
    /// For RSA 2048: modulus_size_words=64, exponent=65537, modulus=256 bytes → 264 bytes total, then Base64 encoded.
    /// Reference: AOSP system/core/adb/adb_auth.cpp — android_pubkey_encode()
    /// </summary>
    private static byte[] EncodeAndroidPubkeyModulus(RSAParameters parameters)
    {
        var modulus = parameters.Modulus!;
        var exponent = parameters.Exponent!;
        
        // Strip leading zero byte from modulus (BigInteger quirk — may be 257 bytes)
        int start = 0;
        while (start < modulus.Length && modulus[start] == 0)
            start++;
        if (start > 0)
            modulus = modulus[start..];
        
        // Pad or trim to exactly 256 bytes (RSA 2048)
        if (modulus.Length < 256)
        {
            var padded = new byte[256];
            Array.Copy(modulus, 0, padded, 256 - modulus.Length, modulus.Length);
            modulus = padded;
        }
        else if (modulus.Length > 256)
        {
            modulus = modulus[^256..]; // Take last 256 bytes
        }
        
        // modulus_size_words = 256 / 4 = 64 (uint32 LE)
        var modulusSizeWords = BitConverter.GetBytes((uint)(modulus.Length / 4));
        if (!BitConverter.IsLittleEndian)
            Array.Reverse(modulusSizeWords);
        
        // Exponent as uint32 LE (typically 65537 = 0x010001)
        // .NET returns exponent as big-endian byte array, convert to uint32
        uint exponentValue = 0;
        foreach (var b in exponent)
            exponentValue = (exponentValue << 8) | b;
        var exponentBytes = BitConverter.GetBytes(exponentValue);
        if (!BitConverter.IsLittleEndian)
            Array.Reverse(exponentBytes);
        
        // Build ANDROID_PUBKEY_MODULUS: 4 + 4 + 256 = 264 bytes
        var result = new byte[4 + 4 + modulus.Length];
        Array.Copy(modulusSizeWords, 0, result, 0, 4);
        Array.Copy(exponentBytes, 0, result, 4, 4);
        Array.Copy(modulus, 0, result, 8, modulus.Length);
        
        return result;
    }
    
    /// <summary>
    /// Parse SSH-format RSA public key back to RSAParameters.
    /// SSH format: 4-byte len + "ssh-rsa" + 4-byte len + exponent + 4-byte len + modulus
    /// Used to convert existing adbkey.pub (SSH format) to ANDROID_PUBKEY_MODULUS format.
    /// </summary>
    private static RSAParameters? ParseSshRsaPublicKey(byte[] sshEncoded)
    {
        try
        {
            int pos = 0;
            
            // Read algorithm name
            var algoLen = ReadBigEndianUInt32(sshEncoded, pos); pos += 4;
            if (pos + algoLen > sshEncoded.Length) return null;
            var algo = Encoding.ASCII.GetString(sshEncoded, pos, (int)algoLen); pos += (int)algoLen;
            if (algo != "ssh-rsa") return null;
            
            // Read exponent
            var expLen = ReadBigEndianUInt32(sshEncoded, pos); pos += 4;
            if (pos + expLen > sshEncoded.Length) return null;
            var exponent = new byte[expLen];
            Array.Copy(sshEncoded, pos, exponent, 0, expLen); pos += (int)expLen;
            
            // Read modulus
            var modLen = ReadBigEndianUInt32(sshEncoded, pos); pos += 4;
            if (pos + modLen > sshEncoded.Length) return null;
            var modulus = new byte[modLen];
            Array.Copy(sshEncoded, pos, modulus, 0, modLen);
            
            return new RSAParameters { Exponent = exponent, Modulus = modulus };
        }
        catch
        {
            return null;
        }
    }
    
    private static uint ReadBigEndianUInt32(byte[] data, int offset)
    {
        return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) |
               ((uint)data[offset + 2] << 8) | data[offset + 3];
    }
    
    /// <summary>
    /// Encode RSA public key in SSH format (kept for reading existing adbkey.pub files)
    /// </summary>
    private static byte[] EncodeSshRsaPublicKey(RSAParameters parameters)
    {
        using var ms = new MemoryStream();
        
        WriteSshString(ms, "ssh-rsa"u8.ToArray());
        WriteSshMpint(ms, parameters.Exponent!);
        WriteSshMpint(ms, parameters.Modulus!);
        
        return ms.ToArray();
    }
    
    /// <summary>
    /// Write SSH string (4-byte length + data)
    /// </summary>
    private static void WriteSshString(MemoryStream ms, byte[] data)
    {
        var length = BitConverter.GetBytes((uint)data.Length);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(length);
        }
        ms.Write(length, 0, 4);
        ms.Write(data, 0, data.Length);
    }
    
    /// <summary>
    /// Write SSH mpint (multi-precision integer)
    /// </summary>
    private static void WriteSshMpint(MemoryStream ms, byte[] data)
    {
        // Trim leading zeros from byte array
        int start = 0;
        while (start < data.Length && data[start] == 0)
            start++;
        
        var trimmed = start < data.Length ? data[start..] : Array.Empty<byte>();
        
        if (trimmed.Length == 0 || (trimmed[0] & 0x80) != 0)
        {
            var newTrimmed = new byte[trimmed.Length + 1];
            newTrimmed[0] = 0;
            Array.Copy(trimmed, 0, newTrimmed, 1, trimmed.Length);
            trimmed = newTrimmed;
        }
        WriteSshString(ms, trimmed);
    }
    
    /// <summary>
    /// Sign token with private key (PKCS#1 v1.5)
    /// </summary>
    public static byte[] SignToken(byte[] privateKey, byte[] token)
    {
        using var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(privateKey, out _);
        
        var signature = rsa.SignData(token, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
        
        return signature;
    }
}