namespace DriverCompass.App.Services;

/// <summary>Window-bound UI operations the view models need (implemented by MainWindow).</summary>
public interface IShellService
{
    /// <summary>Shows a save dialog and returns the chosen path, or <c>null</c> if cancelled.</summary>
    Task<string?> PickSaveFileAsync(string suggestedName, string fileTypeLabel, string extension);

    /// <summary>Shows the settings dialog; returns true if the user saved.</summary>
    Task<bool> ShowSettingsAsync();
}
