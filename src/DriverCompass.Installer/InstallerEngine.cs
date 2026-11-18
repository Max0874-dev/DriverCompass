using System.Text.RegularExpressions;
using DriverCompass.Core.Installation;
using DriverCompass.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DriverCompass.Installer;

/// <summary>
/// Runs inside the elevated process. Trusts nothing from the request except IDs:
/// re-reads the device, lets the package source resolve and verify the package (Windows Update, Dell, HP),
/// creates a restore point (or stops unless the user allowed to continue without), installs ONE package,
/// records what changed in the driver store and writes the log.
/// </summary>
public sealed partial class InstallerEngine
{
    private const int PnpUtilRebootRequired = 3010;

    private readonly IDeviceQuery _devices;
    private readonly IReadOnlyDictionary<string, IPackageSource> _sources;
    private readonly IRestorePointCreator _restorePoints;
    private readonly IDriverStore _driverStore;
    private readonly IInstallStore _store;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;

    public InstallerEngine(
        IDeviceQuery devices,
        IEnumerable<IPackageSource> sources,
        IRestorePointCreator restorePoints,
        IDriverStore driverStore,
        IInstallStore store,
        TimeProvider? time = null,
        ILogger<InstallerEngine>? logger = null)
    {
        _devices = devices;
        _sources = sources.ToDictionary(s => s.SourceId, StringComparer.Ordinal);
        _restorePoints = restorePoints;
        _driverStore = driverStore;
        _store = store;
        _time = time ?? TimeProvider.System;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public async Task<InstallResult> ExecuteAsync(InstallRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = InstallProtocol.Validate(request) is { } error
            ? Fail(request, error)
            : request.Action == InstallAction.Rollback ? Rollback(request) : await InstallAsync(request, cancellationToken).ConfigureAwait(false);
        _store.WriteResult(result);
        return result;
    }

    private async Task<InstallResult> InstallAsync(InstallRequest request, CancellationToken cancellationToken)
    {
        var instanceId = request.DeviceInstanceId!;
        var device = _devices.Find(instanceId);
        if (device is null)
        {
            return Stop(request, InstallOutcome.DeviceMismatch, "DeviceNotFound");
        }

        if (!_sources.TryGetValue(request.Source, out var source))
        {
            return Stop(request, InstallOutcome.Failed, "UnsupportedSource");
        }

        // Resolve, download and verify first – a failure here costs no restore point and changes nothing.
        PrepareResult prepared;
        try
        {
            prepared = await source.PrepareAsync(request, device, cancellationToken).ConfigureAwait(false);
        }
        catch (VersionSourceException ex)
        {
            _logger.LogWarning(ex, "Preparing {Source} package failed: {Reason}", request.Source, ex.Reason);
            return Stop(request, InstallOutcome.Failed, ex.Reason);
        }

        if (prepared.Package is not { } package)
        {
            return Stop(request, prepared.Outcome, prepared.Reason ?? "NotPrepared");
        }

        try
        {
            var restorePoint = _restorePoints.Create($"DriverCompass: {Shorten(package.Title ?? device.Name ?? instanceId)}");
            if (!restorePoint.Created && !request.AllowWithoutRestorePoint)
            {
                _logger.LogWarning("No restore point ({Reason}) and no permission to continue – stopping", restorePoint.Reason);
                return Log(request, device, package, InstallOutcome.RestorePointFailed, restorePoint, [], null, "NoRestorePoint");
            }

            var before = _driverStore.Snapshot();
            _logger.LogInformation("Installing {Source} package {Package} ({Title}) for {Device}", package.Source, package.PackageId, package.Title, instanceId);
            var installed = source.Install(package);
            var after = _driverStore.Snapshot();

            var published = after
                .Where(kv => !before.TryGetValue(kv.Key, out var hash) || hash != kv.Value)
                .Where(kv => package.ExpectedInfHashes is null || package.ExpectedInfHashes.Contains(kv.Value))
                .Select(kv => new PublishedInf(kv.Key, kv.Value))
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var outcome = installed.Succeeded
                ? installed.RebootRequired ? InstallOutcome.RebootRequired : InstallOutcome.Installed
                : installed.Code == UpdateCodes.NoDeviceUpdated ? InstallOutcome.NotInstallable : InstallOutcome.Failed;

            if (!installed.Succeeded && package.ExpectedInfHashes is not null && published.Count > 0)
            {
                // Our own package went into the store but no device uses it – take exactly it out again.
                foreach (var inf in published)
                {
                    _logger.LogInformation("Removing unused package {Inf}: pnputil exit {Exit}", inf.Name, _driverStore.DeleteDriver(inf.Name));
                }

                published = [];
            }

            return Log(request, device, package, outcome, restorePoint, published, _devices.Find(instanceId), installed.Code);
        }
        finally
        {
            source.Cleanup(package);
        }
    }

    private InstallResult Rollback(InstallRequest request)
    {
        var log = _store.ReadLog();
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

            var current = _driverStore.HashOf(inf.Name);
            if (current is null || !string.Equals(current, inf.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return RollbackResult(request, entry, InstallOutcome.Failed, "InfChanged");
            }
        }

        var reboot = false;
        foreach (var inf in entry.PublishedInfs)
        {
            var exit = _driverStore.DeleteDriver(inf.Name);
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
        PreparedPackage package,
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
            Source = package.Source,
            UpdateId = package.PackageId,
            Title = package.Title,
            PreviousInfName = device.Driver?.InfName,
            PreviousVersion = device.Driver?.Version,
            InstalledVersion = deviceAfter?.Driver?.Version,
            PublishedInfs = published,
            RestorePointCreated = restorePoint.Created,
            Message = message,
        };

        _store.AppendLog(entry);
        return new InstallResult
        {
            RequestId = request.RequestId,
            Outcome = outcome,
            Message = message,
            RestorePoint = restorePoint,
            LogEntryId = entry.Id,
            Title = package.Title,
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

        _store.AppendLog(entry);
        return new InstallResult { RequestId = request.RequestId, Outcome = outcome, Message = message, LogEntryId = entry.Id, Title = original?.Title };
    }

    private static string Shorten(string text) => text.Length <= 200 ? text : text[..200];

    [GeneratedRegex(@"^oem\d{1,5}\.inf$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PublishedNameRegex();
}

/// <summary>Well-known result codes of package sources.</summary>
public static class UpdateCodes
{
    /// <summary>pnputil added the package but no device uses it (Windows does not consider it better).</summary>
    public const string NoDeviceUpdated = "NoDeviceUpdated";
}
