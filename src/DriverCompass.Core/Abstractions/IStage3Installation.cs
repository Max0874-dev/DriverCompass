using DriverCompass.Core.Models;

namespace DriverCompass.Core.Abstractions;

// =====================================================================
// Stage 3 – installation.
// Rules: one driver at a time, explicit user confirmation, restore point
// (or the user's explicit OK without one), elevation only in a separate
// process ("DriverCompass.exe --elevated") that re-checks everything itself,
// install log + rollback via pnputil.
// Part 1 (implemented): Windows Update drivers.
// =====================================================================

/// <summary>Unelevated side: hands ONE request to the elevated process (UAC prompt) and returns its result.</summary>
public interface IElevatedInstallerClient
{
    Task<InstallResult> RunAsync(InstallRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Read access to the install log (written only by the elevated process).</summary>
public interface IInstallLog
{
    IReadOnlyList<InstallLogEntry> ReadAll();
}

// ---------- Later: OEM packages (Dell/Lenovo/HP) – interfaces only, not implemented ----------

public interface IDriverDownloader
{
    Task<DownloadedPackage> DownloadAsync(DriverPackageRef package, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>Checks HTTPS origin, domain allow-list, SHA-256 and Authenticode/catalog signature.</summary>
public interface IPackageVerifier
{
    VerificationResult Verify(DownloadedPackage package);
}
