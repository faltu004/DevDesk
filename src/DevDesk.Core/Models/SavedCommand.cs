namespace DevDesk.Core.Models;

/// <summary>
/// Represents a saved developer command, either project-scoped or global.
/// Stores structured executable and argument tokens without raw shell wrapping.
/// </summary>
public sealed class SavedCommand
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Foreign key referencing the parent project, or null if this is a global command.
    /// </summary>
    public Guid? ProjectId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// Executable tool name (e.g. "dotnet", "git", "npm") or absolute canonical path.
    /// </summary>
    public string Executable { get; set; } = string.Empty;

    /// <summary>
    /// Structured argument tokens preserved without shell flattening.
    /// </summary>
    public IReadOnlyList<string> Arguments { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Explicit working directory override, or null/empty to inherit project or fallback directory.
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Optional categorization tag (e.g. "Build", "Test", "Lint", "Git").
    /// </summary>
    public string? Category { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Navigation property to the parent project if project-scoped.
    /// </summary>
    public DeveloperProject? Project { get; set; }
}
