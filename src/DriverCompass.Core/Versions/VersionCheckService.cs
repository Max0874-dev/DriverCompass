using DriverCompass.Core.Abstractions;
using DriverCompass.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DriverCompass.Core.Versions;

/// <summary>
/// Runs the enabled providers one after another and merges their answers per device:
/// newer version (highest wins) → up to date → unknown. A failing provider never stops the others.
/// Nothing runs unless the user enabled the source and clicked "Check versions".
/// </summary>
public sealed class VersionCheckService : IVersionCheckService
{
    private readonly IReadOnlyList<IVersionProvider> _providers;
    private readonly ILogger _logger;

    public VersionCheckService(IEnumerable<IVersionProvider> providers, ILogger<VersionCheckService>? logger = null)
    {
        _providers = [.. providers.OrderBy(p => IndexOf(p.Source.Id)).ThenBy(p => p.Source.Id, StringComparer.Ordinal)];
        _logger = logger ?? NullLogger<VersionCheckService>.Instance;
    }

    public IReadOnlyList<VersionSourceInfo> Sources => [.. _providers.Select(p => p.Source)];

    public bool IsApplicable(string sourceId, SystemInfo system) =>
        _providers.FirstOrDefault(p => p.Source.Id == sourceId)?.IsApplicable(system) ?? false;

    public async Task<VersionCheckRun> CheckAllAsync(
        IReadOnlyList<DeviceAssessment> devices,
        SystemInfo system,
        IReadOnlySet<string> enabledSources,
        IProgress<VersionCheckProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(devices);
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(enabledSources);

        var active = _providers.Where(p => enabledSources.Contains(p.Source.Id)).ToList();
        var statuses = new List<VersionSourceStatus>();
        var perDevice = new Dictionary<string, List<VersionCheckResult>>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < active.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var provider = active[i];
            var id = provider.Source.Id;
            progress?.Report(new VersionCheckProgress(id, i, active.Count));

            if (!provider.IsApplicable(system))
            {
                statuses.Add(new VersionSourceStatus(id, VersionSourceOutcome.NotApplicable, VersionSourceReasons.WrongManufacturer));
                continue;
            }

            try
            {
                var started = DateTimeOffset.UtcNow;
                var result = await provider.CheckAsync(devices, system, cancellationToken).ConfigureAwait(false);

                foreach (var (instanceId, check) in result.Results)
                {
                    if (!perDevice.TryGetValue(instanceId, out var list))
                    {
                        perDevice[instanceId] = list = [];
                    }

                    list.Add(check);
                }

                statuses.Add(new VersionSourceStatus(id, result.Outcome, result.Detail, result.Results.Count));
                _logger.LogInformation(
                    "Version source {Source}: {Outcome} {Detail}, {Matches} matches, {Newer} newer, {Ms} ms",
                    id, result.Outcome, result.Detail, result.Results.Count,
                    result.Results.Values.Count(r => r.State == VersionCheckState.NewerAvailable),
                    (int)(DateTimeOffset.UtcNow - started).TotalMilliseconds);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (VersionSourceException ex)
            {
                _logger.LogWarning(ex, "Version source {Source} failed: {Reason}", id, ex.Reason);
                statuses.Add(new VersionSourceStatus(id, VersionSourceOutcome.Failed, ex.Reason));
            }
            catch (Exception ex)
            {
                // A broken provider must not take the app down – report and continue with the next source.
                _logger.LogError(ex, "Version source {Source} failed unexpectedly", id);
                statuses.Add(new VersionSourceStatus(id, VersionSourceOutcome.Failed, VersionSourceReasons.Unexpected));
            }
        }

        var anySourceFailed = statuses.Any(s => s.Outcome == VersionSourceOutcome.Failed);
        var updated = devices
            .Select(d => d with { VersionCheck = Merge(perDevice.GetValueOrDefault(d.Device.InstanceId), anySourceFailed) })
            .ToList();

        return new VersionCheckRun(updated, statuses, DateTimeOffset.Now);
    }

    /// <summary>Picks the most useful answer: newer (highest version / date) → up to date → unknown/failed.</summary>
    public static VersionCheckResult Merge(IReadOnlyList<VersionCheckResult>? results, bool anySourceFailed = false)
    {
        if (results is null || results.Count == 0)
        {
            return anySourceFailed
                ? new VersionCheckResult(string.Empty, VersionCheckState.Failed)
                : VersionCheckResult.None;
        }

        var newer = results.Where(r => r.State == VersionCheckState.NewerAvailable).ToList();
        if (newer.Count > 0)
        {
            return newer
                .OrderByDescending(r => DriverVersion.TryParse(r.LatestVersion, out var v) ? v : default)
                .ThenByDescending(r => r.LatestDate)
                .ThenBy(r => IndexOf(r.ProviderId))
                .First();
        }

        return results.FirstOrDefault(r => r.State == VersionCheckState.UpToDate)
            ?? results.OrderBy(r => r.State == VersionCheckState.Failed ? 1 : 0).First();
    }

    private static int IndexOf(string id)
    {
        var index = VersionSourceIds.All.ToList().IndexOf(id);
        return index < 0 ? int.MaxValue : index;
    }
}
