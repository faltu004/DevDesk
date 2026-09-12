namespace DevDesk.Core.SystemMonitor;

/// <summary>
/// Abstraction for monotonic elapsed time measurement, enabling deterministic unit testing.
/// </summary>
public interface IMonotonicClock
{
    long GetTimestamp();
    TimeSpan Elapsed(long startingTimestamp, long endingTimestamp);
}
