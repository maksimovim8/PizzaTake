using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PizzaTake.Services;

/// <summary>
/// Opens coordinates in the platform's external map application via MAUI Map.Default.
/// </summary>
public sealed class MauiMapLauncherService : IMapLauncherService
{
    private readonly ILogger<MauiMapLauncherService> logger;

    public MauiMapLauncherService(ILogger<MauiMapLauncherService>? logger = null)
    {
        this.logger = logger ?? NullLogger<MauiMapLauncherService>.Instance;
    }

    public async Task<bool> OpenExternalMapAsync(double latitude, double longitude, string label)
    {
        try
        {
            var options = new MapLaunchOptions
            {
                Name = string.IsNullOrWhiteSpace(label) ? "PizzaTake" : label,
                NavigationMode = NavigationMode.None
            };

            var opened = await MainThread.InvokeOnMainThreadAsync(() =>
                Microsoft.Maui.ApplicationModel.Map.Default.TryOpenAsync(latitude, longitude, options));

            if (opened)
                logger.LogInformation("Opened external map for {Label} ({Latitude:F5}, {Longitude:F5})", label, latitude, longitude);
            else
                logger.LogWarning("External map application was unavailable for {Label} ({Latitude:F5}, {Longitude:F5})", label, latitude, longitude);

            return opened;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to launch external map for {Label}", label);
            return false;
        }
    }
}
