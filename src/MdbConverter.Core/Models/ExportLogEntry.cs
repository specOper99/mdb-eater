namespace MdbConverter.Core.Models;

public enum LogLevel
{
    Info,
    Warning,
    Error
}

public sealed record ExportLogEntry(LogLevel Level, string ObjectName, string Message);
