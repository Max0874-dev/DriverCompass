namespace DriverCompass.Core.Models;

// ---------- Stage 3 (download & install) – models only, not implemented yet ----------

public sealed record DriverPackageRef(
    string DeviceInstanceId,
    Uri DownloadUri,
    string? ExpectedSha256,
    string? Version,
    string SourceProviderId);

public sealed record DownloadedPackage(DriverPackageRef Reference, string LocalPath, string Sha256);

public sealed record VerificationResult(bool IsTrusted, string? Signer, IReadOnlyList<string> Problems);

public sealed record RestorePointResult(bool Created, string? Warning);

public sealed record InstallRequest(DownloadedPackage Package, string InfRelativePath, bool RestorePointConfirmed);

public sealed record InstallResult(bool Success, int ExitCode, string? PublishedInfName, string? Message);

public sealed record InstallLogEntry(
    DateTimeOffset Timestamp,
    string DeviceInstanceId,
    string? PreviousInfName,
    string? PreviousVersion,
    string? InstalledInfName,
    string? InstalledVersion,
    string SourceUri,
    string Sha256,
    bool Success,
    string? Message);
