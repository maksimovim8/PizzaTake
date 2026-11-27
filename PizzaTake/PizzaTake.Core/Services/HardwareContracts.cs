namespace PizzaTake.Core.Services;

public enum AppPermission
{
    Camera,
    LocationWhenInUse
}

public enum HardwareFailureReason
{
    None,
    PermissionDenied,
    FeatureNotSupported,
    FeatureNotEnabled,
    Cancelled,
    Error
}

public sealed record GeoPoint(double Latitude, double Longitude, double? AccuracyMeters = null);

public sealed record LocationResult(
    GeoPoint? Location,
    HardwareFailureReason FailureReason = HardwareFailureReason.None,
    string? ErrorMessage = null)
{
    public bool IsSuccess => Location is not null && FailureReason == HardwareFailureReason.None;
}

public sealed record MediaCaptureResult(
    string? SavedFilePath,
    HardwareFailureReason FailureReason = HardwareFailureReason.None,
    string? ErrorMessage = null)
{
    public bool IsSuccess => !string.IsNullOrWhiteSpace(SavedFilePath) && FailureReason == HardwareFailureReason.None;
}

public sealed record MapPinItem(
    int EntityId,
    string Title,
    string Subtitle,
    double Latitude,
    double Longitude,
    string Category = "Pizzeria")
{
    public string CategoryDisplayName => Category switch
    {
        "QualityReport" => "Акт якості",
        _ => "Піцерія"
    };
}

public interface IPermissionService
{
    Task<bool> IsGrantedAsync(AppPermission permission);
    Task<bool> EnsureGrantedAsync(AppPermission permission, string rationaleTitle, string rationaleMessage);
}

public interface IMediaService
{
    bool IsCaptureSupported { get; }
    Task<MediaCaptureResult> CapturePhotoAsync(string filePrefix, CancellationToken ct = default);
    Task<MediaCaptureResult> PickPhotoAsync(string filePrefix, CancellationToken ct = default);
}

public interface ILocationService
{
    Task<LocationResult> GetCurrentLocationAsync(TimeSpan? timeout = null, CancellationToken ct = default);
    Task<GeoPoint?> GetLastKnownLocationAsync();
}

public interface IMapLauncherService
{
    Task<bool> OpenExternalMapAsync(double latitude, double longitude, string label);
}

public interface IShakeSensorService
{
    bool IsSupported { get; }
    bool IsMonitoring { get; }
    event EventHandler? ShakeDetected;
    void Start();
    void Stop();
}

public interface IConnectivityService
{
    bool IsConnected { get; }
    string ConnectionDescription { get; }
    event EventHandler<bool>? ConnectivityChanged;
    void StartMonitoring();
    void StopMonitoring();
}
