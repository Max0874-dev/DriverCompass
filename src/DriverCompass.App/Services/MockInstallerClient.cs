using DriverCompass.Core.Abstractions;
using DriverCompass.Core.Models;

namespace DriverCompass.App.Services;

/// <summary>
/// Demo mode (--mock): simulates the elevated process without UAC and without touching Windows.
/// The first attempt reports "no restore point" so the warning dialog can be tried out.
/// </summary>
public sealed class MockInstallerClient : IElevatedInstallerClient, IInstallLog
{
    private readonly List<InstallLogEntry> _log = [];
    private bool _restorePointShown;

    public IReadOnlyList<InstallLogEntry> ReadAll() => [.. _log];

    public async Task<InstallResult> RunAsync(InstallRequest request, CancellationToken cancellationToken = default)
    {
        await Task.Delay(1200, cancellationToken).ConfigureAwait(true);

        if (request.Action == InstallAction.Rollback)
        {
            var original = _log.FirstOrDefault(e => e.Id == request.LogEntryId);
            var rollback = Entry(request, InstallAction.Rollback, InstallOutcome.RolledBack, original?.Title) with { RolledBackEntryId = request.LogEntryId, PublishedInfs = original?.PublishedInfs ?? [] };
            _log.Add(rollback);
            return new InstallResult { RequestId = request.RequestId, Outcome = InstallOutcome.RolledBack, LogEntryId = rollback.Id, Title = original?.Title };
        }

        var dell = request.Source is VersionSourceIds.Dell or VersionSourceIds.Lenovo;
        var title = dell ? "Intel AX210/AX211 WLAN Driver" : "Intel Corporation - Bluetooth - 23.150.0.3";
        var (before, after) = dell ? ("23.60.1.2", "23.130.0.4") : ("22.240.0.2", "23.150.0.3");
        if (!_restorePointShown && !request.AllowWithoutRestorePoint)
        {
            _restorePointShown = true;
            var failed = Entry(request, InstallAction.Install, InstallOutcome.RestorePointFailed, title);
            _log.Add(failed);
            return new InstallResult
            {
                RequestId = request.RequestId,
                Outcome = InstallOutcome.RestorePointFailed,
                RestorePoint = new RestorePointInfo(false, "FrequencyLimit", DateTimeOffset.Now.AddHours(-3)),
                LogEntryId = failed.Id,
                Title = title,
            };
        }

        var entry = Entry(request, InstallAction.Install, InstallOutcome.Installed, title) with
        {
            PreviousInfName = dell ? "oem45.inf" : "oem31.inf",
            PreviousVersion = before,
            InstalledVersion = after,
            PublishedInfs = [new PublishedInf(dell ? "oem88.inf" : "oem87.inf", new string('A', 64))],
            RestorePointCreated = !request.AllowWithoutRestorePoint,
        };
        _log.Add(entry);
        return new InstallResult { RequestId = request.RequestId, Outcome = InstallOutcome.Installed, LogEntryId = entry.Id, Title = title, InstalledVersion = after };
    }

    private static InstallLogEntry Entry(InstallRequest request, InstallAction action, InstallOutcome outcome, string? title) => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTimeOffset.Now,
        Action = action,
        Outcome = outcome,
        RequestId = request.RequestId,
        DeviceInstanceId = request.DeviceInstanceId,
        DeviceName = request.Source is VersionSourceIds.Dell or VersionSourceIds.Lenovo ? "Intel(R) Wi-Fi 6E AX210 160MHz" : "Intel(R) Wireless Bluetooth(R)",
        Source = request.Source,
        UpdateId = request.UpdateId ?? request.PackageId,
        Title = title,
    };
}
