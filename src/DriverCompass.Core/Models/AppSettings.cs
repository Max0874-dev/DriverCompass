namespace DriverCompass.Core.Models;

public enum ViewMode
{
    Simple = 0,
    Expert = 1,
}

/// <summary>User settings, persisted as JSON in %LOCALAPPDATA%\DriverCompass\settings.json.</summary>
public sealed record AppSettings
{
    /// <summary>"system", "de-DE" or "en-US".</summary>
    public string Language { get; init; } = "system";

    public ViewMode StartView { get; init; } = ViewMode.Simple;

    public bool ShowSystemDevices { get; init; }

    /// <summary>Use the serial number / service tag in OEM support links (only opened on click).</summary>
    public bool UseSerialNumberInLinks { get; init; } = true;

    /// <summary>Include the serial number in HTML/CSV exports (off by default – reports get shared).</summary>
    public bool IncludeSerialNumberInExports { get; init; }

    /// <summary>Load the newest vendor list from GitHub at startup.</summary>
    public bool UpdateCatalogOnline { get; init; } = true;

    public static AppSettings Default { get; } = new();
}
