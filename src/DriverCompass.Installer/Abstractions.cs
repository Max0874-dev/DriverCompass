using DriverCompass.Core.Models;

namespace DriverCompass.Installer;

/// <summary>Reads a device fresh from Windows (WMI) – never from the app's request.</summary>
public interface IDeviceQuery
{
    DeviceInfo? Find(string instanceId);
}

/// <summary>A driver update as Windows Update itself describes it right now.</summary>
public sealed record OfferedUpdate(
    string UpdateId,
    int Revision,
    bool IsDriver,
    bool EulaAccepted,
    string? Title,
    string? DriverHardwareId,
    string? DriverProvider,
    DateOnly? DriverDate);

/// <param name="Succeeded">Download and installation reported success.</param>
/// <param name="RebootRequired">Windows needs a restart to finish.</param>
/// <param name="Code">Windows Update result code / HRESULT for the log.</param>
public sealed record UpdateInstallResult(bool Succeeded, bool RebootRequired, string Code);

/// <summary>Windows Update Agent: search one update by ID, then download + install exactly that one.</summary>
public interface IWindowsUpdateInstaller
{
    OfferedUpdate? Find(string updateId);

    /// <summary>Installs the update returned by the last <see cref="Find"/> call.</summary>
    UpdateInstallResult Install(OfferedUpdate update);
}

public interface IRestorePointCreator
{
    RestorePointInfo Create(string description);
}

/// <summary>The Windows driver store as seen through C:\Windows\INF\oem*.inf and pnputil.</summary>
public interface IDriverStore
{
    /// <summary>Published driver packages: name (oemNN.inf) → SHA-256.</summary>
    IReadOnlyDictionary<string, string> Snapshot();

    string? HashOf(string publishedName);

    /// <summary><c>pnputil /delete-driver &lt;name&gt; /uninstall</c>; returns the exit code.</summary>
    int DeleteDriver(string publishedName);
}

/// <summary>The admin-only folder %ProgramData%\DriverCompass: install log and results.</summary>
public interface IInstallStore
{
    IReadOnlyList<InstallLogEntry> ReadLog();

    void AppendLog(InstallLogEntry entry);

    void WriteResult(InstallResult result);
}
