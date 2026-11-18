using DriverCompass.Core.Models;

namespace DriverCompass.Core.Abstractions;

// =====================================================================
// Stage 2 – version comparison. Interfaces only, NOT implemented yet.
// Every source (OEM catalog, Windows Update …) becomes one plugin.
// =====================================================================

/// <summary>A source that can tell whether a newer driver exists. Only official, machine-readable sources.</summary>
public interface IVersionProvider
{
    string Id { get; }

    /// <summary>True if this provider sends device data off the PC (must then be opt-in).</summary>
    bool TransmitsDeviceData { get; }

    bool CanHandle(DeviceAssessment assessment, SystemInfo system);

    Task<VersionCheckResult> CheckAsync(DeviceAssessment assessment, SystemInfo system, CancellationToken cancellationToken = default);
}

/// <summary>Runs all enabled <see cref="IVersionProvider"/>s for a set of devices.</summary>
public interface IVersionCheckService
{
    Task<IReadOnlyList<DeviceAssessment>> CheckAllAsync(
        IReadOnlyList<DeviceAssessment> devices,
        SystemInfo system,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);
}
