using DevDesk.Core.Common;

namespace DevDesk.Core.Models;

/// <summary>
/// Represents a durable developer project registered in DevDesk.
/// </summary>
public sealed class DeveloperProject
{
    private string _path = string.Empty;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the absolute normalized filesystem path to the project directory.
    /// </summary>
    public string Path
    {
        get => _path;
        set => _path = PathHelper.NormalizePath(value);
    }

    public string? Framework { get; set; }

    public string? Language { get; set; }

    public string? PackageManager { get; set; }

    public string? RunCommand { get; set; }

    public string? BuildCommand { get; set; }

    public string? TestCommand { get; set; }

    public int? DefaultPort { get; set; }

    public bool IsGitRepository { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastOpenedAt { get; set; }

    /// <summary>
    /// Project-scoped saved commands associated with this project.
    /// </summary>
    public ICollection<SavedCommand> SavedCommands { get; set; } = new List<SavedCommand>();
}
