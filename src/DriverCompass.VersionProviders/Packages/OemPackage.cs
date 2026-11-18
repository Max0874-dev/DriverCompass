using DriverCompass.Core.Models;

namespace DriverCompass.VersionProviders.Packages;

/// <summary>
/// A vendor driver package as described by the vendor's signed catalog (stage 3, Dell/HP).
/// Everything here comes from the catalog – nothing from the unelevated app.
/// </summary>
/// <param name="Source">Version source id (dell/hp).</param>
/// <param name="PackageId">Dell component path or HP SoftPaq ID.</param>
/// <param name="DownloadUri">HTTPS address on <paramref name="AllowedHosts"/>.</param>
/// <param name="Sha256">Hex SHA-256 the download must have.</param>
/// <param name="Size">Size from the catalog, if given.</param>
/// <param name="Title">Package name for display and log.</param>
/// <param name="Version">Driver version the package installs.</param>
/// <param name="HardwareIds">Devices the catalog says the package is for.</param>
/// <param name="Signers">Accepted Authenticode signer names of the package EXE.</param>
/// <param name="AllowedHosts">Exact hosts the download may use.</param>
/// <param name="Extraction">How the driver files are extracted.</param>
/// <param name="InstallerFileName">File name the package EXE must have (Lenovo).</param>
public sealed record OemPackage(
    string Source,
    string PackageId,
    Uri DownloadUri,
    string Sha256,
    long? Size,
    string? Title,
    string? Version,
    IReadOnlyList<string> HardwareIds,
    IReadOnlyList<string> Signers,
    IReadOnlyCollection<string> AllowedHosts,
    PackageExtraction Extraction = PackageExtraction.SevenZip,
    string? InstallerFileName = null);

/// <summary>How the driver files get out of the vendor EXE.</summary>
public enum PackageExtraction
{
    /// <summary>Dell / HP: appended 7z archive, extracted with Windows' tar.exe – no vendor code runs.</summary>
    SevenZip = 0,

    /// <summary>Lenovo: Inno Setup package; Lenovo's own signed EXE extracts itself (/EXTRACT=YES).</summary>
    LenovoInno = 1,
}

/// <summary>Looks up an installable package in a vendor's signed catalog.</summary>
public interface IOemPackageCatalog
{
    string SourceId { get; }

    bool IsApplicable(SystemInfo system);

    /// <summary><c>null</c> if the catalog for this PC has no such driver package.</summary>
    Task<OemPackage?> FindPackageAsync(SystemInfo system, string packageId, CancellationToken cancellationToken = default);
}
