namespace PizzaTake.Services;

/// <summary>
/// Platform-agnostic secure key-value storage contract backed by OS keystore/keychain/DPAPI.
/// </summary>
public interface ISecureStoreService
{
    Task<string?> GetAsync(string key);
    Task SetAsync(string key, string value);
    bool Remove(string key);
}
