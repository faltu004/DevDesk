using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevDesk.App.ViewModels.Common;
using DevDesk.Core.Git;
using DevDesk.Core.Launchers;
using Microsoft.Extensions.Logging;

namespace DevDesk.App.ViewModels.Projects;

/// <summary>
/// ViewModel presenting read-only Git status, topology, working tree modifications,
/// and commit metadata for the currently selected project.
/// </summary>
public sealed partial class ProjectGitViewModel : ViewModelBase, IDisposable
{
    private readonly IGitService _gitService;
    private readonly ILauncherService _launcherService;
    private readonly ILogger<ProjectGitViewModel> _logger;

    private Guid? _currentProjectId;
    private string? _currentProjectPath;
    private int _activeRequestId;
    private CancellationTokenSource? _activeCts;
    private bool _isActive;
    private bool _disposed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotGitRepository))]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotGitRepository))]
    private bool _isGitRepository;

    [ObservableProperty]
    private string? _repositoryRoot;

    [ObservableProperty]
    private string _branchName = "unknown";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpstreamDisplay))]
    [NotifyPropertyChangedFor(nameof(HasUpstream))]
    private string? _upstreamBranch;

    public bool HasUpstream => !string.IsNullOrEmpty(UpstreamBranch);
    public string UpstreamDisplay => string.IsNullOrEmpty(UpstreamBranch) ? "No upstream" : UpstreamBranch;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SyncStatusDisplay))]
    private int? _aheadCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SyncStatusDisplay))]
    private int? _behindCount;

    public string SyncStatusDisplay
    {
        get
        {
            if (!HasUpstream) return "No upstream";
            if (AheadCount == 0 && BehindCount == 0) return "Up to date";
            if (AheadCount > 0 && (BehindCount == 0 || BehindCount == null)) return $"+{AheadCount} ahead";
            if (BehindCount > 0 && (AheadCount == 0 || AheadCount == null)) return $"-{BehindCount} behind";
            if (AheadCount > 0 && BehindCount > 0) return $"+{AheadCount} / -{BehindCount}";
            return "Up to date";
        }
    }

    [ObservableProperty]
    private bool _isDetached;

    [ObservableProperty]
    private bool _isUnborn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWorkingTreeModifications))]
    [NotifyPropertyChangedFor(nameof(IsWorkingTreeClean))]
    private int _stagedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWorkingTreeModifications))]
    [NotifyPropertyChangedFor(nameof(IsWorkingTreeClean))]
    private int _unstagedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWorkingTreeModifications))]
    [NotifyPropertyChangedFor(nameof(IsWorkingTreeClean))]
    private int _untrackedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWorkingTreeModifications))]
    [NotifyPropertyChangedFor(nameof(IsWorkingTreeClean))]
    private int _conflictedCount;

    public bool HasWorkingTreeModifications => StagedCount > 0 || UnstagedCount > 0 || UntrackedCount > 0 || ConflictedCount > 0;
    public bool IsWorkingTreeClean => !HasWorkingTreeModifications;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLastCommit))]
    [NotifyPropertyChangedFor(nameof(FormattedCommitTime))]
    private GitCommitInfo? _lastCommit;

    public bool HasLastCommit => LastCommit != null;

    public string FormattedCommitTime => LastCommit != null
        ? LastCommit.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
        : string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(IsNotGitRepository))]
    private string? _errorMessage;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool IsNotGitRepository => !IsLoading && !IsGitRepository && string.IsNullOrEmpty(ErrorMessage);

    public ProjectGitViewModel(
        IGitService gitService,
        ILauncherService launcherService,
        ILogger<ProjectGitViewModel> logger)
    {
        _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
        _launcherService = launcherService ?? throw new ArgumentNullException(nameof(launcherService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Updates the target project and refreshes status if the Git tab is active.
    /// </summary>
    public void SetProject(Guid? projectId, string? projectPath)
    {
        if (_currentProjectId == projectId && _currentProjectPath == projectPath)
        {
            return;
        }

        CancelActiveRequest();

        _currentProjectId = projectId;
        _currentProjectPath = projectPath;
        var requestId = Interlocked.Increment(ref _activeRequestId);

        ResetState();

        if (projectId is null || string.IsNullOrWhiteSpace(projectPath))
        {
            return;
        }

        if (_isActive)
        {
            _ = RefreshAsyncInternal(requestId, projectId.Value, projectPath);
        }
    }

    /// <summary>
    /// Invoked when the user navigates to the Git tab.
    /// </summary>
    public void Activate()
    {
        _isActive = true;

        if (_currentProjectId is { } projectId && !string.IsNullOrWhiteSpace(_currentProjectPath))
        {
            // If we don't have status yet or need refresh, trigger refresh
            if (!IsGitRepository && string.IsNullOrEmpty(ErrorMessage) && !IsLoading)
            {
                var requestId = Interlocked.Increment(ref _activeRequestId);
                _ = RefreshAsyncInternal(requestId, projectId, _currentProjectPath);
            }
        }
    }

    /// <summary>
    /// Invoked when the user leaves the Git tab.
    /// </summary>
    public void Deactivate()
    {
        _isActive = false;
        CancelActiveRequest();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsRefreshing || _currentProjectId is not { } projectId || string.IsNullOrWhiteSpace(_currentProjectPath))
        {
            return;
        }

        CancelActiveRequest();
        var requestId = Interlocked.Increment(ref _activeRequestId);
        await RefreshAsyncInternal(requestId, projectId, _currentProjectPath);
    }

    [RelayCommand]
    public async Task OpenFolderAsync()
    {
        var targetPath = RepositoryRoot ?? _currentProjectPath;
        if (!string.IsNullOrWhiteSpace(targetPath))
        {
            await _launcherService.OpenInExplorerAsync(targetPath);
        }
    }

    [RelayCommand]
    public async Task OpenTerminalAsync()
    {
        var targetPath = RepositoryRoot ?? _currentProjectPath;
        if (!string.IsNullOrWhiteSpace(targetPath))
        {
            await _launcherService.OpenTerminalAsync(targetPath);
        }
    }

    [RelayCommand]
    public void CopyToClipboard(string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            try
            {
                Clipboard.SetText(text);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to copy text to clipboard");
            }
        }
    }

    private async Task RefreshAsyncInternal(int requestId, Guid projectId, string projectPath)
    {
        _activeCts = new CancellationTokenSource();
        var token = _activeCts.Token;

        try
        {
            IsLoading = true;
            IsRefreshing = true;
            ErrorMessage = null;

            var status = await _gitService.GetRepositoryStatusAsync(projectPath, token);

            // Stale check: ensure this request is still the active one for this project
            if (requestId != _activeRequestId || _currentProjectId != projectId)
            {
                return;
            }

            ApplyStatus(status);
        }
        catch (OperationCanceledException)
        {
            // Expected when user switches project or deactivates Git tab
        }
        catch (Exception ex)
        {
            if (requestId == _activeRequestId && _currentProjectId == projectId)
            {
                _logger.LogError(ex, "Unhandled error during Git status refresh for {ProjectPath}", projectPath);
                ErrorMessage = ex.Message;
                IsGitRepository = false;
            }
        }
        finally
        {
            if (requestId == _activeRequestId)
            {
                IsLoading = false;
                IsRefreshing = false;
            }
        }
    }

    public void ApplyStatus(GitRepositoryStatus status)
    {
        IsGitRepository = status.IsGitRepository;
        RepositoryRoot = status.RepositoryRoot;

        if (status.ErrorCode != GitErrorCode.None && status.ErrorCode != GitErrorCode.NotAGitRepository)
        {
            ErrorMessage = status.ErrorMessage;
        }
        else
        {
            ErrorMessage = null;
        }

        if (status.Branch is not null)
        {
            BranchName = status.Branch.BranchName;
            UpstreamBranch = status.Branch.UpstreamBranch;
            AheadCount = status.Branch.AheadCount;
            BehindCount = status.Branch.BehindCount;
            IsDetached = status.Branch.IsDetached;
            IsUnborn = status.Branch.IsUnborn;
        }
        else
        {
            BranchName = "unknown";
            UpstreamBranch = null;
            AheadCount = null;
            BehindCount = null;
            IsDetached = false;
            IsUnborn = false;
        }

        if (status.WorkingTree is not null)
        {
            StagedCount = status.WorkingTree.StagedCount;
            UnstagedCount = status.WorkingTree.UnstagedCount;
            UntrackedCount = status.WorkingTree.UntrackedCount;
            ConflictedCount = status.WorkingTree.ConflictedCount;
        }
        else
        {
            StagedCount = 0;
            UnstagedCount = 0;
            UntrackedCount = 0;
            ConflictedCount = 0;
        }

        LastCommit = status.LastCommit;
    }

    private void ResetState()
    {
        IsLoading = false;
        IsRefreshing = false;
        IsGitRepository = false;
        RepositoryRoot = null;
        BranchName = "unknown";
        UpstreamBranch = null;
        AheadCount = null;
        BehindCount = null;
        IsDetached = false;
        IsUnborn = false;
        StagedCount = 0;
        UnstagedCount = 0;
        UntrackedCount = 0;
        ConflictedCount = 0;
        LastCommit = null;
        ErrorMessage = null;
    }

    private void CancelActiveRequest()
    {
        try
        {
            _activeCts?.Cancel();
            _activeCts?.Dispose();
        }
        catch
        {
            // Ignore disposal issues
        }
        finally
        {
            _activeCts = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelActiveRequest();
    }
}
