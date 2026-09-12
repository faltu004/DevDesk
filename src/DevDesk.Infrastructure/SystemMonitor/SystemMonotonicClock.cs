using System.Diagnostics;
using DevDesk.Core.SystemMonitor;

namespace DevDesk.Infrastructure.SystemMonitor;

/// <summary>
/// Production monotonic clock backed by Stopwatch high-resolution performance counter.
/// </summary>
public sealed class SystemMonotonicClock : IMonotonicClock
{
    public static readonly SystemMonotonicClock Instance = new();

    public long GetTimestamp() => Stopwatch.GetTimestamp();

    public TimeSpan Elapsed(long startingTimestamp, long endingTimestamp) =>
        Stopwatch.GetElapsedTime(startingTimestamp, endingTimestamp);
}
