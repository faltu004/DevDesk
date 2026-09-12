using System.Text;
using DevDesk.Core.Commands;

namespace DevDesk.Infrastructure.Commands;

/// <summary>
/// Thread-safe bounded in-memory buffer storing captured standard output and standard error events for a saved command session.
/// Enforces per-line character limits, line count limits, and total memory bounds.
/// </summary>
internal sealed class SavedCommandBoundedBuffer
{
    public const int DefaultMaxLines = 1000;
    public const int DefaultMaxLineLength = 4096;
    public const int DefaultMaxTotalBytes = 512 * 1024; // 512 KB

    private readonly int _maxLines;
    private readonly int _maxLineLength;
    private readonly int _maxTotalBytes;

    private readonly object _lock = new();
    private readonly Queue<SavedCommandOutputEvent> _entries = new();
    private int _currentTotalBytes;

    public SavedCommandBoundedBuffer(
        int maxLines = DefaultMaxLines,
        int maxLineLength = DefaultMaxLineLength,
        int maxTotalBytes = DefaultMaxTotalBytes)
    {
        _maxLines = maxLines;
        _maxLineLength = maxLineLength;
        _maxTotalBytes = maxTotalBytes;
    }

    public SavedCommandOutputEvent Add(Guid sessionId, Guid commandId, string rawLine, bool isError, long sequenceNumber)
    {
        string text = rawLine;
        if (text.Length > _maxLineLength)
        {
            text = text[.._maxLineLength] + " [truncated]";
        }

        int entryBytes = Encoding.UTF8.GetByteCount(text) + 64;

        var item = new SavedCommandOutputEvent
        {
            SessionId = sessionId,
            CommandId = commandId,
            SequenceNumber = sequenceNumber,
            TimestampUtc = DateTimeOffset.UtcNow,
            IsError = isError,
            Text = text
        };

        lock (_lock)
        {
            while (_entries.Count >= _maxLines && _entries.Count > 0)
            {
                EvictOldest();
            }

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

    public IReadOnlyList<SavedCommandOutputEvent> GetSnapshot()
    {
        lock (_lock)
        {
            return _entries.OrderBy(e => e.SequenceNumber).ToArray();
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
