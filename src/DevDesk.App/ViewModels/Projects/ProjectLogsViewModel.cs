using System.Collections;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using DevDesk.App.ViewModels.Common;
using DevDesk.Core.Runner;

namespace DevDesk.App.ViewModels.Projects;

public enum LogStreamFilter
{
    All,
    Stdout,
    Stderr
}

/// <summary>
/// ViewModel coordinating bounded real-time log delivery, virtualized presentation,
/// session snapshot synchronization, presentation-only clear-view watermarking,
/// filter/search, and smart auto-scroll for project stdout/stderr.
/// </summary>
public sealed partial class ProjectLogsViewModel : ViewModelBase, IDisposable
{
    public const int MaxPendingQueueCapacity = 2000;
    public const int MaxBatchPerTick = 150;
    public const int MaxVisibleLines = 1000;
    public const int DeliveryIntervalMs = 60;
    public const int SearchDebounceMs = 150;

    private readonly IProjectRunnerService _runnerService;
    private readonly ILogger<ProjectLogsViewModel> _logger;

    private readonly ConcurrentQueue<ProcessOutputEvent> _pendingQueue = new();
    private int _pendingCount;
    private bool _needsResync;
    private bool _isActive;
    private bool _disposed;

    private readonly DispatcherTimer _deliveryTimer;
    private readonly DispatcherTimer _searchDebounceTimer;

    private Guid? _currentProjectId;
    private Guid? _currentSessionId;
    private readonly HashSet<long> _seenSequences = new();
    private readonly Dictionary<Guid, long> _sessionClearWatermarks = new();

    private readonly List<ProcessOutputEvent> _rawLogs = new();

    public event EventHandler? ScrollToBottomRequested;

    public ObservableCollection<LogLineItem> VisibleLogs { get; } = new();

    public ObservableCollection<SessionPresentationItem> RecentSessions { get; } = new();

    [ObservableProperty]
    private SessionPresentationItem? _selectedSession;

    [ObservableProperty]
    private LogStreamFilter _streamFilter = LogStreamFilter.All;

    [ObservableProperty]
    private bool _isFilterAll = true;

    [ObservableProperty]
    private bool _isFilterStdout;

    [ObservableProperty]
    private bool _isFilterStderr;

    partial void OnIsFilterAllChanged(bool value)
    {
        if (value)
        {
            StreamFilter = LogStreamFilter.All;
            IsFilterStdout = false;
            IsFilterStderr = false;
        }
    }

    partial void OnIsFilterStdoutChanged(bool value)
    {
        if (value)
        {
            StreamFilter = LogStreamFilter.Stdout;
            IsFilterAll = false;
            IsFilterStderr = false;
        }
    }

