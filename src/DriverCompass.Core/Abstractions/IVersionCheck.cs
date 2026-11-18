using DriverCompass.Core.Models;

namespace DriverCompass.Core.Abstractions;

// =====================================================================
// Stage 2 – version comparison.
// Every source (Windows Update, Dell, Lenovo, HP catalog …) is one plugin.
// Only official, machine-readable sources – no HTML scraping.
// =====================================================================

/// <summary>A source that can tell whether a newer driver exists.</summary>
public interface IVersionProvider
{
    VersionSourceInfo Source { get; }

    /// <summary>Cheap, offline check whether the source can cover this PC at all (e.g. Dell catalog only on Dell PCs).</summary>
    bool IsApplicable(SystemInfo system);

    /// <summary>
    /// Checks all devices in one go (one catalog download / one Windows Update search per run).
    /// Expected failures are reported as <see cref="VersionSourceException"/>.
    /// </summary>
    Task<VersionProviderResult> CheckAsync(
        IReadOnlyList<DeviceAssessment> devices,
        SystemInfo system,
        CancellationToken cancellationToken = default);
}

/// <summary>Runs the enabled <see cref="IVersionProvider"/>s and merges their results per device.</summary>
public interface IVersionCheckService
{
    /// <summary>All known sources (enabled or not), in display order.</summary>
    IReadOnlyList<VersionSourceInfo> Sources { get; }

    /// <summary>Offline check whether a source covers this PC (for the settings page). <c>false</c> for unknown ids.</summary>
    bool IsApplicable(string sourceId, SystemInfo system);

    Task<VersionCheckRun> CheckAllAsync(
        IReadOnlyList<DeviceAssessment> devices,
        SystemInfo system,
        IReadOnlySet<string> enabledSources,
        IProgress<VersionCheckProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
