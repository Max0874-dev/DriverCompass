using DriverCompass.Core.Models;

namespace DriverCompass.Installer;

/// <summary>Reads a device fresh from Windows (WMI) – never from the app's request.</summary>
public interface IDeviceQuery
{
    DeviceInfo? Find(string instanceId);
}

/// <summary>Reads the PC identity fresh from Windows (manufacturer, SKU, mainboard, Windows release).</summary>
public interface ISystemQuery
{
    SystemInfo Read();
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
/// <param name="Code">Result code for the log ("Install:2:0x…", "pnputil:0", "NoDeviceUpdated" …).</param>
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

    /// <summary><c>pnputil /add-driver &lt;inf&gt; /install</c>; returns the exit code.</summary>
    int AddDriver(string infPath);

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

/// <summary>A package that passed every check and is ready to install (nothing has been changed yet).</summary>
/// <param name="Source">Version source id.</param>
/// <param name="PackageId">UpdateID (Windows Update), component path (Dell) or SoftPaq ID (HP).</param>
/// <param name="Title">Name for log and messages.</param>
/// <param name="Version">Driver version, if known.</param>
/// <param name="ExpectedInfHashes">
/// SHA-256 of the INF files that will be added. Windows copies an INF byte for byte to oemNN.inf, so only new
/// oem INFs with one of these hashes are attributed to this installation. <c>null</c> = take every new oem INF.
/// </param>
/// <param name="State">Source-specific data (the Windows Update object, the chosen INF paths …).</param>
public sealed record PreparedPackage(
    string Source,
    string PackageId,
    string? Title,
    string? Version,
    IReadOnlySet<string>? ExpectedInfHashes,
    object? State);

/// <summary>Result of <see cref="IPackageSource.PrepareAsync"/>: either a package or why not (nothing changed).</summary>
public sealed record PrepareResult(PreparedPackage? Package, InstallOutcome Outcome = InstallOutcome.Failed, string? Reason = null)
{
    public static PrepareResult Ready(PreparedPackage package) => new(package);

    public static PrepareResult Stop(InstallOutcome outcome, string reason) => new(null, outcome, reason);
}

/// <summary>
/// Where an installable driver comes from (Windows Update, Dell, HP). <see cref="PrepareAsync"/> resolves,
/// downloads and verifies everything BEFORE the restore point is created; <see cref="Install"/> then changes Windows.
/// </summary>
public interface IPackageSource
{
    string SourceId { get; }

    Task<PrepareResult> PrepareAsync(InstallRequest request, DeviceInfo device, CancellationToken cancellationToken = default);

    UpdateInstallResult Install(PreparedPackage package);

    /// <summary>Removes temporary files (extracted packages). Called in every case after <see cref="PrepareAsync"/> succeeded.</summary>
    void Cleanup(PreparedPackage package);
}
