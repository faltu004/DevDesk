using CommunityToolkit.Mvvm.ComponentModel;
using DevDesk.Core.Processes;

namespace DevDesk.App.ViewModels.Processes;

/// <summary>
/// Presentation wrapper around a ProcessInfo domain model, formatting metrics and state
/// for WPF data binding in the Processes view.
/// </summary>
public sealed partial class ProcessItemPresentationModel : ObservableObject
{
    public ProcessInfo Process { get; }

    [ObservableProperty]
    private string? _executablePath;

    public ProcessItemPresentationModel(ProcessInfo process)
    {
        Process = process ?? throw new ArgumentNullException(nameof(process));
        _executablePath = process.ExecutablePath;
    }

    public ProcessKey Key => Process.Key;

    public int ProcessId => Process.ProcessId;

    public string ProcessName => Process.ProcessName;

    public double? CpuPercent => Process.CpuPercent;

    public string FormattedCpu => Process.CpuPercent.HasValue
        ? $"{Process.CpuPercent.Value:F1}%"
        : "—";

    public bool IsHighCpu => Process.CpuPercent.HasValue && Process.CpuPercent.Value >= 10.0;

    public long? WorkingSetBytes => Process.WorkingSetBytes;

    public string FormattedWorkingSet => FormatBytes(Process.WorkingSetBytes);

    public DateTimeOffset? StartTime => Process.StartTime;

    public string FormattedStartTime => Process.StartTime.HasValue
        ? Process.StartTime.Value.ToLocalTime().ToString("HH:mm:ss")
        : "—";

    public bool? IsResponding => Process.IsResponding;

    public string FormattedResponding => Process.IsResponding.HasValue
        ? (Process.IsResponding.Value ? "Responding" : "Not Responding")
        : "—";

    public ProcessOwnership Ownership => Process.Ownership;

    public bool IsManaged => Process.IsManaged;

    public string OwnershipBadgeText => IsManaged ? "DevDesk managed" : "External";

    public Guid? ManagedProjectId => Process.ManagedProjectId;

    public string? ManagedProjectName => Process.ManagedProjectName;

    public Guid? ManagedSessionId => Process.ManagedSessionId;

    public bool IsRootManagedProcess => Process.IsRootManagedProcess;

    public string StatusText => "Running";

    public static string FormatBytes(long? bytes)
    {
        if (!bytes.HasValue)
        {
            return "—";
        }

        double val = bytes.Value;
        if (val < 1024)
        {
            return $"{val:F0} B";
        }

        val /= 1024.0;
        if (val < 1024)
        {
            return $"{val:F1} KB";
        }

        val /= 1024.0;
        if (val < 1024)
        {
            return $"{val:F1} MB";
        }

        val /= 1024.0;
        return $"{val:F2} GB";
    }
}
