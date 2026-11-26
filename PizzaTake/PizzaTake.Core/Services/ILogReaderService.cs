using PizzaTake.Core.Models;

namespace PizzaTake.Core.Services;

/// <summary>
/// Platform-independent abstraction for reading and filtering local application log files.
/// </summary>
public interface ILogReaderService
{
    string LogsDirectory { get; }
    string? GetLatestLogFilePath();
    Task<IReadOnlyList<LogEntry>> ReadRecentEntriesAsync(int maxEntries = 100, string? levelFilter = null, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ReadRecentRawLinesAsync(int maxLines = 30, CancellationToken ct = default);
}
