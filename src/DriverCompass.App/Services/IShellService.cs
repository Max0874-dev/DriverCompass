using DriverCompass.App.ViewModels;

namespace DriverCompass.App.Services;

/// <summary>What the install confirmation dialog shows.</summary>
public sealed record InstallConfirmation(string DeviceName, string DriverTitle, string VersionText, bool NeedsDisclaimer, string SourceId);

/// <summary>Window-bound UI operations the view models need (implemented by MainWindow).</summary>
public interface IShellService
{
    /// <summary>Shows a save dialog and returns the chosen path, or <c>null</c> if cancelled.</summary>
    Task<string?> PickSaveFileAsync(string suggestedName, string fileTypeLabel, string extension);

    /// <summary>Shows the settings dialog; returns true if the user saved.</summary>
    Task<bool> ShowSettingsAsync();

    /// <summary>Stage 3: asks before installing ONE driver (with the disclaimer on first use). True = install.</summary>
    Task<bool> ConfirmInstallAsync(InstallConfirmation confirmation);

    /// <summary>Generic yes/no question; <paramref name="primaryText"/> is the button that proceeds.</summary>
    Task<bool> ConfirmAsync(string title, string message, string primaryText);

    /// <summary>Shows the install history; returns the entry the user wants to roll back, or <c>null</c>.</summary>
    Task<Guid?> ShowInstallHistoryAsync(IReadOnlyList<InstallHistoryItem> items);
}
