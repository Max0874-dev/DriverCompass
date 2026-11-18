using DriverCompass.Core.Models;
using DriverCompass.Core.Versions;

namespace DriverCompass.Installer.Sources;

/// <summary>
/// Windows Update drivers (stage 3 part 1). Everything is synchronous on purpose: the WUA COM objects stay on
/// the elevated process's STA thread.
/// </summary>
public sealed class WindowsUpdateSource(IWindowsUpdateInstaller windowsUpdate) : IPackageSource
{
    public string SourceId => VersionSourceIds.WindowsUpdate;

    public Task<PrepareResult> PrepareAsync(InstallRequest request, DeviceInfo device, CancellationToken cancellationToken = default) =>
        Task.FromResult(Prepare(request, device));

    private PrepareResult Prepare(InstallRequest request, DeviceInfo device)
    {
        var update = windowsUpdate.Find(request.UpdateId!);
        if (update is null)
        {
            return PrepareResult.Stop(InstallOutcome.NotOffered, "UpdateNotOffered");
        }

        if (!update.IsDriver)
        {
            return PrepareResult.Stop(InstallOutcome.DeviceMismatch, "NotADriver");
        }

        var deviceIds = VersionMatching.MatchableIds(device).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (update.DriverHardwareId is null || !deviceIds.Contains(update.DriverHardwareId))
        {
            return PrepareResult.Stop(InstallOutcome.DeviceMismatch, "HardwareIdMismatch");
        }

        if (!update.EulaAccepted)
        {
            // Accepting license terms is the user's decision in Windows Update itself – never done silently here.
            return PrepareResult.Stop(InstallOutcome.Failed, "EulaRequired");
        }

        return PrepareResult.Ready(new PreparedPackage(SourceId, update.UpdateId, update.Title, null, null, update));
    }

    public UpdateInstallResult Install(PreparedPackage package) => windowsUpdate.Install((OfferedUpdate)package.State!);

    public void Cleanup(PreparedPackage package)
    {
        // Nothing to clean up – Windows Update manages its own download cache.
    }
}