    partial void OnIsFilterStderrChanged(bool value)
    {
        if (value)
        {
            StreamFilter = LogStreamFilter.Stderr;
            IsFilterAll = false;
            IsFilterStdout = false;
        }
    }

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private bool _autoScroll = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnseenLogs))]
    private int _unseenLogsCount;

    public bool HasUnseenLogs => UnseenLogsCount > 0;

    public bool HasLogs => VisibleLogs.Count > 0;

    public ProjectLogsViewModel(
        IProjectRunnerService runnerService,
        ILogger<ProjectLogsViewModel> logger)
    {
        _runnerService = runnerService ?? throw new ArgumentNullException(nameof(runnerService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _runnerService.OutputReceived += OnOutputReceived;
        _runnerService.SessionChanged += OnSessionChanged;

        _deliveryTimer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(DeliveryIntervalMs)
        };
        _deliveryTimer.Tick += OnDeliveryTimerTick;

        _searchDebounceTimer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(SearchDebounceMs)
        };
        _searchDebounceTimer.Tick += OnSearchDebounceTimerTick;
    }

    public void Activate()
    {
        _isActive = true;
        _deliveryTimer.Start();
        if (AutoScroll)
        {
            ScrollToBottomRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Deactivate()
    {
        _isActive = false;
        _deliveryTimer.Stop();
        _searchDebounceTimer.Stop();
    }

    public void SetProject(Guid projectId, Guid? preferredSessionId = null)
    {
        if (_currentProjectId == projectId && _currentSessionId == preferredSessionId)
        {
            return;
        }

        _currentProjectId = projectId;
        RefreshRecentSessions(preferredSessionId);
    }

    private void RefreshRecentSessions(Guid? preferredSessionId = null)
    {
        if (!_currentProjectId.HasValue)
        {
            RecentSessions.Clear();
            SetSession((SessionPresentationItem?)null);
            return;
        }

        var sessions = _runnerService.GetRecentSessions(_currentProjectId.Value);
        RecentSessions.Clear();
        foreach (var s in sessions)
        {
            RecentSessions.Add(new SessionPresentationItem(s));
        }

        SessionPresentationItem? targetSession = null;
        if (preferredSessionId.HasValue)
        {
            targetSession = RecentSessions.FirstOrDefault(s => s.SessionId == preferredSessionId.Value);
        }

        targetSession ??= RecentSessions.FirstOrDefault();

        SetSession(targetSession);
    }

    partial void OnSelectedSessionChanged(SessionPresentationItem? value)
    {
        if (value?.SessionId != _currentSessionId)
        {
            SetSession(value);
        }
    }

    public void SetSession(SessionPresentationItem? item)
    {
        _currentSessionId = item?.SessionId;
        if (SelectedSession != item)
        {
            SelectedSession = item;
        }

        DrainPendingQueue();
        _needsResync = false;
        ReloadFromSnapshot();
    }

    public void SetSession(ProjectRunSession? session)
    {
        if (session is null)
        {
            SetSession((SessionPresentationItem?)null);
            return;
        }

        var item = RecentSessions.FirstOrDefault(s => s.SessionId == session.SessionId) ?? new SessionPresentationItem(session);
        SetSession(item);
    }

    partial void OnStreamFilterChanged(LogStreamFilter value)
    {
        RebuildVisibleLogs();
    }

    partial void OnFilterTextChanged(string value)
    {
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    public void ApplyFilterImmediate()
    {
        _searchDebounceTimer.Stop();
        RebuildVisibleLogs();
    }

    private void OnSearchDebounceTimerTick(object? sender, EventArgs e)
    {
        _searchDebounceTimer.Stop();
        RebuildVisibleLogs();
    }

    public long GetClearWatermark(Guid sessionId)
    {
        return _sessionClearWatermarks.TryGetValue(sessionId, out var mark) ? mark : 0;
    }

    private long GetCurrentClearWatermark()
    {
        return _currentSessionId.HasValue ? GetClearWatermark(_currentSessionId.Value) : 0;
    }

    [RelayCommand]
    public void ClearView()
    {
        if (_currentSessionId.HasValue)
        {
            long highestSeq = _rawLogs.Count > 0 ? _rawLogs[^1].SequenceNumber : 0;
            _sessionClearWatermarks[_currentSessionId.Value] = highestSeq;
            VisibleLogs.Clear();
            UnseenLogsCount = 0;
            OnPropertyChanged(nameof(HasLogs));
        }
    }

    [RelayCommand]
    public void ScrollToBottom()
    {
        AutoScroll = true;
        UnseenLogsCount = 0;
        ScrollToBottomRequested?.Invoke(this, EventArgs.Empty);
    }

    public void NotifyUserScrolled(bool isNearBottom)
    {
        if (isNearBottom)
        {
            AutoScroll = true;
            UnseenLogsCount = 0;
        }
        else
        {
            AutoScroll = false;
        }
    }

    [RelayCommand]
    public void CopySelectedLogs(IList? selectedItems)
    {
        if (selectedItems is null || selectedItems.Count == 0)
        {
            return;
        }

        var lines = selectedItems.OfType<LogLineItem>().Select(l => l.Text);
        var text = string.Join(Environment.NewLine, lines);
        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to copy selected logs to clipboard");
        }
    }

    [RelayCommand]
    public void CopyAllVisible()
    {
        if (VisibleLogs.Count == 0)
        {
            return;
        }

        var lines = VisibleLogs.Select(l => l.Text);
        var text = string.Join(Environment.NewLine, lines);
        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to copy all visible logs to clipboard");
        }
    }

    private void OnOutputReceived(object? sender, ProcessOutputEvent evt)
    {
        if (!_currentProjectId.HasValue || evt.ProjectId != _currentProjectId.Value)
        {
            return;
        }

        if (_currentSessionId.HasValue && evt.SessionId != _currentSessionId.Value)
        {
            return;
        }

        if (_pendingCount >= MaxPendingQueueCapacity)
        {
            // Bounded overflow policy: mark resync and drop event to prevent unbounded growth
            _needsResync = true;
            return;
        }

        _pendingQueue.Enqueue(evt);
        Interlocked.Increment(ref _pendingCount);
    }

    private void OnSessionChanged(object? sender, ProjectRunSession session)
    {
        if (!_currentProjectId.HasValue || session.ProjectId != _currentProjectId.Value)
        {
            return;
        }

        void Apply()
        {
            var item = new SessionPresentationItem(session);
            // Update RecentSessions list or SelectedSession in place
            var existing = RecentSessions.FirstOrDefault(s => s.SessionId == session.SessionId);
            if (existing is not null)
            {
                int index = RecentSessions.IndexOf(existing);
                RecentSessions[index] = item;
                if (SelectedSession?.SessionId == session.SessionId)
                {
                    SelectedSession = item;
                }
            }
            else
            {
                RecentSessions.Insert(0, item);
                while (RecentSessions.Count > 3) // active + 2 completed = 3
                {
                    RecentSessions.RemoveAt(RecentSessions.Count - 1);
                }

                PruneSessionWatermarks();

                if (SelectedSession is null || SelectedSession.SessionId == session.SessionId)
                {
                    SetSession(item);
                }
            }
        }

        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.InvokeAsync(Apply);
        }
        else
        {
            Apply();
        }
    }

    private void PruneSessionWatermarks()
    {
        if (RecentSessions.Count == 0)
        {
            return;
        }

        var validIds = RecentSessions.Select(s => s.SessionId).ToHashSet();
        var toRemove = _sessionClearWatermarks.Keys.Where(k => !validIds.Contains(k)).ToList();
        foreach (var k in toRemove)
        {
            _sessionClearWatermarks.Remove(k);
        }
    }

    private void OnDeliveryTimerTick(object? sender, EventArgs e)
    {
        if (!_isActive)
        {
            return;
        }

        ProcessDeliveryBatch();
    }

    public void ProcessDeliveryBatch()
    {
        if (_needsResync)
        {
            _needsResync = false;
            DrainPendingQueue();
            ReloadFromSnapshot();
            return;
        }

        if (_pendingQueue.IsEmpty)
        {
            return;
        }

        int processed = 0;
        var batch = new List<ProcessOutputEvent>();

        while (processed < MaxBatchPerTick && _pendingQueue.TryDequeue(out var evt))
        {
            Interlocked.Decrement(ref _pendingCount);
            processed++;

            if (_currentSessionId.HasValue && evt.SessionId == _currentSessionId.Value)
            {
                batch.Add(evt);
            }
        }

        if (batch.Count == 0)
        {
            return;
        }

        // Deterministic sequence ordering
        batch.Sort((a, b) => a.SequenceNumber.CompareTo(b.SequenceNumber));

        int addedVisible = 0;
        long watermark = GetCurrentClearWatermark();

        foreach (var evt in batch)
        {
            // Deduplicate: sequence number must not have been seen for this session
            if (_seenSequences.Contains(evt.SequenceNumber))
            {
                continue;
            }

            // Eviction check: if we've accumulated MaxVisibleLines, reject events older than our oldest line
            if (_rawLogs.Count >= MaxVisibleLines && evt.SequenceNumber < _rawLogs[0].SequenceNumber)
            {
                continue;
            }

            _seenSequences.Add(evt.SequenceNumber);

            // Insert into _rawLogs in deterministic sequence order
            int rawIndex = _rawLogs.Count;
            while (rawIndex > 0 && _rawLogs[rawIndex - 1].SequenceNumber > evt.SequenceNumber)
            {
                rawIndex--;
            }
            _rawLogs.Insert(rawIndex, evt);
            if (_rawLogs.Count > MaxVisibleLines)
            {
                var evicted = _rawLogs[0];
                _rawLogs.RemoveAt(0);
                _seenSequences.Remove(evicted.SequenceNumber);
            }

            // Session-scoped watermark exclusion
            if (evt.SequenceNumber <= watermark)
            {
                continue;
            }

            if (MatchesFilter(evt))
            {
                var item = new LogLineItem
                {
                    SessionId = evt.SessionId,
                    SequenceNumber = evt.SequenceNumber,
                    Timestamp = evt.Timestamp,
                    Text = evt.Text,
                    IsError = evt.IsError
                };

                // Insert into VisibleLogs in deterministic sequence order
                int visibleIndex = VisibleLogs.Count;
                while (visibleIndex > 0 && VisibleLogs[visibleIndex - 1].SequenceNumber > item.SequenceNumber)
                {
                    visibleIndex--;
                }
                VisibleLogs.Insert(visibleIndex, item);
                addedVisible++;

                if (VisibleLogs.Count > MaxVisibleLines)
                {
                    VisibleLogs.RemoveAt(0);
                }
            }
        }

        if (addedVisible > 0)
        {
            OnPropertyChanged(nameof(HasLogs));

            if (AutoScroll)
            {
                ScrollToBottomRequested?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                UnseenLogsCount += addedVisible;
            }
        }
    }

    private void ReloadFromSnapshot()
    {
        VisibleLogs.Clear();
        _rawLogs.Clear();
        _seenSequences.Clear();
        UnseenLogsCount = 0;

        if (!_currentProjectId.HasValue || !_currentSessionId.HasValue)
        {
            OnPropertyChanged(nameof(HasLogs));
            return;
        }

        long watermark = GetCurrentClearWatermark();
        var snapshot = _runnerService.GetSessionLogs(_currentProjectId.Value, _currentSessionId.Value);
        var sorted = snapshot.OrderBy(e => e.SequenceNumber).ToList();

        foreach (var evt in sorted)
        {
            _seenSequences.Add(evt.SequenceNumber);
            _rawLogs.Add(evt);

            if (evt.SequenceNumber > watermark && MatchesFilter(evt))
            {
                VisibleLogs.Add(new LogLineItem
                {
                    SessionId = evt.SessionId,
                    SequenceNumber = evt.SequenceNumber,
                    Timestamp = evt.Timestamp,
                    Text = evt.Text,
                    IsError = evt.IsError
                });
            }
        }

        while (_rawLogs.Count > MaxVisibleLines)
        {
            var evicted = _rawLogs[0];
            _rawLogs.RemoveAt(0);
            _seenSequences.Remove(evicted.SequenceNumber);
        }

        while (VisibleLogs.Count > MaxVisibleLines)
        {
            VisibleLogs.RemoveAt(0);
        }

        OnPropertyChanged(nameof(HasLogs));

        if (AutoScroll)
        {
            ScrollToBottomRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RebuildVisibleLogs()
    {
        VisibleLogs.Clear();
        UnseenLogsCount = 0;

        long watermark = GetCurrentClearWatermark();

        foreach (var evt in _rawLogs)
        {
            if (evt.SequenceNumber > watermark && MatchesFilter(evt))
            {
                VisibleLogs.Add(new LogLineItem
                {
                    SessionId = evt.SessionId,
                    SequenceNumber = evt.SequenceNumber,
                    Timestamp = evt.Timestamp,
                    Text = evt.Text,
                    IsError = evt.IsError
                });
            }
        }

        while (VisibleLogs.Count > MaxVisibleLines)
        {
            VisibleLogs.RemoveAt(0);
        }

        OnPropertyChanged(nameof(HasLogs));

        if (AutoScroll)
        {
            ScrollToBottomRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool MatchesFilter(ProcessOutputEvent evt)
    {
        if (StreamFilter == LogStreamFilter.Stdout && evt.IsError)
        {
            return false;
        }

        if (StreamFilter == LogStreamFilter.Stderr && !evt.IsError)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(FilterText))
        {
            return evt.Text.Contains(FilterText.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private void DrainPendingQueue()
    {
        while (_pendingQueue.TryDequeue(out _))
        {
            Interlocked.Decrement(ref _pendingCount);
        }
        _pendingCount = 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _deliveryTimer.Stop();
        _deliveryTimer.Tick -= OnDeliveryTimerTick;
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Tick -= OnSearchDebounceTimerTick;

        _runnerService.OutputReceived -= OnOutputReceived;
        _runnerService.SessionChanged -= OnSessionChanged;

        DrainPendingQueue();
        _sessionClearWatermarks.Clear();
        _seenSequences.Clear();
        VisibleLogs.Clear();
        _rawLogs.Clear();
    }
}
