namespace DevDesk.Core.Models;

/// <summary>
/// Represents a saved execution command, either project-scoped or global.
/// </summary>
public sealed class SavedCommand
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Foreign key referencing the parent project, or null if this is a global command.
    /// </summary>
    public Guid? ProjectId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Command { get; set; } = string.Empty;

    public string WorkingDirectory { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Navigation property to the parent project if project-scoped.
    /// </summary>
    public DeveloperProject? Project { get; set; }
}
