using System.Runtime.InteropServices;
using DriverCompass.Core.Models;
using DriverCompass.Core.Versions;
using DriverCompass.Installer.Packages;
using DriverCompass.VersionProviders.Net;
using DriverCompass.VersionProviders.Packages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DriverCompass.Installer.Sources;

/// <summary>
/// Dell and HP driver packages (stage 3 part 2). Inside the elevated process:
/// signed catalog (fetched again, protected cache) → package URL + SHA-256 → HTTPS download from the vendor host
/// → SHA-256 + Authenticode signer check → 7z payload extracted with Windows' tar (no vendor code runs)
/// → only INFs for this device with a trusted catalog → pnputil.
/// </summary>
public sealed class OemPackageSource(
    IOemPackageCatalog catalog,
    ISystemQuery system,
    ICatalogDownloader downloader,
    IFileSignatureVerifier signatures,
    IPayloadExtractor extractor,
    IDriverStore driverStore,
    string workRoot,
    ILogger<OemPackageSource>? logger = null) : IPackageSource
{
    public const long MaxPackageBytes = 2L * 1024 * 1024 * 1024;
    private const int PnpUtilRebootRequired = 3010;
    private const int PnpUtilNoDeviceUpdated = 259;

    private readonly ILogger _logger = (ILogger?)logger ?? NullLogger.Instance;

    public string SourceId => catalog.SourceId;

    private sealed record State(string WorkDirectory, string PackagePath, IReadOnlyList<InfCandidate> Infs);

    public async Task<PrepareResult> PrepareAsync(InstallRequest request, DeviceInfo device, CancellationToken cancellationToken = default)
    {
        var pc = system.Read();
        if (!catalog.IsApplicable(pc))
        {
            return PrepareResult.Stop(InstallOutcome.DeviceMismatch, "WrongManufacturer");
        }

        var package = await catalog.FindPackageAsync(pc, request.PackageId!, cancellationToken).ConfigureAwait(false);
        if (package is null)
        {
            return PrepareResult.Stop(InstallOutcome.NotOffered, "PackageNotInCatalog");
        }

        var deviceIds = VersionMatching.MatchableIds(device).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!package.HardwareIds.Any(deviceIds.Contains))
        {
            return PrepareResult.Stop(InstallOutcome.DeviceMismatch, "HardwareIdMismatch");
        }

        if (package.Size > MaxPackageBytes)
        {
            return PrepareResult.Stop(InstallOutcome.NotInstallable, "PackageTooLarge");
        }

        _logger.LogInformation("Downloading {Source} package {Package} from {Host}", package.Source, package.PackageId, package.DownloadUri.Host);
        var packagePath = await downloader.DownloadAsync(
            new DownloadRequest(package.DownloadUri, package.AllowedHosts, MaxPackageBytes, package.Sha256), cancellationToken).ConfigureAwait(false);

        var signer = signatures.GetTrustedSigner(packagePath);
        if (signer is null || !package.Signers.Contains(signer, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Package signature rejected: signer {Signer}", signer ?? "(none/invalid)");
            DeleteQuietly(packagePath);
            return PrepareResult.Stop(InstallOutcome.Failed, VersionSourceReasons.SignatureInvalid);
        }

        var work = Path.Combine(workRoot, request.RequestId.ToString("N"));
        var extracted = Path.Combine(work, "extract");
        if (!extractor.TryExtract(packagePath, extracted))
        {
            Cleanup(work, packagePath);
            return PrepareResult.Stop(InstallOutcome.NotInstallable, "UnsupportedPackage");
        }

        var infs = InfMatcher.FindMatching(extracted, deviceIds, signatures.GetTrustedSigner, RuntimeInformation.OSArchitecture);
        if (infs.Count == 0)
        {
            Cleanup(work, packagePath);
            return PrepareResult.Stop(InstallOutcome.NotInstallable, "NoMatchingDriver");
        }

        _logger.LogInformation("Package {Package}: {Count} matching INF(s), catalog signer {Signer}", package.PackageId, infs.Count, infs[0].CatalogSigner);
        return PrepareResult.Ready(new PreparedPackage(
            package.Source,
            package.PackageId,
            package.Title,
            package.Version,
            infs.Select(i => i.Sha256).ToHashSet(StringComparer.OrdinalIgnoreCase),
            new State(work, packagePath, infs)));
    }

    public UpdateInstallResult Install(PreparedPackage package)
    {
        var state = (State)package.State!;
        var codes = state.Infs.Select(inf =>
        {
            var exit = driverStore.AddDriver(inf.InfPath);
            _logger.LogInformation("pnputil /add-driver {Inf} /install → {Exit}", Path.GetFileName(inf.InfPath), exit);
            return exit;
        }).ToList();

        var code = "pnputil:" + string.Join(',', codes);
        if (codes.All(c => c == PnpUtilNoDeviceUpdated))
        {
            return new UpdateInstallResult(false, false, UpdateCodes.NoDeviceUpdated);
        }

        var ok = codes.All(c => c is 0 or PnpUtilRebootRequired or PnpUtilNoDeviceUpdated);
        return new UpdateInstallResult(ok, codes.Contains(PnpUtilRebootRequired), code);
    }

    public void Cleanup(PreparedPackage package)
    {
        if (package.State is State state)
        {
            Cleanup(state.WorkDirectory, state.PackagePath);
        }
    }

    private static void Cleanup(string workDirectory, string packagePath)
    {
        try
        {
            if (Directory.Exists(workDirectory))
            {
                Directory.Delete(workDirectory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left in the protected work folder; removed with the next run's cleanup at the latest.
        }

        // Packages can be large – do not keep them in the cache.
        DeleteQuietly(packagePath);
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ignore – cache file in the protected folder.
        }
    }
}
