namespace DevDesk.Core.Processes;

/// <summary>
/// Immutable snapshot container holding all observed processes and truthfully calculated summary metrics.
/// </summary>
public sealed record ProcessSnapshot
{
    public required DateTimeOffset CapturedAt { get; init; }
    public required IReadOnlyList<ProcessInfo> Processes { get; init; }
    public required int TotalProcessCount { get; init; }
    public required int ManagedProcessCount { get; init; }

    /// <summary>
    /// Sum of all valid CPU percentage samples across observable processes.
    /// Null if no valid CPU samples exist (such as on the initial sampling cycle).
    /// </summary>
    public double? SampledCpuPercent { get; init; }

    /// <summary>
    /// Sum of physical working sets across observable processes whose memory was readable.
    /// Excludes processes where working set query returned null or threw access denied.
    /// </summary>
    public long? CombinedWorkingSetBytes { get; init; }
}
