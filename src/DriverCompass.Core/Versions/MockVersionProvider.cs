using DriverCompass.Core.Abstractions;
using DriverCompass.Core.Models;

namespace DriverCompass.Core.Versions;

/// <summary>
/// Offline stand-in for a real source, used by the demo mode (<c>--mock</c>) and tests.
/// Knows a few of the <see cref="Scanning.MockDeviceScanner"/> devices – no network access.
/// </summary>
public sealed class MockVersionProvider(string sourceId, IReadOnlyList<CatalogDriverEntry>? entries = null) : IVersionProvider
{
    public VersionSourceInfo Source { get; } = new(sourceId, TransmitsDeviceData: false);

    public bool IsApplicable(SystemInfo system) => true;

    public async Task<VersionProviderResult> CheckAsync(
        IReadOnlyList<DeviceAssessment> devices,
        SystemInfo system,
        CancellationToken cancellationToken = default)
    {
        // Feels like a real check in the demo.
        await Task.Delay(400, cancellationToken).ConfigureAwait(false);
        var results = VersionMatching.Match(devices, entries ?? DefaultEntries(sourceId), sourceId);

        // Windows Update reports the number of offered drivers as detail; catalogs report a catalog version.
        var detail = sourceId == VersionSourceIds.WindowsUpdate
            ? results.Values.Count(r => r.State == VersionCheckState.NewerAvailable).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "demo";
        return new VersionProviderResult(results, VersionSourceOutcome.Ok, detail);
    }

    public const string MockUpdateId = "6f1c2b7e-0d1a-4c5e-9b0a-3e2d1c4b5a69";
    public const string MockDellPackage = "FOLDER00000001M/1/Intel-AX210-WLAN-Driver_DEMO1_WIN64_23.130.0.4_A01.EXE";

    private static IReadOnlyList<CatalogDriverEntry> DefaultEntries(string sourceId) => sourceId switch
    {
        VersionSourceIds.WindowsUpdate =>
        [
            // Intel Bluetooth (installed 22.240.0.2) – Windows Update reports only a date.
            // The UpdateID makes it installable in the demo (stage 3, simulated – nothing is installed with --mock).
            new([@"USB\VID_8087&PID_0029&REV_0001"], null, new DateOnly(2025, 9, 18), "Intel Corporation - Bluetooth - 23.150.0.3",
                PackageId: MockUpdateId),
        ],
        _ =>
        [
            // Intel Wi-Fi AX210 (installed 23.60.1.2) → newer.
            new([@"PCI\VEN_8086&DEV_2725&SUBSYS_00248086"], "23.130.0.4", new DateOnly(2025, 3, 12), "Intel AX210/AX211 WLAN Driver",
                new Uri("https://www.dell.com/support/home/drivers/driversdetails?driverid=demo1"),
                PackageId: sourceId == VersionSourceIds.Dell ? MockDellPackage : null),
            // Samsung NVMe (Microsoft inbox driver) – catalog has the same version → up to date.
            new([@"PCI\VEN_144D&DEV_A80A&SUBSYS_A801144D"], "10.0.26100.1", new DateOnly(2006, 6, 21), "Standard NVMe Controller"),
        ],
    };
}
