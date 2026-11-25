using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
#if WINDOWS
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Security.Cryptography.DataProtection;
#endif

namespace PizzaTake.Services;

/// <summary>
/// Secure storage implementation backed by MAUI SecureStorage with a Windows DPAPI-encrypted fallback
/// for unpackaged Windows builds where SecureStorage.Default is unavailable.
/// Never falls back to plaintext Preferences.
/// </summary>
public sealed class MauiSecureStoreService : ISecureStoreService
{
    private readonly ILogger<MauiSecureStoreService>? logger;
    private readonly object fileLock = new();
    private readonly string fallbackFilePath;

    public MauiSecureStoreService(ILogger<MauiSecureStoreService>? logger = null)
    {
        this.logger = logger;
        fallbackFilePath = Path.Combine(FileSystem.AppDataDirectory, ".pizzatake_secure_store.dat");
    }

    public async Task<string?> GetAsync(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        try
        {
            return await SecureStorage.Default.GetAsync(key);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "SecureStorage.GetAsync failed for key {Key}; trying encrypted fallback.", key);
            return await ReadFromEncryptedFallbackAsync(key);
        }
    }

    public async Task SetAsync(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        try
        {
            await SecureStorage.Default.SetAsync(key, value);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "SecureStorage.SetAsync failed for key {Key}; using encrypted fallback.", key);
            await WriteToEncryptedFallbackAsync(key, value);
        }
    }

    public bool Remove(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var removed = false;
        try
        {
            removed = SecureStorage.Default.Remove(key);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "SecureStorage.Remove failed for key {Key}; clearing encrypted fallback.", key);
        }

        if (RemoveFromEncryptedFallback(key))
            removed = true;

        return removed;
    }

    private Task<string?> ReadFromEncryptedFallbackAsync(string key)
    {
#if WINDOWS
        return Task.Run(() =>
        {
            lock (fileLock)
            {
                var data = LoadFallbackDictionary();
                return data.TryGetValue(key, out var value) ? value : null;
            }
        });
#else
        return Task.FromResult<string?>(null);
#endif
    }

    private Task WriteToEncryptedFallbackAsync(string key, string value)
    {
#if WINDOWS
        return Task.Run(() =>
        {
            lock (fileLock)
            {
                var data = LoadFallbackDictionary();
                data[key] = value;
                SaveFallbackDictionary(data);
            }
        });
#else
        return Task.CompletedTask;
#endif
    }

    private bool RemoveFromEncryptedFallback(string key)
    {
#if WINDOWS
        lock (fileLock)
        {
            var data = LoadFallbackDictionary();
            if (!data.Remove(key))
                return false;

            SaveFallbackDictionary(data);
            return true;
        }
#else
        return false;
#endif
    }

#if WINDOWS
    private Dictionary<string, string> LoadFallbackDictionary()
    {
        if (!File.Exists(fallbackFilePath))
            return new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            var protectedBytes = File.ReadAllBytes(fallbackFilePath);
            var provider = new DataProtectionProvider();
            var unprotectedBuffer = provider.UnprotectAsync(protectedBytes.AsBuffer()).AsTask().GetAwaiter().GetResult();
            var plainBytes = unprotectedBuffer.ToArray();
            var json = Encoding.UTF8.GetString(plainBytes);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to decrypt Windows DPAPI secure store fallback.");
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private void SaveFallbackDictionary(Dictionary<string, string> data)
    {
        try
        {
            var dir = Path.GetDirectoryName(fallbackFilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(data);
            var plainBytes = Encoding.UTF8.GetBytes(json);
            var provider = new DataProtectionProvider("LOCAL=user");
            var protectedBuffer = provider.ProtectAsync(plainBytes.AsBuffer()).AsTask().GetAwaiter().GetResult();
            File.WriteAllBytes(fallbackFilePath, protectedBuffer.ToArray());
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to write Windows DPAPI secure store fallback.");
        }
    }
#endif
}
