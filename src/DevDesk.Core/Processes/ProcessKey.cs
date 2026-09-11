namespace DevDesk.Core.Processes;

/// <summary>
/// Compound process identity model ensuring strict protection against Windows PID recycling.
/// When StartTimeUtc is available, the identity is verifiable across refreshes.
/// When StartTimeUtc is inaccessible (e.g. protected system processes), an ephemeral generation ID
/// ensures the unverifiable identity never collides with or inherits state from previous samples.
/// </summary>
public readonly record struct ProcessKey : IEquatable<ProcessKey>
{
    public int ProcessId { get; }
    public DateTimeOffset? StartTimeUtc { get; }
    public long? EphemeralId { get; }

    public bool IsVerifiable => StartTimeUtc.HasValue;

    public ProcessKey(int processId, DateTimeOffset? startTimeUtc, long? ephemeralId = null)
    {
        ProcessId = processId;
        StartTimeUtc = startTimeUtc.HasValue ? startTimeUtc.Value.ToUniversalTime() : null;
        EphemeralId = ephemeralId;
    }

    public static ProcessKey CreateVerified(int processId, DateTimeOffset startTimeUtc)
        => new(processId, startTimeUtc.ToUniversalTime(), null);

    public static ProcessKey CreateUnverified(int processId, long ephemeralId)
        => new(processId, null, ephemeralId);

    public override string ToString() => IsVerifiable
        ? $"PID:{ProcessId}_{StartTimeUtc:O}"
        : $"PID:{ProcessId}_Ephemeral:{EphemeralId}";
}
