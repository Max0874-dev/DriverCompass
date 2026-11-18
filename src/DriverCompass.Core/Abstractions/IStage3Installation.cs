using DriverCompass.Core.Models;

namespace DriverCompass.Core.Abstractions;

// =====================================================================
// Stage 3 – download & installation. Interfaces only, NOT implemented.
// Rules: one driver at a time, explicit user confirmation, HTTPS from
// allow-listed domains only, signature + hash check, restore point,
// elevation only in the separate DriverCompass.Installer process.
// =====================================================================

public interface IDriverDownloader
{
    Task<DownloadedPackage> DownloadAsync(DriverPackageRef package, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>Checks HTTPS origin, domain allow-list, SHA-256 and Authenticode/catalog signature.</summary>
public interface IPackageVerifier
{
    VerificationResult Verify(DownloadedPackage package);
}

public interface IRestorePointService
{
    Task<RestorePointResult> CreateAsync(string description, CancellationToken cancellationToken = default);
}

/// <summary>Unelevated client that starts DriverCompass.Installer.exe via UAC ("runas") for ONE confirmed driver.</summary>
public interface IElevatedInstallerClient
{
    Task<InstallResult> InstallAsync(InstallRequest request, CancellationToken cancellationToken = default);
}

public interface IInstallLog
{
    Task AppendAsync(InstallLogEntry entry, CancellationToken cancellationToken = default);

    IAsyncEnumerable<InstallLogEntry> ReadAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>Rollback via <c>pnputil /delete-driver oemNN.inf /uninstall</c>.</summary>
public interface IRollbackService
{
    Task<InstallResult> RollbackAsync(InstallLogEntry entry, CancellationToken cancellationToken = default);
}
