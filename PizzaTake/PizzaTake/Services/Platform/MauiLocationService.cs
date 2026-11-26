using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.Services;

namespace PizzaTake.Services.Platform;

public sealed class MauiLocationService : ILocationService
{
    private readonly ILogger<MauiLocationService> logger;

    public MauiLocationService(ILogger<MauiLocationService>? logger = null)
    {
        this.logger = logger ?? NullLogger<MauiLocationService>.Instance;
    }

    public async Task<LocationResult> GetCurrentLocationAsync(TimeSpan? timeout = null, CancellationToken ct = default)
    {
        try
        {
            var request = new GeolocationRequest(GeolocationAccuracy.Medium, timeout ?? TimeSpan.FromSeconds(10));
            var location = await Geolocation.Default.GetLocationAsync(request, ct);
            if (location is null)
            {
                var fallback = await GetLastKnownLocationAsync();
                if (fallback is not null)
                    return new LocationResult(fallback);

                return new LocationResult(null, HardwareFailureReason.Error, "Не вдалося визначити місце розташування.");
            }

            return new LocationResult(new GeoPoint(location.Latitude, location.Longitude, location.Accuracy));
        }
        catch (FeatureNotEnabledException ex)
        {
            logger.LogWarning(ex, "GPS/Location service is disabled on device");
            return new LocationResult(null, HardwareFailureReason.FeatureNotEnabled, "Увімкніть геолокацію на пристрої.");
        }
        catch (FeatureNotSupportedException ex)
        {
            logger.LogWarning(ex, "Geolocation is not supported on this device");
            return new LocationResult(null, HardwareFailureReason.FeatureNotSupported, "Геолокація не підтримується на цьому пристрої.");
        }
        catch (PermissionException ex)
        {
            logger.LogWarning(ex, "Location permission denied by user");
            return new LocationResult(null, HardwareFailureReason.PermissionDenied, "Надайте застосунку дозвіл на геолокацію.");
        }
        catch (OperationCanceledException)
        {
            return new LocationResult(null, HardwareFailureReason.Cancelled, "Запит геолокації скасовано.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error while retrieving device location");
            return new LocationResult(null, HardwareFailureReason.Error, "Не вдалося визначити місце розташування.");
        }
    }

    public async Task<GeoPoint?> GetLastKnownLocationAsync()
    {
        try
        {
            var last = await Geolocation.Default.GetLastKnownLocationAsync();
            return last is null ? null : new GeoPoint(last.Latitude, last.Longitude, last.Accuracy);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to retrieve last known location");
            return null;
        }
    }
}
