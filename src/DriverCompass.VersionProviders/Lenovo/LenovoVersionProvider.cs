using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using DriverCompass.Core.Abstractions;
using DriverCompass.Core.Models;
using DriverCompass.Core.Versions;
using DriverCompass.VersionProviders.Net;
using DriverCompass.VersionProviders.Packages;

namespace DriverCompass.VersionProviders.Lenovo;

/// <summary>
/// Lenovo System Update catalogs. Downloading the catalog tells Lenovo the machine type (first four
/// characters of the model, e.g. "20XW"). Every descriptor is checked against the SHA-256 from the catalog.
/// </summary>
public sealed partial class LenovoVersionProvider(ICatalogDownloader downloader) : IVersionProvider, IOemPackageCatalog
{
    internal static readonly string[] Hosts = ["download.lenovo.com"];
    internal static readonly string[] Signers = ["Lenovo"];
    private const long MaxCatalogBytes = 5L * 1024 * 1024;
    private const long MaxDescriptorBytes = 2L * 1024 * 1024;
    private const int Parallelism = 4;

    public VersionSourceInfo Source { get; } = new(VersionSourceIds.Lenovo, TransmitsDeviceData: true);

    public bool IsApplicable(SystemInfo system) =>
        system.Manufacturer?.Trim().StartsWith("LENOVO", StringComparison.OrdinalIgnoreCase) == true;

    internal static string? MachineType(SystemInfo system) =>
        system.Model?.Trim() is { Length: >= 4 } model && MachineTypeRegex().IsMatch(model[..4])
            ? model[..4].ToUpperInvariant()
            : null;

    public string SourceId => VersionSourceIds.Lenovo;

    public async Task<VersionProviderResult> CheckAsync(
        IReadOnlyList<DeviceAssessment> devices,
        SystemInfo system,
        CancellationToken cancellationToken = default)
    {
        if (await LoadCatalogAsync(system, cancellationToken).ConfigureAwait(false) is not { } catalog)
        {
            return VersionProviderResult.NotApplicable(VersionSourceReasons.ModelNotInCatalog);
        }

        var entries = new ConcurrentBag<CatalogDriverEntry>();
        await Parallel.ForEachAsync(
            catalog.Packages,
            new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = cancellationToken },
            async (package, ct) =>
            {
                var path = await downloader.DownloadAsync(
                    new DownloadRequest(package.Location, Hosts, MaxDescriptorBytes, package.Sha256), ct).ConfigureAwait(false);
                await using var stream = File.OpenRead(path);
                foreach (var entry in LenovoCatalogParser.ParseDescriptor(stream))
                {
                    entries.Add(entry);
                }
            }).ConfigureAwait(false);

        var results = VersionMatching.Match(devices, entries, Source.Id);
        return new VersionProviderResult(results, VersionSourceOutcome.Ok, catalog.Label);
    }

    /// <summary>Stage 3: finds the descriptor with this id in the catalog for this PC and turns it into a package.</summary>
    public async Task<OemPackage?> FindPackageAsync(SystemInfo system, string packageId, CancellationToken cancellationToken = default)
    {
        if (!IsApplicable(system) || await LoadCatalogAsync(system, cancellationToken).ConfigureAwait(false) is not { } catalog)
        {
            return null;
        }

        foreach (var reference in catalog.Packages)
        {
            var path = await downloader.DownloadAsync(
                new DownloadRequest(reference.Location, Hosts, MaxDescriptorBytes, reference.Sha256), cancellationToken).ConfigureAwait(false);
            LenovoCatalogParser.LenovoDescriptor descriptor;
            await using (var stream = File.OpenRead(path))
            {
                descriptor = LenovoCatalogParser.ReadDescriptor(stream);
            }

            if (string.Equals(descriptor.Id, packageId, StringComparison.Ordinal))
            {
                return LenovoCatalogParser.ToPackage(descriptor, reference.Location);
            }
        }

        return null;
    }

    /// <summary>Catalog for this machine type (Win11 or Win10 list), only descriptors on download.lenovo.com.</summary>
    private async Task<(IReadOnlyList<LenovoPackageRef> Packages, string Label)?> LoadCatalogAsync(SystemInfo system, CancellationToken cancellationToken)
    {
        var mt = MachineType(system);
        if (mt is null)
        {
            return null;
        }

        var preferred = system.IsWindows11 ? "Win11" : "Win10";
        var fallback = system.IsWindows11 ? "Win10" : "Win11";
        var catalogPath = await TryDownloadCatalogAsync(mt, preferred, cancellationToken).ConfigureAwait(false)
            ?? await TryDownloadCatalogAsync(mt, fallback, cancellationToken).ConfigureAwait(false);
        if (catalogPath is null)
        {
            return null;
        }

        await using var stream = File.OpenRead(catalogPath);
        var packages = LenovoCatalogParser.ParseCatalog(stream).Where(p => CatalogDownloader.IsAllowed(p.Location, Hosts)).ToList();
        return (packages, $"{mt} {preferred}");
    }

    private async Task<string?> TryDownloadCatalogAsync(string mt, string os, CancellationToken ct)
    {
        try
        {
            var uri = new Uri($"https://download.lenovo.com/catalog/{mt}_{os}.xml");
            return await downloader.DownloadAsync(new DownloadRequest(uri, Hosts, MaxCatalogBytes), ct).ConfigureAwait(false);
        }
        catch (VersionSourceException ex) when (ex.Reason == VersionSourceReasons.ModelNotInCatalog)
        {
            return null;
        }
    }

    [GeneratedRegex("^[0-9A-Za-z]{4}$", RegexOptions.CultureInvariant)]
    private static partial Regex MachineTypeRegex();
}
