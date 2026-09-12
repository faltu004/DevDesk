namespace DevDesk.Core.Settings;

/// <summary>
/// Supported telemetry polling cadences for Dashboard and Process views.
/// Sub-second polling is strictly prohibited.
/// </summary>
public enum MonitorRefreshInterval
{
    Seconds1_5 = 0,
    Seconds2 = 1,
    Seconds5 = 2,
    Seconds10 = 3
}

public static class MonitorRefreshIntervalExtensions
{
    public static TimeSpan ToTimeSpan(this MonitorRefreshInterval interval) => interval switch
    {
        MonitorRefreshInterval.Seconds1_5 => TimeSpan.FromMilliseconds(1500),
        MonitorRefreshInterval.Seconds2 => TimeSpan.FromSeconds(2),
        MonitorRefreshInterval.Seconds5 => TimeSpan.FromSeconds(5),
        MonitorRefreshInterval.Seconds10 => TimeSpan.FromSeconds(10),
        _ => TimeSpan.FromMilliseconds(1500)
    };

    public static string ToDisplayString(this MonitorRefreshInterval interval) => interval switch
    {
        MonitorRefreshInterval.Seconds1_5 => "1.5 seconds",
        MonitorRefreshInterval.Seconds2 => "2 seconds",
        MonitorRefreshInterval.Seconds5 => "5 seconds",
        MonitorRefreshInterval.Seconds10 => "10 seconds",
        _ => "1.5 seconds"
    };
}
