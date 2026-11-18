namespace DriverCompass.Core.Models;

// =====================================================================
// Stage 3 – installation of ONE user-confirmed driver at a time.
// Part 1: Windows Update drivers. The elevated process (DriverCompass.exe --elevated)
// re-checks everything itself; the request only carries IDs.
// =====================================================================

public enum InstallAction
{
    Install = 0,
    Rollback = 1,
}

public enum InstallOutcome
{
    /// <summary>Driver installed, no restart needed.</summary>
    Installed = 0,

    /// <summary>Driver installed or removed; Windows needs a restart to finish.</summary>
    RebootRequired,

    /// <summary>The user declined the UAC prompt (or cancelled otherwise). Nothing changed.</summary>
    Cancelled,

    /// <summary>No restore point could be created and the user had not allowed to continue without one. Nothing changed.</summary>
    RestorePointFailed,

    /// <summary>Windows Update no longer offers this update for this PC. Nothing changed.</summary>
    NotOffered,

    /// <summary>The update is not a driver for this device (re-checked by the elevated process). Nothing changed.</summary>
    DeviceMismatch,

    /// <summary>Something went wrong; see <see cref="InstallResult.Message"/>.</summary>
    Failed,

    /// <summary>Rollback finished.</summary>
    RolledBack,

    /// <summary>The package contains no driver DriverCompass can install for this device (or an unsupported format). Nothing changed.</summary>
    NotInstallable,
}

/// <summary>
/// What the unelevated app asks for. Deliberately only IDs: the elevated process looks up the device,
/// the update and the log entry itself and never trusts names or paths from here.
/// </summary>
public sealed record InstallRequest
{
    public required Guid RequestId { get; init; }
    public required InstallAction Action { get; init; }

    /// <summary>Install: PnP instance ID of the device the update is meant for.</summary>
    public string? DeviceInstanceId { get; init; }

    /// <summary>Install: where the package comes from – "windowsupdate", "dell" or "hp" (see <see cref="VersionSourceIds"/>).</summary>
    public string Source { get; init; } = VersionSourceIds.WindowsUpdate;

    /// <summary>Install from Dell/HP: Dell component path or HP SoftPaq ID. The elevated process looks it up in the signed catalog itself.</summary>
    public string? PackageId { get; init; }

    /// <summary>Install: Windows Update UpdateID (GUID).</summary>
    public string? UpdateId { get; init; }

    /// <summary>Install: Windows Update revision number seen during the version check (informational).</summary>
    public int Revision { get; init; }

    /// <summary>Install: the user explicitly accepted to continue if no restore point can be created.</summary>
    public bool AllowWithoutRestorePoint { get; init; }

    /// <summary>Rollback: <see cref="InstallLogEntry.Id"/> of the installation to undo.</summary>
    public Guid? LogEntryId { get; init; }
}

/// <param name="Created">A new restore point exists.</param>
/// <param name="Reason">Code when not created: "Disabled", "FrequencyLimit", "Failed:&lt;code&gt;", "Skipped".</param>
/// <param name="LastRestorePoint">Newest existing restore point, if any.</param>
public sealed record RestorePointInfo(bool Created, string? Reason = null, DateTimeOffset? LastRestorePoint = null);

/// <summary>A driver package Windows published to the driver store (C:\Windows\INF\oemNN.inf).</summary>
public sealed record PublishedInf(string Name, string Sha256);

public sealed record InstallResult
{
    public required Guid RequestId { get; init; }
    public required InstallOutcome Outcome { get; init; }

    /// <summary>Technical detail (error code, HRESULT) for the log and the message.</summary>
    public string? Message { get; init; }

    public RestorePointInfo? RestorePoint { get; init; }
    public Guid? LogEntryId { get; init; }
    public string? Title { get; init; }
    public string? InstalledVersion { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool Succeeded => Outcome is InstallOutcome.Installed or InstallOutcome.RebootRequired or InstallOutcome.RolledBack;
}

/// <summary>One line of the install log (%ProgramData%\DriverCompass\install-log.jsonl, written by the elevated process only).</summary>
public sealed record InstallLogEntry
{
    public required Guid Id { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required InstallAction Action { get; init; }
    public required InstallOutcome Outcome { get; init; }
    public Guid RequestId { get; init; }

    public string? DeviceInstanceId { get; init; }
    public string? DeviceName { get; init; }
    public string? Source { get; init; }
    public string? UpdateId { get; init; }
    public string? Title { get; init; }

    public string? PreviousInfName { get; init; }
    public string? PreviousVersion { get; init; }
    public string? InstalledVersion { get; init; }

    /// <summary>Packages that were new in the driver store after the installation – what a rollback removes.</summary>
    public IReadOnlyList<PublishedInf> PublishedInfs { get; init; } = [];

    public bool RestorePointCreated { get; init; }

    /// <summary>For rollback entries: the installation that was undone.</summary>
    public Guid? RolledBackEntryId { get; init; }

    public string? Message { get; init; }
}

// ---------- Later (OEM packages from Dell/Lenovo/HP) – models only ----------

public sealed record DriverPackageRef(
    string DeviceInstanceId,
    Uri DownloadUri,
    string? ExpectedSha256,
    string? Version,
    string SourceProviderId);

public sealed record DownloadedPackage(DriverPackageRef Reference, string LocalPath, string Sha256);

public sealed record VerificationResult(bool IsTrusted, string? Signer, IReadOnlyList<string> Problems);
