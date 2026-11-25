namespace PizzaTake.Services;

/// <summary>
/// Standard business event names tracked in crash reporting and analytics.
/// </summary>
public static class CrashEventNames
{
    public const string ProductOpened = "ProductOpened";
    public const string OrderPlaced = "OrderPlaced";
    public const string LoginSuccess = "LoginSuccess";
    public const string QualityReportCreated = "QualityReportCreated";
}

/// <summary>
/// Platform-independent abstraction for crash reporting, breadcrumbs, and user bug reports.
/// </summary>
public interface ICrashReportingService
{
    bool IsConfigured { get; }
    void TrackEvent(string eventName, IDictionary<string, string>? properties = null);
    void CaptureException(Exception exception, string? context = null);
    Task<bool> SendUserBugReportAsync(string description, IReadOnlyList<string> recentLogLines, CancellationToken ct = default);
    void TriggerTestCrash();
}
