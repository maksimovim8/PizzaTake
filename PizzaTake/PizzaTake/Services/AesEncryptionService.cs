using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace PizzaTake.Services;

/// <summary>
/// AES-256 encryption service backed by a persistent key stored in SecureStorage.
/// </summary>
public sealed class AesEncryptionService : IEncryptionService
{
    private const string KeyStorageName = "pizzatake_aes_key";
    private const int KeySizeBytes = 32;
    private const int IvSizeBytes = 16;

    private readonly ISecureStoreService? secureStore;
    private readonly SemaphoreSlim keyLock = new(1, 1);
    private byte[]? cachedKey;

    public AesEncryptionService(ISecureStoreService? secureStore = null)
    {
        this.secureStore = secureStore;
    }

    public string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        var key = GetOrCreateKey();
        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.GenerateIV();

        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        using var encryptor = aes.CreateEncryptor();
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        var combined = new byte[aes.IV.Length + cipherBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, combined, 0, aes.IV.Length);
        Buffer.BlockCopy(cipherBytes, 0, combined, aes.IV.Length, cipherBytes.Length);
        return Convert.ToBase64String(combined);
    }

    public string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
            return string.Empty;

        try
        {
            var combined = Convert.FromBase64String(cipherText);
            if (combined.Length <= IvSizeBytes)
                return string.Empty;

            var iv = new byte[IvSizeBytes];
            var cipherBytes = new byte[combined.Length - IvSizeBytes];
            Buffer.BlockCopy(combined, 0, iv, 0, IvSizeBytes);
            Buffer.BlockCopy(combined, IvSizeBytes, cipherBytes, 0, cipherBytes.Length);

            var key = GetOrCreateKey();
            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var decryptor = aes.CreateDecryptor();
            var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            Debug.WriteLine($"[AesEncryptionService] Decryption failed: {ex.Message}");
            return string.Empty;
        }
    }

    private byte[] GetOrCreateKey()
    {
        if (cachedKey is not null)
            return cachedKey;

        // Offload async SecureStorage call to avoid SynchronizationContext deadlocks on the UI thread.
        return Task.Run(GetOrCreateKeyAsync).GetAwaiter().GetResult();
    }

    private async Task<byte[]> GetOrCreateKeyAsync()
    {
        if (cachedKey is not null)
            return cachedKey;

        await keyLock.WaitAsync();
        try
        {
            if (cachedKey is not null)
                return cachedKey;

            string? storedKey = null;
            try
            {
                storedKey = secureStore is not null
                    ? await secureStore.GetAsync(KeyStorageName)
                    : await SecureStorage.Default.GetAsync(KeyStorageName);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AesEncryptionService] SecureStorage read failed: {ex.Message}");
            }

            if (!string.IsNullOrEmpty(storedKey))
            {
                var decoded = Convert.FromBase64String(storedKey);
                if (decoded.Length == KeySizeBytes)
                {
                    cachedKey = decoded;
                    return cachedKey;
                }
            }

            var generated = RandomNumberGenerator.GetBytes(KeySizeBytes);
            var encoded = Convert.ToBase64String(generated);
            try
            {
                if (secureStore is not null)
                    await secureStore.SetAsync(KeyStorageName, encoded);
                else
                    await SecureStorage.Default.SetAsync(KeyStorageName, encoded);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AesEncryptionService] SecureStorage write failed: {ex.Message}");
            }

            cachedKey = generated;
            return cachedKey;
        }
        finally
        {
            keyLock.Release();
        }
    }
}
