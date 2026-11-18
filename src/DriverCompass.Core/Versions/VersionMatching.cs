using DriverCompass.Core.Models;

namespace DriverCompass.Core.Versions;

/// <summary>One driver entry from a catalog: the hardware IDs it is for and its version/date.</summary>
public sealed record CatalogDriverEntry(
    IReadOnlyList<string> HardwareIds,
    string? Version,
    DateOnly? Date,
    string? PackageName = null,
    Uri? SourceUri = null,
    string? PackageId = null);

/// <summary>Shared helpers for all version providers: hardware-ID matching and version comparison.</summary>
public static class VersionMatching
{
    /// <summary>
    /// IDs a catalog entry may match: all hardware IDs plus vendor-specific compatible IDs
    /// (e.g. <c>PCI\VEN_8086&amp;DEV_A0E8</c>, which Windows lists as compatible ID).
    /// Class-only compatible IDs such as <c>PCI\CC_0200</c> or <c>USB\Class_FF</c> are excluded –
    /// they would match drivers for completely different devices.
    /// </summary>
    public static IEnumerable<string> MatchableIds(DeviceInfo device)
    {
        foreach (var id in device.HardwareIds)
        {
            yield return id;
        }

        foreach (var id in device.CompatibleIds)
        {
            if (id.Contains("VEN_", StringComparison.OrdinalIgnoreCase) ||
                id.Contains("VID_", StringComparison.OrdinalIgnoreCase))
            {
                yield return id;
            }
        }
    }

    /// <summary>
    /// Builds a lookup "hardware ID → catalog entries". For every device the best (newest) matching entry
    /// is chosen and compared with the installed driver.
    /// </summary>
    public static Dictionary<string, VersionCheckResult> Match(
        IReadOnlyList<DeviceAssessment> devices,
        IEnumerable<CatalogDriverEntry> entries,
        string providerId)
    {
        var index = new Dictionary<string, List<CatalogDriverEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            foreach (var id in entry.HardwareIds)
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                if (!index.TryGetValue(id.Trim(), out var list))
                {
                    index[id.Trim()] = list = [];
                }

                list.Add(entry);
            }
        }

        var results = new Dictionary<string, VersionCheckResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var assessment in devices)
        {
            var candidates = MatchableIds(assessment.Device)
                .Where(index.ContainsKey)
                .SelectMany(id => index[id])
                .Distinct()
                .ToList();

            if (candidates.Count == 0)
            {
                continue;
            }

            var best = candidates.OrderByDescending(e => e, EntryOrder.Instance).First();
            results[assessment.Device.InstanceId] = Evaluate(assessment.Device.Driver, best, providerId);
        }

        return results;
    }

    /// <summary>Compares the installed driver with a catalog entry. Version first, date only if no version is comparable.</summary>
    public static VersionCheckResult Evaluate(DriverInfo? installed, CatalogDriverEntry entry, string providerId)
    {
        var state = Compare(installed, entry.Version, entry.Date);
        return new VersionCheckResult(providerId, state, Clean(entry.Version), entry.Date, entry.SourceUri, entry.PackageName) { PackageId = entry.PackageId };
    }

    public static VersionCheckState Compare(DriverInfo? installed, string? latestVersion, DateOnly? latestDate)
    {
        if (installed is null || !installed.HasData)
        {
            return VersionCheckState.Unknown;
        }

        if (DriverVersion.TryParse(latestVersion, out var latest) && DriverVersion.TryParse(installed.Version, out var current))
        {
            return latest > current ? VersionCheckState.NewerAvailable : VersionCheckState.UpToDate;
        }

        if (latestDate is { } ld && installed.Date is { } cd)
        {
            return ld > cd ? VersionCheckState.NewerAvailable : VersionCheckState.UpToDate;
        }

        return VersionCheckState.Unknown;
    }

    /// <summary>Removes catalog markers like the trailing <c>^</c>.</summary>
    public static string? Clean(string? version) =>
        string.IsNullOrWhiteSpace(version) ? null : version.Trim().TrimEnd('^').Trim();

    private sealed class EntryOrder : IComparer<CatalogDriverEntry>
    {
        public static EntryOrder Instance { get; } = new();

        public int Compare(CatalogDriverEntry? x, CatalogDriverEntry? y)
        {
            var hx = DriverVersion.TryParse(x?.Version, out var vx);
            var hy = DriverVersion.TryParse(y?.Version, out var vy);
            if (hx && hy && vx != vy)
            {
                return vx.CompareTo(vy);
            }

            if (hx != hy)
            {
                return hx ? 1 : -1;
            }

            return Nullable.Compare(x?.Date, y?.Date);
        }
    }
}
