namespace DevDesk.App.ViewModels.Common;

/// <summary>
/// Marks a ViewModel as supporting asynchronous lifecycle initialization upon navigation activation.
/// </summary>
public interface IAsyncInitializable
{
    Task InitializeAsync();
}
