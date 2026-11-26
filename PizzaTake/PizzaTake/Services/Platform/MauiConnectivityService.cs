using PizzaTake.Core.Services;

namespace PizzaTake.Services.Platform;

public sealed class MauiConnectivityService : IConnectivityService
{
    private bool isMonitoring;

    public bool IsConnected => Connectivity.Current.NetworkAccess == NetworkAccess.Internet;

    public string ConnectionDescription => Connectivity.Current.NetworkAccess.ToString();

    public event EventHandler<bool>? ConnectivityChanged;

    public void StartMonitoring()
    {
        if (isMonitoring)
            return;

        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
        isMonitoring = true;
    }

    public void StopMonitoring()
    {
        if (!isMonitoring)
            return;

        Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged;
        isMonitoring = false;
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        ConnectivityChanged?.Invoke(this, e.NetworkAccess == NetworkAccess.Internet);
    }
}
