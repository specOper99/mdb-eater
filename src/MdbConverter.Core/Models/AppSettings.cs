namespace MdbConverter.Core.Models;

public sealed class AppSettings
{
    public ConflictMode ConflictMode { get; set; } = ConflictMode.Replace;
    public string? LastOutputFolder { get; set; }
    public string? LastMdbPath { get; set; }
    public string? LastWorkgroupPath { get; set; }
    public string? LastWorkgroupUser { get; set; }
    public bool WriteJson { get; set; } = true;
    public bool WritePostgresSql { get; set; } = true;
}
