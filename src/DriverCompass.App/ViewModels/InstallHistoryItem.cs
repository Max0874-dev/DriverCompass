using System.Globalization;
using DriverCompass.App.Services;
using DriverCompass.Core.Models;

namespace DriverCompass.App.ViewModels;

/// <summary>One row of the install history dialog.</summary>
public sealed class InstallHistoryItem(InstallLogEntry entry, bool canRollBack)
{
    public InstallLogEntry Entry { get; } = entry;

    public Guid Id => Entry.Id;

    public bool CanRollBack { get; } = canRollBack;

    public string WhenText { get; } = entry.Timestamp.LocalDateTime.ToString("g", CultureInfo.CurrentCulture);

    public string Heading { get; } =
        $"{Localizer.T($"History_Action_{entry.Action}")} · {Localizer.T($"InstallOutcomeShort_{entry.Outcome}")}";

    public string Title { get; } = entry.Title ?? entry.UpdateId ?? string.Empty;

    public string DeviceName { get; } = entry.DeviceName ?? entry.DeviceInstanceId ?? string.Empty;

    public string Details { get; } = string.Join("  ·  ", new[]
    {
        entry.PreviousVersion is not null || entry.InstalledVersion is not null
            ? Localizer.F("History_Versions", entry.PreviousVersion ?? "–", entry.InstalledVersion ?? "–")
            : null,
        entry.PublishedInfs.Count > 0 ? Localizer.F("History_Packages", string.Join(", ", entry.PublishedInfs.Select(p => p.Name))) : null,
        entry.Action == InstallAction.Install && entry.Outcome is InstallOutcome.Installed or InstallOutcome.RebootRequired && !entry.RestorePointCreated
            ? Localizer.T("History_NoRestorePoint")
            : null,
        entry.Message,
    }.Where(s => !string.IsNullOrWhiteSpace(s)));
}
