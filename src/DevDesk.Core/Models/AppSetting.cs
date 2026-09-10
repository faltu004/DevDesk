namespace DevDesk.Core.Models;

/// <summary>
/// Represents a durable application configuration key-value setting.
/// </summary>
public sealed class AppSetting
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}
