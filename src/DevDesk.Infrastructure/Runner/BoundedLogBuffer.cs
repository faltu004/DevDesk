using System.Text;
using DevDesk.Core.Runner;

namespace DevDesk.Infrastructure.Runner;

/// <summary>
/// Memory-safe circular buffer for storing process stdout and stderr lines.
/// Strictly enforces per-line character limits, total line count limits, and total memory byte limits.
/// </summary>
internal sealed class BoundedLogBuffer
{
    public const int DefaultMaxLines = 1000;
    public const int DefaultMaxLineLength = 4096;
    public const int DefaultMaxTotalBytes = 512 * 1024; // 512 KB

    private readonly int _maxLines;
    private readonly int _maxLineLength;
    private readonly int _maxTotalBytes;

    private readonly object _lock = new();
    private readonly Queue<ProcessOutputEvent> _entries = new();
    private int _currentTotalBytes;

    public BoundedLogBuffer(
        int maxLines = DefaultMaxLines,
        int maxLineLength = DefaultMaxLineLength,
        int maxTotalBytes = DefaultMaxTotalBytes)
    {
        _maxLines = maxLines;
        _maxLineLength = maxLineLength;
        _maxTotalBytes = maxTotalBytes;
    }

    public ProcessOutputEvent Add(Guid sessionId, Guid projectId, string rawLine, bool isError)
    {
        // 1. Truncate pathological single line
        string text = rawLine;
        if (text.Length > _maxLineLength)
        {
            text = text[.._maxLineLength] + " [truncated]";
        }

        int entryBytes = Encoding.UTF8.GetByteCount(text) + 64; // approximate metadata overhead

        var item = new ProcessOutputEvent
        {
            SessionId = sessionId,
            ProjectId = projectId,
            Text = text,
            IsError = isError,
            Timestamp = DateTimeOffset.UtcNow
        };

        lock (_lock)
        {
            // Evict oldest if line count limit reached
            while (_entries.Count >= _maxLines && _entries.Count > 0)
            {
                EvictOldest();
            }

            // Evict oldest if byte limit reached
            while (_currentTotalBytes + entryBytes > _maxTotalBytes && _entries.Count > 0)
            {
                EvictOldest();
            }

            _entries.Enqueue(item);
            _currentTotalBytes += entryBytes;
        }

        return item;
    }

    private void EvictOldest()
    {
        if (_entries.TryDequeue(out var evicted))
        {
            int evictedBytes = Encoding.UTF8.GetByteCount(evicted.Text) + 64;
            _currentTotalBytes = Math.Max(0, _currentTotalBytes - evictedBytes);
        }
    }

    public IReadOnlyList<ProcessOutputEvent> GetSnapshot()
    {
        lock (_lock)
        {
            return _entries.ToArray();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
            _currentTotalBytes = 0;
        }
    }
}
