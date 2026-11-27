using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PizzaTake.Core.Services;

/// <summary>
/// Platform-independent implementation of <see cref="IBugReportService"/> that collects the last 30 log lines
/// in the background and submits a user bug report via <see cref="ICrashReportingService"/>.
/// </summary>
public sealed class BugReportService : IBugReportService
{
    public const int RecentLogLinesCount = 30;

    private readonly ILogReaderService logReaderService;
    private readonly ICrashReportingService crashReportingService;
    private readonly IDialogService dialogService;
    private readonly ILogger<BugReportService> logger;

    public BugReportService(
        ILogReaderService logReaderService,
        ICrashReportingService crashReportingService,
        IDialogService dialogService,
        ILogger<BugReportService>? logger = null)
    {
        this.logReaderService = logReaderService;
        this.crashReportingService = crashReportingService;
        this.dialogService = dialogService;
        this.logger = logger ?? NullLogger<BugReportService>.Instance;
    }

    public async Task<BugReportSubmissionResult> SendReportAsync(
        string? userDescription,
        string? errorContext = null,
        CancellationToken ct = default)
    {
        var recentLines = await logReaderService.ReadRecentRawLinesAsync(RecentLogLinesCount, ct);
        var formattedDescription = BuildDescription(userDescription, errorContext);
        var sentToSentry = await crashReportingService.SendUserBugReportAsync(formattedDescription, recentLines, ct);

        var message = sentToSentry
            ? $"Звіт про помилку надіслано в Sentry (додано {recentLines.Count} рядків логу)."
            : $"Звіт зафіксовано у локальному журналі ({recentLines.Count} рядків). Налаштуйте SENTRY_DSN для надсилання у Sentry.";

        return new BugReportSubmissionResult(sentToSentry, recentLines.Count, message);
    }

    public async Task<bool> PromptAndSendAsync(string? errorContext = null, CancellationToken ct = default)
    {
        try
        {
            var message = string.IsNullOrWhiteSpace(errorContext)
                ? "Опишіть, що саме трапилося. Технічні дані пристрою та останні логи будуть додані автоматично."
                : $"Контекст: {errorContext}\nОпишіть, що саме трапилося (опційно). Технічні дані додадуться автоматично.";

            var promptResult = await dialogService.PromptAsync(
                "Повідомити про помилку",
                message,
                accept: "Надіслати",
                cancel: "Скасувати",
                placeholder: "Опис проблеми (опційно)...",
                maxLength: 400);

            if (promptResult is null)
                return false;

            var result = await SendReportAsync(promptResult, errorContext, ct);
            var alertTitle = result.SentToSentry ? "Звіт надіслано" : "Звіт збережено локально";
            await dialogService.AlertAsync(alertTitle, result.Message, "OK");
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to submit user bug report (Context={ErrorContext})", errorContext ?? "General");
            await dialogService.AlertAsync("Помилка", "Не вдалося надіслати звіт про помилку. Спробуйте ще раз.", "OK");
            return false;
        }
    }

    public async Task ShowErrorWithReportOptionAsync(
        string title,
        string message,
        string errorContext,
        CancellationToken ct = default)
    {
        var wantsToReport = await dialogService.ConfirmAsync(
            title,
            message,
            accept: "Повідомити про помилку",
            cancel: "OK");

        if (wantsToReport)
            await PromptAndSendAsync(errorContext, ct);
    }

    private static string BuildDescription(string? userDescription, string? errorContext)
    {
        var trimmedUser = userDescription?.Trim() ?? string.Empty;
        var trimmedContext = errorContext?.Trim() ?? string.Empty;

        if (!string.IsNullOrEmpty(trimmedContext) && !string.IsNullOrEmpty(trimmedUser))
            return $"[{trimmedContext}] {trimmedUser}";

        if (!string.IsNullOrEmpty(trimmedUser))
            return trimmedUser;

        if (!string.IsNullOrEmpty(trimmedContext))
            return $"Користувач повідомив про помилку ({trimmedContext}).";

        return "Користувач надіслав звіт про помилку.";
    }
}
