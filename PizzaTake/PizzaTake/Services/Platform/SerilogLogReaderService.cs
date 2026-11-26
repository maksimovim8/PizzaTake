using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PizzaTake.Core.Models;

using PizzaTake.Core.Services;

namespace PizzaTake.Services.Platform;

/// <summary>
/// Reads and filters rolling Serilog log files from AppDataDirectory/logs using shared read access.
/// </summary>
public sealed partial class SerilogLogReaderService : ILogReaderService
{
    private readonly IAppPathsService appPaths;
    private readonly ILogger<SerilogLogReaderService> logger;

    public SerilogLogReaderService(IAppPathsService appPaths, ILogger<SerilogLogReaderService> logger)
    {
        this.appPaths = appPaths;
        this.logger = logger;
    }

    public string LogsDirectory => appPaths.LogsDirectory;

    public string? GetLatestLogFilePath()
    {
        if (!Directory.Exists(LogsDirectory))
            return null;

        return Directory.EnumerateFiles(LogsDirectory, "app-*.log", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ThenByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    public async Task<IReadOnlyList<LogEntry>> ReadRecentEntriesAsync(
        int maxEntries = 100,
        string? levelFilter = null,
        CancellationToken ct = default)
    {
        var rawLines = await ReadAllLinesSharedAsync(ct);
        if (rawLines.Count == 0)
            return Array.Empty<LogEntry>();

        var entries = ParseEntries(rawLines);
        var normalizedFilter = NormalizeFilter(levelFilter);
        IEnumerable<LogEntry> filtered = entries;
        if (!string.IsNullOrEmpty(normalizedFilter))
        {
            filtered = filtered.Where(entry =>
                string.Equals(entry.Level, normalizedFilter, StringComparison.OrdinalIgnoreCase));
        }

        return filtered
            .TakeLast(Math.Max(1, maxEntries))
            .Reverse()
            .ToList();
    }

    public async Task<IReadOnlyList<string>> ReadRecentRawLinesAsync(int maxLines = 30, CancellationToken ct = default)
    {
        var rawLines = await ReadAllLinesSharedAsync(ct);
        if (rawLines.Count == 0)
            return Array.Empty<string>();

        return rawLines
            .TakeLast(Math.Max(1, maxLines))
            .ToList();
    }

    private async Task<List<string>> ReadAllLinesSharedAsync(CancellationToken ct)
    {
        var filePath = GetLatestLogFilePath();
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return [];

        var lines = new List<string>();
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

            while (await reader.ReadLineAsync(ct) is { } line)
            {
                ct.ThrowIfCancellationRequested();
                lines.Add(line);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to read log file {LogFilePath}", filePath);
        }

        return lines;
    }

    private static List<LogEntry> ParseEntries(IReadOnlyList<string> lines)
    {
        var result = new List<LogEntry>();
        LogEntry? current = null;
        var extraMessage = new StringBuilder();
        var extraRaw = new StringBuilder();

        void FlushCurrent()
        {
            if (current is null)
                return;

            if (extraMessage.Length > 0)
            {
                result.Add(new LogEntry
                {
                    Timestamp = current.Timestamp,
                    Level = current.Level,
                    LevelShort = current.LevelShort,
                    Source = current.Source,
                    Message = $"{current.Message}{Environment.NewLine}{extraMessage}",
                    RawText = $"{current.RawText}{Environment.NewLine}{extraRaw}"
                });
                extraMessage.Clear();
                extraRaw.Clear();
            }
            else
            {
                result.Add(current);
            }
        }

        foreach (var line in lines)
        {
            var match = LogLineRegex().Match(line);
            if (match.Success)
            {
                FlushCurrent();

                var shortLevel = match.Groups["level"].Value.ToUpperInvariant();
                var source = match.Groups["source"].Value.Trim();
                var lastDot = source.LastIndexOf('.');
                if (lastDot >= 0 && lastDot < source.Length - 1)
                    source = source[(lastDot + 1)..];

                current = new LogEntry
                {
                    Timestamp = match.Groups["ts"].Value,
                    LevelShort = shortLevel,
                    Level = MapLevelName(shortLevel),
                    Source = string.IsNullOrEmpty(source) ? "App" : source,
                    Message = match.Groups["msg"].Value.Trim(),
                    RawText = line
                };
            }
            else if (current is not null)
            {
                if (extraMessage.Length > 0)
                {
                    extraMessage.AppendLine();
                    extraRaw.AppendLine();
                }

                extraMessage.Append(line);
                extraRaw.Append(line);
            }
            else if (!string.IsNullOrWhiteSpace(line))
            {
                result.Add(new LogEntry
                {
                    Timestamp = string.Empty,
                    LevelShort = "INF",
                    Level = "Information",
                    Source = "App",
                    Message = line,
                    RawText = line
                });
            }
        }

        FlushCurrent();
        return result;
    }

    private static string MapLevelName(string shortLevel) => shortLevel switch
    {
        "VRB" or "DBG" => "Debug",
        "INF" => "Information",
        "WRN" => "Warning",
        "ERR" => "Error",
        "FTL" => "Fatal",
        _ => "Information"
    };

    private static string? NormalizeFilter(string? levelFilter)
    {
        if (string.IsNullOrWhiteSpace(levelFilter))
            return null;

        var trimmed = levelFilter.Trim();
        if (string.Equals(trimmed, "Усі", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "All", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return trimmed;
    }

    [GeneratedRegex(@"^(?<ts>\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}\.\d{3})\s+\[(?<level>[A-Z]{3})\](?:\s+\[(?<source>[^\]]*)\])?\s*(?<msg>.*)$", RegexOptions.Compiled)]
    private static partial Regex LogLineRegex();
}
