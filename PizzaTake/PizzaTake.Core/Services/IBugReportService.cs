namespace PizzaTake.Core.Services;

/// <summary>
/// Result of submitting a user bug report with recent diagnostic logs.
/// </summary>
public sealed record BugReportSubmissionResult(bool SentToSentry, int AttachedLogLines, string Message);

/// <summary>
/// Coordinates user-initiated and error-triggered bug reports across the application
/// without exposing raw diagnostic logs to non-administrator users.
/// </summary>
public interface IBugReportService
{
    Task<BugReportSubmissionResult> SendReportAsync(
        string? userDescription,
        string? errorContext = null,
        CancellationToken ct = default);

    Task<bool> PromptAndSendAsync(
        string? errorContext = null,
        CancellationToken ct = default);

    Task ShowErrorWithReportOptionAsync(
        string title,
        string message,
        string errorContext,
        CancellationToken ct = default);
}
