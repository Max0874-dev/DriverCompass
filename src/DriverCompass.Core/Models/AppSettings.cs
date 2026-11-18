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

    /// <summary>Ask GitHub at startup whether a newer DriverCompass release exists (off by default; only shows a link).</summary>
    public bool CheckForAppUpdates { get; init; }

    /// <summary>
    /// Version sources the user switched on (ids from <see cref="VersionSourceIds"/>).
    /// Empty by default: every source sends model or device data to a third party, so each one is opt-in.
    /// </summary>
    public IReadOnlyList<string> EnabledVersionSources { get; init; } = [];

    /// <summary>Stage 3: the user acknowledged the installation disclaimer (asked once, before the first install).</summary>
    public bool InstallDisclaimerAccepted { get; init; }

    public static AppSettings Default { get; } = new();

    // Value equality including the list contents (records compare collections by reference).
    public bool Equals(AppSettings? other) =>
        other is not null &&
        Language == other.Language &&
        StartView == other.StartView &&
        ShowSystemDevices == other.ShowSystemDevices &&
        UseSerialNumberInLinks == other.UseSerialNumberInLinks &&
        IncludeSerialNumberInExports == other.IncludeSerialNumberInExports &&
        UpdateCatalogOnline == other.UpdateCatalogOnline &&
        CheckForAppUpdates == other.CheckForAppUpdates &&
        InstallDisclaimerAccepted == other.InstallDisclaimerAccepted &&
        EnabledVersionSources.SequenceEqual(other.EnabledVersionSources, StringComparer.Ordinal);

    public override int GetHashCode() =>
        HashCode.Combine(Language, StartView, ShowSystemDevices, UseSerialNumberInLinks, IncludeSerialNumberInExports, UpdateCatalogOnline, CheckForAppUpdates, EnabledVersionSources.Count);
}
