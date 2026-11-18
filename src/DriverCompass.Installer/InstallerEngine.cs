using System.Text.RegularExpressions;
using DriverCompass.Core.Installation;
using DriverCompass.Core.Models;
using DriverCompass.Core.Versions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DriverCompass.Installer;

/// <summary>
/// Runs inside the elevated process. Trusts nothing from the request except IDs:
/// re-reads the device, re-searches Windows Update, checks that the update is a driver for exactly
/// this device, creates a restore point (or stops unless the user allowed to continue without),
/// installs ONE update, records what changed in the driver store and writes the log.
/// </summary>
public sealed partial class InstallerEngine(
    IDeviceQuery devices,
    IWindowsUpdateInstaller windowsUpdate,
    IRestorePointCreator restorePoints,
    IDriverStore driverStore,
    IInstallStore store,
    TimeProvider? time = null,
    ILogger<InstallerEngine>? logger = null)
{
    private const int PnpUtilRebootRequired = 3010;

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly ILogger _logger = (ILogger?)logger ?? NullLogger.Instance;

    public InstallResult Execute(InstallRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = InstallProtocol.Validate(request) is { } error
            ? Fail(request, error)
            : request.Action == InstallAction.Rollback ? Rollback(request) : Install(request);
        store.WriteResult(result);
        return result;
    }

    private InstallResult Install(InstallRequest request)
    {
        var instanceId = request.DeviceInstanceId!;
        var device = devices.Find(instanceId);
        if (device is null)
        {
            return Stop(request, InstallOutcome.DeviceMismatch, "DeviceNotFound");
        }

        var update = windowsUpdate.Find(request.UpdateId!);
        if (update is null)
        {
            return Stop(request, InstallOutcome.NotOffered, "UpdateNotOffered");
        }

        if (!update.IsDriver)
        {
            return Stop(request, InstallOutcome.DeviceMismatch, "NotADriver");
        }

        var deviceIds = VersionMatching.MatchableIds(device).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (update.DriverHardwareId is null || !deviceIds.Contains(update.DriverHardwareId))
        {
            return Stop(request, InstallOutcome.DeviceMismatch, "HardwareIdMismatch");
        }

        if (!update.EulaAccepted)
        {
            // Accepting license terms is the user's decision in Windows Update itself – never done silently here.
            return Stop(request, InstallOutcome.Failed, "EulaRequired");
        }

        var restorePoint = restorePoints.Create($"DriverCompass: {Shorten(update.Title ?? device.Name ?? instanceId)}");
        if (!restorePoint.Created && !request.AllowWithoutRestorePoint)
        {
            _logger.LogWarning("No restore point ({Reason}) and no permission to continue – stopping", restorePoint.Reason);
            return Log(request, device, update, InstallOutcome.RestorePointFailed, restorePoint, [], null, "NoRestorePoint");
        }

        var before = driverStore.Snapshot();
        _logger.LogInformation("Installing Windows Update {UpdateId} ({Title}) for {Device}", update.UpdateId, update.Title, instanceId);
        var installed = windowsUpdate.Install(update);
        var after = driverStore.Snapshot();

        var published = after
            .Where(kv => !before.TryGetValue(kv.Key, out var hash) || hash != kv.Value)
            .Select(kv => new PublishedInf(kv.Key, kv.Value))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var outcome = !installed.Succeeded
            ? InstallOutcome.Failed
            : installed.RebootRequired ? InstallOutcome.RebootRequired : InstallOutcome.Installed;

        return Log(request, device, update, outcome, restorePoint, published, devices.Find(instanceId), installed.Code);
    }

    private InstallResult Rollback(InstallRequest request)
    {
        var log = store.ReadLog();
        var entry = log.FirstOrDefault(e => e.Id == request.LogEntryId);
        if (entry is null || !InstallProtocol.CanRollBack(entry, log))
        {
            return RollbackResult(request, entry, InstallOutcome.Failed, "EntryNotRollbackable");
        }

        // Only remove exactly what this installation added – and only if it is still the very same file.
        foreach (var inf in entry.PublishedInfs)
        {
            if (!PublishedNameRegex().IsMatch(inf.Name))
            {
                return RollbackResult(request, entry, InstallOutcome.Failed, "InvalidInfName");
            }

            var current = driverStore.HashOf(inf.Name);
            if (current is null || !string.Equals(current, inf.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return RollbackResult(request, entry, InstallOutcome.Failed, "InfChanged");
            }
        }

        var reboot = false;
        foreach (var inf in entry.PublishedInfs)
        {
            var exit = driverStore.DeleteDriver(inf.Name);
            _logger.LogInformation("pnputil /delete-driver {Inf} /uninstall → {Exit}", inf.Name, exit);
            if (exit == PnpUtilRebootRequired)
            {
                reboot = true;
            }
            else if (exit != 0)
            {
                return RollbackResult(request, entry, InstallOutcome.Failed, $"pnputil:{exit}");
            }
        }

        return RollbackResult(request, entry, reboot ? InstallOutcome.RebootRequired : InstallOutcome.RolledBack, null);
    }

    private InstallResult Stop(InstallRequest request, InstallOutcome outcome, string reason)
    {
        _logger.LogWarning("Install request {Request} stopped: {Reason}", request.RequestId, reason);
        return new InstallResult { RequestId = request.RequestId, Outcome = outcome, Message = reason };
    }

    private static InstallResult Fail(InstallRequest request, string reason) =>
        new() { RequestId = request.RequestId, Outcome = InstallOutcome.Failed, Message = reason };

    private InstallResult Log(
        InstallRequest request,
        DeviceInfo device,
        OfferedUpdate update,
        InstallOutcome outcome,
        RestorePointInfo restorePoint,
        IReadOnlyList<PublishedInf> published,
        DeviceInfo? deviceAfter,
        string? message)
    {
        var entry = new InstallLogEntry
        {
            Id = Guid.NewGuid(),
            Timestamp = _time.GetLocalNow(),
            Action = InstallAction.Install,
            Outcome = outcome,
            RequestId = request.RequestId,
            DeviceInstanceId = device.InstanceId,
            DeviceName = device.Name,
            Source = VersionSourceIds.WindowsUpdate,
            UpdateId = update.UpdateId,
            Title = update.Title,
            PreviousInfName = device.Driver?.InfName,
            PreviousVersion = device.Driver?.Version,
            InstalledVersion = deviceAfter?.Driver?.Version,
            PublishedInfs = published,
            RestorePointCreated = restorePoint.Created,
            Message = message,
        };

        store.AppendLog(entry);
        return new InstallResult
        {
            RequestId = request.RequestId,
            Outcome = outcome,
            Message = message,
            RestorePoint = restorePoint,
            LogEntryId = entry.Id,
            Title = update.Title,
            InstalledVersion = entry.InstalledVersion,
        };
    }

    private InstallResult RollbackResult(InstallRequest request, InstallLogEntry? original, InstallOutcome outcome, string? message)
    {
        var entry = new InstallLogEntry
        {
            Id = Guid.NewGuid(),
            Timestamp = _time.GetLocalNow(),
            Action = InstallAction.Rollback,
            Outcome = outcome,
            RequestId = request.RequestId,
            DeviceInstanceId = original?.DeviceInstanceId,
            DeviceName = original?.DeviceName,
            Source = original?.Source,
            UpdateId = original?.UpdateId,
            Title = original?.Title,
            PublishedInfs = original?.PublishedInfs ?? [],
            RolledBackEntryId = request.LogEntryId,
            Message = message,
        };

        store.AppendLog(entry);
        return new InstallResult { RequestId = request.RequestId, Outcome = outcome, Message = message, LogEntryId = entry.Id, Title = original?.Title };
    }

    private static string Shorten(string text) => text.Length <= 200 ? text : text[..200];

    [GeneratedRegex(@"^oem\d{1,5}\.inf$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PublishedNameRegex();
}
