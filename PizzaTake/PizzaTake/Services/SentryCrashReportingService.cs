using System.Text;
using Microsoft.Extensions.Logging;
using Sentry;

namespace PizzaTake.Services;

/// <summary>
/// Sentry.Maui-backed crash reporting, breadcrumb tracking, and user bug report submission service.
/// </summary>
public sealed class SentryCrashReportingService : ICrashReportingService
{
    private readonly ILogger<SentryCrashReportingService> logger;

    public SentryCrashReportingService(ILogger<SentryCrashReportingService> logger)
    {
        this.logger = logger;
    }

    public bool IsConfigured => SentrySdk.IsEnabled;

    public void TrackEvent(string eventName, IDictionary<string, string>? properties = null)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            return;

        logger.LogInformation("Tracked event {EventName} ({Properties})", eventName, properties);

        try
        {
            var data = properties is null ? null : new Dictionary<string, string>(properties);
            SentrySdk.AddBreadcrumb(
                message: eventName,
                category: "business.event",
                type: "info",
                data: data,
                level: BreadcrumbLevel.Info);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to record Sentry breadcrumb for {EventName}", eventName);
        }
    }

    public void CaptureException(Exception exception, string? context = null)
    {
        ArgumentNullException.ThrowIfNull(exception);

        logger.LogError(exception, "Captured exception in context {Context}", context ?? "General");

        try
        {
            SentrySdk.CaptureException(exception, scope =>
            {
                if (!string.IsNullOrWhiteSpace(context))
                    scope.SetTag("context", context);
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send exception to Sentry");
        }
    }

    public async Task<bool> SendUserBugReportAsync(
        string description,
        IReadOnlyList<string> recentLogLines,
        CancellationToken ct = default)
    {
        var summary = string.IsNullOrWhiteSpace(description)
            ? "Користувач надіслав звіт про помилку."
            : description.Trim();

        var trimmedLogs = recentLogLines
            .TakeLast(30)
            .ToArray();

        var devicePlatform = DeviceInfo.Current.Platform.ToString();
        var osVersion = DeviceInfo.Current.VersionString;
        var deviceModel = $"{DeviceInfo.Current.Manufacturer} {DeviceInfo.Current.Model}".Trim();
        var deviceType = DeviceInfo.Current.DeviceType.ToString();
        var appVersion = $"{AppInfo.Current.VersionString} ({AppInfo.Current.BuildString})";

        logger.LogWarning(
            "User bug report requested: {Description} | Platform={Platform}, OS={OsVersion}, Model={DeviceModel}, App={AppVersion}, AttachedLogLines={LineCount}",
            summary,
            devicePlatform,
            osVersion,
            deviceModel,
            appVersion,
            trimmedLogs.Length);

        if (!SentrySdk.IsEnabled)
            return false;

        try
        {
            var logBody = trimmedLogs.Length == 0
                ? "(Лог-файл порожній)"
                : string.Join(Environment.NewLine, trimmedLogs);

            var eventId = SentrySdk.CaptureMessage(
                $"User Bug Report: {summary}",
                scope =>
                {
                    scope.SetTag("report_type", "user_bug_report");
                    scope.SetTag("device_platform", devicePlatform);
                    scope.SetTag("os_version", osVersion);
                    scope.SetTag("device_model", deviceModel);
                    scope.SetTag("app_version", appVersion);

                    scope.SetExtra("device_type", deviceType);
                    scope.SetExtra("user_description", summary);
                    scope.SetExtra("recent_logs_count", trimmedLogs.Length);
                    scope.SetExtra("recent_logs", logBody);

                    var attachmentBytes = Encoding.UTF8.GetBytes(logBody);
                    scope.AddAttachment(attachmentBytes, "recent-logs-30.txt", AttachmentType.Default, "text/plain");
                },
                SentryLevel.Warning);

            await SentrySdk.FlushAsync(TimeSpan.FromSeconds(5));
            logger.LogInformation("User bug report sent to Sentry with EventId={EventId}", eventId);
            return eventId != SentryId.Empty;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to submit user bug report to Sentry");
            return false;
        }
    }

    public void TriggerTestCrash()
    {
        logger.LogCritical("Manual test crash triggered by user for Sentry Issues verification");
        SentrySdk.AddBreadcrumb("User triggered manual test crash from LogsPage", "diagnostics", level: BreadcrumbLevel.Fatal);
        throw new InvalidOperationException("PizzaTake: тестовий краш для перевірки дашборда Sentry.");
    }
}
