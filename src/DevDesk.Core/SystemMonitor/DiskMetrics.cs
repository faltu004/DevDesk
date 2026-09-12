namespace DevDesk.Core.SystemMonitor;

/// <summary>
/// Capacity metrics for an individual drive.
/// </summary>
public sealed record DiskDriveMetrics
{
    public string DriveName { get; init; } = string.Empty;
    public string VolumeLabel { get; init; } = string.Empty;
    public ulong TotalBytes { get; init; }
    public ulong FreeBytes { get; init; }
    public ulong UsedBytes { get; init; }
    public double? UsagePercentage { get; init; }
    public bool IsReady { get; init; }
    public bool IsSystemDrive { get; init; }
}

/// <summary>
/// Authoritative host storage capacity summary.
/// </summary>
public sealed record DiskMetrics
{
    public IReadOnlyList<DiskDriveMetrics> Drives { get; init; } = Array.Empty<DiskDriveMetrics>();
    public DiskDriveMetrics? PrimarySystemDrive { get; init; }
    public bool IsAvailable => Drives.Count > 0 && PrimarySystemDrive is not null;

    public static DiskMetrics Empty() => new();
}
