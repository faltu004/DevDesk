using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DevDesk.App.ViewModels.Common;

namespace DevDesk.App.ViewModels.Dashboard;

/// <summary>
/// View model presenting the Dashboard overview state and metrics.
/// </summary>
public sealed partial class DashboardViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _title = "Dashboard";

    [ObservableProperty]
    private string _subtitle = "Your development environment at a glance.";

    [ObservableProperty]
    private string _searchPlaceholder = "Search projects, processes, ports or run a command...";

    [ObservableProperty]
    private string _searchShortcut = "Ctrl + K";

    public ObservableCollection<DashboardMetricItem> SystemMetrics { get; }
    public ObservableCollection<DashboardProjectItem> ActiveProjects { get; }
    public ObservableCollection<DashboardEnvSummaryItem> EnvironmentSummary { get; }
    public ObservableCollection<DashboardRecentProjectItem> RecentProjects { get; }
    public ObservableCollection<DashboardQuickActionItem> QuickActions { get; }

    public DashboardViewModel()
    {
        // Isolated presentation sample data for Phase 1 UI representation
        SystemMetrics = new ObservableCollection<DashboardMetricItem>
        {
            new()
            {
                Title = "CPU",
                Value = "28%",
                Subtext = "3.4 / 12 Cores",
                IconType = "CPU",
                ProgressValue = 28,
                VisualType = "Sparkline",
                AccentColorBrushKey = "Brush.Accent.Light"
            },
            new()
            {
                Title = "RAM",
                Value = "9.2 / 16 GB",
                Subtext = "57% in use",
                IconType = "RAM",
                ProgressValue = 57,
                VisualType = "Progress",
                AccentColorBrushKey = "Brush.Status.Success"
            },
            new()
            {
                Title = "Disk",
                Value = "68%",
                Subtext = "326 / 476 GB",
                IconType = "Disk",
                ProgressValue = 68,
                VisualType = "Progress",
                AccentColorBrushKey = "Brush.Status.Warning"
            },
            new()
            {
                Title = "Network",
                Value = "12 Mbps",
                Subtext = "↓ 8.4 Mbps   ↑ 3.6 Mbps",
                IconType = "Network",
                ProgressValue = 40,
                VisualType = "Histogram",
                AccentColorBrushKey = "Brush.Accent.Light"
            }
        };

        ActiveProjects = new ObservableCollection<DashboardProjectItem>
        {
            new()
            {
                Name = "Portfolio",
                Path = @"C:\Dev\portfolio",
                FrameworkBadge = "Vite",
                CategoryBadge = "Frontend",
                StatusText = "Running",
                IsRunning = true,
                PortText = "Port 5173",
                HasActivePort = true,
                IconKind = "Vite"
            },
            new()
            {
                Name = "DevAI Toolkit",
                Path = @"C:\Dev\devai-toolkit",
                FrameworkBadge = "Next.js",
                CategoryBadge = "Full Stack",
                StatusText = "Running",
                IsRunning = true,
                PortText = "Port 3000",
                HasActivePort = true,
                IconKind = "Next"
            },
            new()
            {
                Name = "API Server",
                Path = @"C:\Dev\api-server",
                FrameworkBadge = ".NET 8",
                CategoryBadge = "Backend",
                StatusText = "Stopped",
                IsRunning = false,
                PortText = "Port —",
                HasActivePort = false,
                IconKind = "DotNet"
            }
        };

        EnvironmentSummary = new ObservableCollection<DashboardEnvSummaryItem>
        {
            new()
            {
                Title = "Active Projects",
                Value = "2",
                Subtext = "of 3 total",
                IconKind = "Projects",
                IconBrushKey = "Brush.Accent.Light"
            },
            new()
            {
                Title = "Active Dev Ports",
                Value = "6",
                Subtext = "open and in use",
                IconKind = "Ports",
                IconBrushKey = "Brush.Accent.Light"
            },
            new()
            {
                Title = "Git Changes",
                Value = "9",
                Subtext = "uncommitted changes",
                IconKind = "Git",
                IconBrushKey = "Brush.Status.Success"
            },
            new()
            {
                Title = "Port Conflicts",
                Value = "0",
                Subtext = "all clear",
                IconKind = "Conflicts",
                IconBrushKey = "Brush.Status.Danger"
            }
        };

        RecentProjects = new ObservableCollection<DashboardRecentProjectItem>
        {
            new()
            {
                Name = "DevAI Toolkit",
                Path = @"C:\Dev\devai-toolkit",
                LastOpenedText = "2 hours ago",
                StatusColorBrushKey = "Brush.Status.Success",
                IconKind = "Next"
            },
            new()
            {
                Name = "Portfolio",
                Path = @"C:\Dev\portfolio",
                LastOpenedText = "5 hours ago",
                StatusColorBrushKey = "Brush.Status.Success",
                IconKind = "Vite"
            },
            new()
            {
                Name = "API Server",
                Path = @"C:\Dev\api-server",
                LastOpenedText = "1 day ago",
                StatusColorBrushKey = "Brush.Status.Danger",
                IconKind = "DotNet"
            },
            new()
            {
                Name = "Tools",
                Path = @"C:\Dev\tools",
                LastOpenedText = "2 days ago",
                StatusColorBrushKey = "Brush.Text.Muted",
                IconKind = "Folder"
            }
        };

        QuickActions = new ObservableCollection<DashboardQuickActionItem>
        {
            new()
            {
                Title = "Add Project",
                Description = "Manually add a project folder",
                IconKind = "Add"
            },
            new()
            {
                Title = "Detect Project",
                Description = "Scan for development projects",
                IconKind = "Detect"
            },
            new()
            {
                Title = "Open VS Code",
                Description = "Open current workspace",
                IconKind = "VSCode"
            },
            new()
            {
                Title = "Check Ports",
                Description = "Scan for port conflicts",
                IconKind = "Ports"
            }
        };
    }
}
