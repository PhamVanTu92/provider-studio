using System.Security.Cryptography;
using System.Text;

namespace ProviderStudio.Shared;

/// <summary>
/// AES-256-CBC encryption for secrets stored in SQLite.
/// Key is read from env var STUDIO_ENCRYPTION_KEY (base64-encoded 32 bytes).
/// Falls back to a deterministic dev key when env var is not set (NOT for production).
/// </summary>
public static class Encryption
{
    private static readonly byte[] _key;

    static Encryption()
    {
        var envKey = Environment.GetEnvironmentVariable("STUDIO_ENCRYPTION_KEY");
        if (!string.IsNullOrEmpty(envKey))
        {
            _key = Convert.FromBase64String(envKey);
            if (_key.Length != 32)
                throw new InvalidOperationException(
                    "STUDIO_ENCRYPTION_KEY must be exactly 32 bytes (base64-encoded).");
        }
        else
        {
            // Dev fallback — deterministic, not secure for production
            _key = SHA256.HashData(Encoding.UTF8.GetBytes("provider-studio-dev-key-2024"));
        }
    }

    public static string Encrypt(string plainText)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();

        using var enc = aes.CreateEncryptor();
        var data = Encoding.UTF8.GetBytes(plainText);
        var cipher = enc.TransformFinalBlock(data, 0, data.Length);

        // Prefix IV (16 bytes) + cipher → base64
        var combined = new byte[16 + cipher.Length];
        aes.IV.CopyTo(combined, 0);
        cipher.CopyTo(combined, 16);
        return Convert.ToBase64String(combined);
    }

    public static string Decrypt(string cipherBase64)
    {
        var combined = Convert.FromBase64String(cipherBase64);

        var iv     = combined[..16];
        var cipher = combined[16..];

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV  = iv;

        using var dec = aes.CreateDecryptor();
        var plain = dec.TransformFinalBlock(cipher, 0, cipher.Length);
        return Encoding.UTF8.GetString(plain);
    }

    /// <summary>Generate a printable base64 key for use in STUDIO_ENCRYPTION_KEY.</summary>
    public static string GenerateKey() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}
