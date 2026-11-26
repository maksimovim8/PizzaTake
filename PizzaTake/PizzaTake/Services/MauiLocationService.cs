using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PizzaTake.Services;

/// <summary>
/// MAUI geolocation service with configurable timeout, last-known-location fallback, and structured error mapping.
/// </summary>
public sealed class MauiLocationService : ILocationService
{
    private readonly ILogger<MauiLocationService> logger;

    public MauiLocationService(ILogger<MauiLocationService>? logger = null)
    {
        this.logger = logger ?? NullLogger<MauiLocationService>.Instance;
    }

    public async Task<LocationResult> GetCurrentLocationAsync(TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(10);
        try
        {
            var request = new GeolocationRequest(GeolocationAccuracy.Medium, effectiveTimeout);
            var location = await Geolocation.Default.GetLocationAsync(request, ct);
            if (location is not null)
            {
                var point = new GeoPoint(location.Latitude, location.Longitude, location.Accuracy);
                logger.LogInformation(
                    "Detected current GPS location ({Latitude:F5}, {Longitude:F5}, Accuracy={Accuracy}m)",
                    point.Latitude,
                    point.Longitude,
                    point.AccuracyMeters);
                return new LocationResult(point);
            }

            logger.LogWarning("Active GPS lookup returned null; attempting GetLastKnownLocationAsync fallback");
            var fallback = await GetLastKnownLocationAsync();
            if (fallback is not null)
                return new LocationResult(fallback);

            return new LocationResult(
                null,
                HardwareFailureReason.Error,
                "Не вдалося визначити координати. Перевірте сигнал GPS.");
        }
        catch (FeatureNotSupportedException ex)
        {
            logger.LogWarning(ex, "Geolocation feature is not supported on this device");
            return new LocationResult(
                null,
                HardwareFailureReason.FeatureNotSupported,
                "Геолокація не підтримується на цьому пристрої.");
        }
        catch (FeatureNotEnabledException ex)
        {
            logger.LogWarning(ex, "GPS/Location services are disabled on device");
            return new LocationResult(
                null,
                HardwareFailureReason.FeatureNotEnabled,
                "Увімкніть геолокацію (GPS) у налаштуваннях пристрою.");
        }
        catch (PermissionException ex)
        {
            logger.LogWarning(ex, "Location permission denied by user");
            return new LocationResult(
                null,
                HardwareFailureReason.PermissionDenied,
                "Надайте застосунку дозвіл на геолокацію.");
        }
        catch (OperationCanceledException ex)
        {
            logger.LogInformation(ex, "Location lookup was cancelled or timed out");
            var fallback = await GetLastKnownLocationAsync();
            if (fallback is not null)
                return new LocationResult(fallback);

            return new LocationResult(
                null,
                HardwareFailureReason.Cancelled,
                "Пошук координат скасовано або перевищено час очікування.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error while retrieving current location");
            return new LocationResult(
                null,
                HardwareFailureReason.Error,
                "Не вдалося визначити місце розташування. Спробуйте ще раз.");
        }
    }

    public async Task<GeoPoint?> GetLastKnownLocationAsync()
    {
        try
        {
            var lastKnown = await Geolocation.Default.GetLastKnownLocationAsync();
            if (lastKnown is null)
                return null;

            logger.LogInformation(
                "Using last known GPS location ({Latitude:F5}, {Longitude:F5})",
                lastKnown.Latitude,
                lastKnown.Longitude);
            return new GeoPoint(lastKnown.Latitude, lastKnown.Longitude, lastKnown.Accuracy);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to retrieve last known location");
            return null;
        }
    }
}
