using DriverCompass.Core.Abstractions;
using DriverCompass.Core.Models;

namespace DriverCompass.Core.Installation;

/// <summary>
/// Stand-in for stage 3 in the Microsoft Store edition, which installs no drivers:
/// the install log is always empty, and the UI never offers "Install …", so <see cref="RunAsync"/> is never reached.
/// </summary>
public sealed class NoInstallation : IElevatedInstallerClient, IInstallLog
{
    public IReadOnlyList<InstallLogEntry> ReadAll() => [];

    public Task<InstallResult> RunAsync(InstallRequest request, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("This edition of DriverCompass does not install drivers.");
}

/// <summary>Update hint stand-in for the Microsoft Store edition: the Store keeps the app up to date itself.</summary>
public sealed class NoAppUpdateChecker : IAppUpdateChecker
{
    public Task<AppUpdateInfo?> CheckAsync(string currentVersion, CancellationToken cancellationToken = default) =>
        Task.FromResult<AppUpdateInfo?>(null);
}
