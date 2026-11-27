using PizzaTake.Core.Services;

namespace PizzaTake.Core.Tests.Helpers;

internal sealed class ImmediateMainThreadDispatcher : IMainThreadDispatcher
{
    public void BeginInvokeOnMainThread(Action action) => action();

    public Task InvokeOnMainThreadAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    public Task InvokeOnMainThreadAsync(Func<Task> func) => func();
}

internal sealed class InMemorySecureStore : ISecureStoreService
{
    private readonly Dictionary<string, string> store = new(StringComparer.Ordinal);

    public Task<string?> GetAsync(string key) =>
        Task.FromResult(store.TryGetValue(key, out var value) ? value : null);

    public Task SetAsync(string key, string value)
    {
        store[key] = value;
        return Task.CompletedTask;
    }

    public bool Remove(string key) => store.Remove(key);
}

internal sealed class InMemorySettingsService : ISettingsService
{
    private readonly Dictionary<string, object> values = new(StringComparer.Ordinal);

    public string GetString(string key, string defaultValue = "") =>
        values.TryGetValue(key, out var val) && val is string s ? s : defaultValue;

    public void SetString(string key, string value) => values[key] = value;

    public bool GetBool(string key, bool defaultValue = false) =>
        values.TryGetValue(key, out var val) && val is bool b ? b : defaultValue;

    public void SetBool(string key, bool value) => values[key] = value;

    public int GetInt(string key, int defaultValue = 0) =>
        values.TryGetValue(key, out var val) && val is int i ? i : defaultValue;

    public void SetInt(string key, int value) => values[key] = value;

    public void Remove(string key) => values.Remove(key);

    public void Clear() => values.Clear();
}
