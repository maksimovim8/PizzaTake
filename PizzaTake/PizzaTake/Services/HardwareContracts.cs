namespace PizzaTake.Services;

/// <summary>
/// Platform-independent hardware permission types requested by PizzaTake.
/// </summary>
public enum AppPermission
{
    Camera,
    LocationWhenInUse
}

/// <summary>
/// Categorized reasons why a hardware capability call did not produce a value.
/// </summary>
public enum HardwareFailureReason
{
    None,
    PermissionDenied,
    FeatureNotSupported,
    FeatureNotEnabled,
    Cancelled,
    Error
}

/// <summary>
/// Geographic coordinate pair with optional horizontal accuracy in meters.
/// </summary>
public sealed record GeoPoint(double Latitude, double Longitude, double? AccuracyMeters = null);

/// <summary>
/// Outcome of a geolocation lookup.
/// </summary>
public sealed record LocationResult(
    GeoPoint? Location,
    HardwareFailureReason FailureReason = HardwareFailureReason.None,
    string? ErrorMessage = null)
{
    public bool IsSuccess => Location is not null && FailureReason == HardwareFailureReason.None;
}

/// <summary>
/// Outcome of a photo capture or gallery selection operation.
/// </summary>
public sealed record MediaCaptureResult(
    string? SavedFilePath,
    HardwareFailureReason FailureReason = HardwareFailureReason.None,
    string? ErrorMessage = null)
{
    public bool IsSuccess => !string.IsNullOrWhiteSpace(SavedFilePath) && FailureReason == HardwareFailureReason.None;
}

/// <summary>
/// Platform-independent map marker descriptor for pizzerias and quality reports.
/// </summary>
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

/// <summary>
/// Platform-independent runtime permission abstraction with rationale support.
/// </summary>
public interface IPermissionService
{
    Task<bool> IsGrantedAsync(AppPermission permission);
    Task<bool> EnsureGrantedAsync(AppPermission permission, string rationaleTitle, string rationaleMessage);
}

/// <summary>
/// Platform-independent camera capture and gallery picker abstraction.
/// </summary>
public interface IMediaService
{
    bool IsCaptureSupported { get; }
    Task<MediaCaptureResult> CapturePhotoAsync(string filePrefix, CancellationToken ct = default);
    Task<MediaCaptureResult> PickPhotoAsync(string filePrefix, CancellationToken ct = default);
}

/// <summary>
/// Platform-independent GPS / network geolocation abstraction.
/// </summary>
public interface ILocationService
{
    Task<LocationResult> GetCurrentLocationAsync(TimeSpan? timeout = null, CancellationToken ct = default);
    Task<GeoPoint?> GetLastKnownLocationAsync();
}

/// <summary>
/// Platform-independent launcher for opening coordinates in an external map application.
/// </summary>
public interface IMapLauncherService
{
    Task<bool> OpenExternalMapAsync(double latitude, double longitude, string label);
}

/// <summary>
/// Platform-independent accelerometer shake detection abstraction.
/// </summary>
public interface IShakeSensorService
{
    bool IsSupported { get; }
    bool IsMonitoring { get; }
    event EventHandler? ShakeDetected;
    void Start();
    void Stop();
}

/// <summary>
/// Platform-independent network connectivity monitoring abstraction.
/// </summary>
public interface IConnectivityService
{
    bool IsConnected { get; }
    string ConnectionDescription { get; }
    event EventHandler<bool>? ConnectivityChanged;
    void StartMonitoring();
    void StopMonitoring();
}
