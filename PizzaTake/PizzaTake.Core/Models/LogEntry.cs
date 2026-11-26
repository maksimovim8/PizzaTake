namespace PizzaTake.Core.Models;

/// <summary>
/// Represents a single parsed entry from the local Serilog log file.
/// </summary>
public sealed class LogEntry
{
    public string Timestamp { get; init; } = string.Empty;
    public string Level { get; init; } = "Information";
    public string LevelShort { get; init; } = "INF";
    public string Source { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string RawText { get; init; } = string.Empty;
}
