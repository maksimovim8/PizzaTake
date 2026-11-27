using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using PizzaTake.Core.Services;

namespace PizzaTake.Services.Platform;

/// <summary>
/// MAUI connectivity monitoring service exposing online state and localized connection descriptions.
/// </summary>
public sealed class MauiConnectivityService : IConnectivityService
{
    private readonly ILogger<MauiConnectivityService> logger;
    private bool isMonitoring;

    public MauiConnectivityService(ILogger<MauiConnectivityService>? logger = null)
    {
        this.logger = logger ?? NullLogger<MauiConnectivityService>.Instance;
    }

    public bool IsConnected => Connectivity.Current.NetworkAccess == NetworkAccess.Internet;

    public string ConnectionDescription
    {
        get
        {
            if (!IsConnected)
                return "Офлайн-режим";

            var profiles = Connectivity.Current.ConnectionProfiles;
            if (profiles.Contains(ConnectionProfile.WiFi))
                return "Онлайн (Wi-Fi)";
            if (profiles.Contains(ConnectionProfile.Cellular))
                return "Онлайн (Мобільна мережа)";
            if (profiles.Contains(ConnectionProfile.Ethernet))
                return "Онлайн (Ethernet)";

            return "Онлайн";
        }
    }

    public event EventHandler<bool>? ConnectivityChanged;

    public void StartMonitoring()
    {
        if (isMonitoring)
            return;

        Connectivity.Current.ConnectivityChanged += OnMauiConnectivityChanged;
        isMonitoring = true;
        logger.LogDebug("Connectivity monitoring started (IsConnected={IsConnected}, Description={Description})",
            IsConnected, ConnectionDescription);
    }

    public void StopMonitoring()
    {
        if (!isMonitoring)
            return;

        Connectivity.Current.ConnectivityChanged -= OnMauiConnectivityChanged;
        isMonitoring = false;
        logger.LogDebug("Connectivity monitoring stopped");
    }

    private void OnMauiConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        var connected = e.NetworkAccess == NetworkAccess.Internet;
        logger.LogInformation("Network connectivity changed: IsConnected={IsConnected}, NetworkAccess={NetworkAccess}",
            connected, e.NetworkAccess);
        ConnectivityChanged?.Invoke(this, connected);
    }
}
