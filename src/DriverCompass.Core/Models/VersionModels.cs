namespace DriverCompass.Core.Models;

// ---------- Stage 2 – version comparison ----------

public enum VersionCheckState
{
    /// <summary>No reliable source knows this device – UI shows "Version unknown, please check the link".</summary>
    Unknown = 0,

    /// <summary>The source has no newer driver than the installed one.</summary>
    UpToDate,

    /// <summary>The source offers a newer driver.</summary>
    NewerAvailable,

    /// <summary>The source could not be checked (offline, invalid data …).</summary>
    Failed,
}

/// <summary>What one source (or the merged result of all sources) says about one device.</summary>
/// <param name="ProviderId">Source id, see <see cref="VersionSourceIds"/>. Empty when no source knew the device.</param>
/// <param name="State">Outcome.</param>
/// <param name="LatestVersion">Newest driver version the source knows (may be <c>null</c>, e.g. Windows Update only reports a date).</param>
/// <param name="LatestDate">Date of that driver, if known.</param>
/// <param name="SourceUri">Official page of the driver (HTTPS, allow-listed), if the source provides one.</param>
/// <param name="PackageName">Name of the package / update in the source, for display.</param>
public sealed record VersionCheckResult(
    string ProviderId,
    VersionCheckState State,
    string? LatestVersion = null,
    DateOnly? LatestDate = null,
    Uri? SourceUri = null,
    string? PackageName = null)
{
    /// <summary>Stage 3: ID the source uses for the package (Windows Update: UpdateID). Set only where DriverCompass can install it.</summary>
    public string? PackageId { get; init; }

    /// <summary>Stage 3: Windows Update revision number belonging to <see cref="PackageId"/>.</summary>
    public int PackageRevision { get; init; }

    /// <summary>True if DriverCompass can install this newer driver itself (stage 3, Windows Update only).</summary>
    public bool IsInstallable => State == VersionCheckState.NewerAvailable && ProviderId == VersionSourceIds.WindowsUpdate && PackageId is not null;

    public static VersionCheckResult None { get; } = new(string.Empty, VersionCheckState.Unknown);
}

/// <summary>Well-known source ids. They are also used in the settings file and as resource-key suffixes.</summary>
public static class VersionSourceIds
{
    public const string WindowsUpdate = "windowsupdate";
    public const string Dell = "dell";
    public const string Lenovo = "lenovo";
    public const string Hp = "hp";

    /// <summary>Display order in the settings.</summary>
    public static IReadOnlyList<string> All { get; } = [WindowsUpdate, Dell, Lenovo, Hp];
}

/// <param name="Id">One of <see cref="VersionSourceIds"/>.</param>
/// <param name="TransmitsDeviceData">True if using the source tells a third party something about this PC (model, hardware IDs).</param>
public sealed record VersionSourceInfo(string Id, bool TransmitsDeviceData);

public enum VersionSourceOutcome
{
    Ok = 0,

    /// <summary>The source does not cover this PC (e.g. Dell catalog on a Lenovo PC, model not in the catalog).</summary>
    NotApplicable,

    Failed,
}

/// <summary>Per-source summary of one version check run.</summary>
/// <param name="SourceId">One of <see cref="VersionSourceIds"/>.</param>
/// <param name="Outcome">Ok, not applicable or failed.</param>
/// <param name="Matches">Number of devices the source knew.</param>
/// <param name="Detail">Catalog version / date for <see cref="VersionSourceOutcome.Ok"/>, or an error / reason code (see <see cref="VersionSourceReasons"/>).</param>
public sealed record VersionSourceStatus(string SourceId, VersionSourceOutcome Outcome, string? Detail = null, int Matches = 0);

/// <summary>Reason codes for <see cref="VersionSourceStatus.Detail"/> (localized by the app).</summary>
public static class VersionSourceReasons
{
    public const string WrongManufacturer = "WrongManufacturer";
    public const string ModelNotInCatalog = "ModelNotInCatalog";
    public const string Offline = "Offline";
    public const string InvalidData = "InvalidData";
    public const string SignatureInvalid = "SignatureInvalid";
    public const string HashMismatch = "HashMismatch";
    public const string AccessDenied = "AccessDenied";
    public const string Unexpected = "Unexpected";
}

/// <summary>What a provider returns for one run.</summary>
/// <param name="Results">Keyed by <see cref="DeviceInfo.InstanceId"/>; only devices the source knows.</param>
/// <param name="Outcome">Ok or NotApplicable.</param>
/// <param name="Detail">Catalog version, or a <see cref="VersionSourceReasons"/> code.</param>
public sealed record VersionProviderResult(
    IReadOnlyDictionary<string, VersionCheckResult> Results,
    VersionSourceOutcome Outcome = VersionSourceOutcome.Ok,
    string? Detail = null)
{
    public static VersionProviderResult NotApplicable(string reason) =>
        new(new Dictionary<string, VersionCheckResult>(), VersionSourceOutcome.NotApplicable, reason);
}

/// <summary>Thrown by providers for expected failures; <see cref="Reason"/> is a <see cref="VersionSourceReasons"/> code.</summary>
public sealed class VersionSourceException(string reason, string message, Exception? inner = null) : Exception(message, inner)
{
    public string Reason { get; } = reason;
}

public sealed record VersionCheckProgress(string SourceId, int SourceIndex, int SourceCount);

/// <summary>Result of "Check versions".</summary>
public sealed record VersionCheckRun(
    IReadOnlyList<DeviceAssessment> Devices,
    IReadOnlyList<VersionSourceStatus> Sources,
    DateTimeOffset CompletedAt)
{
    public int NewerCount => Devices.Count(d => !d.IsSystemDevice && d.VersionCheck?.State == VersionCheckState.NewerAvailable);
    public int UpToDateCount => Devices.Count(d => !d.IsSystemDevice && d.VersionCheck?.State == VersionCheckState.UpToDate);
    public int UnknownCount => Devices.Count(d => !d.IsSystemDevice && d.VersionCheck?.State is VersionCheckState.Unknown or VersionCheckState.Failed);
}
