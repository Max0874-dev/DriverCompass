using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using DriverCompass.Core.Abstractions;
using DriverCompass.Core.Models;
using DriverCompass.Core.Versions;
using DriverCompass.VersionProviders.Net;

namespace DriverCompass.VersionProviders.Dell;

/// <summary>
/// Dell Command | Update catalogs. The index carries no device data; downloading the model catalog
/// tells Dell which model (systemID) is asked for. Both CABs are Authenticode-signed by Dell, the model
/// CAB is additionally checked against the SHA-256 from the index.
/// </summary>
public sealed partial class DellVersionProvider(ICatalogDownloader downloader, ICabExtractor extractor) : IVersionProvider
{
    internal static readonly Uri IndexUri = new("https://downloads.dell.com/catalog/CatalogIndexPC.cab");
    internal static readonly Uri BaseUri = new("https://downloads.dell.com/");
    internal static readonly string[] Hosts = ["downloads.dell.com"];
    internal static readonly string[] Signers = ["Dell Technologies Inc.", "Dell Inc."];
    private const long MaxCabBytes = 25L * 1024 * 1024;

    public VersionSourceInfo Source { get; } = new(VersionSourceIds.Dell, TransmitsDeviceData: true);

    public bool IsApplicable(SystemInfo system) =>
        system.Manufacturer?.TrimStart().StartsWith("Dell", StringComparison.OrdinalIgnoreCase) == true;

    public async Task<VersionProviderResult> CheckAsync(
        IReadOnlyList<DeviceAssessment> devices,
        SystemInfo system,
        CancellationToken cancellationToken = default)
    {
        var systemId = system.SystemSku?.Trim().ToUpperInvariant();
        if (systemId is null || !SystemIdRegex().IsMatch(systemId))
        {
            return VersionProviderResult.NotApplicable(VersionSourceReasons.ModelNotInCatalog);
        }

        var indexCab = await downloader.DownloadAsync(new DownloadRequest(IndexUri, Hosts, MaxCabBytes), cancellationToken).ConfigureAwait(false);
        IReadOnlyList<DellModelCatalogRef> index;
        using (var xml = await extractor.ExtractAsync(indexCab, Signers, cancellationToken).ConfigureAwait(false))
        await using (var stream = xml.OpenRead())
        {
            index = DellCatalogParser.ParseIndex(stream);
        }

        var model = index.FirstOrDefault(r => r.SystemIds.Contains(systemId));
        if (model is null)
        {
            return VersionProviderResult.NotApplicable(VersionSourceReasons.ModelNotInCatalog);
        }

        var modelCab = await downloader.DownloadAsync(
            new DownloadRequest(new Uri(BaseUri, model.Path), Hosts, MaxCabBytes, model.Sha256),
            cancellationToken).ConfigureAwait(false);

        IReadOnlyList<CatalogDriverEntry> entries;
        using (var xml = await extractor.ExtractAsync(modelCab, Signers, cancellationToken).ConfigureAwait(false))
        await using (var stream = xml.OpenRead())
        {
            entries = DellCatalogParser.ParseModelCatalog(stream, RuntimeInformation.OSArchitecture);
        }

        var results = VersionMatching.Match(devices, entries, Source.Id);
        return new VersionProviderResult(results, VersionSourceOutcome.Ok, model.Version);
    }

    [GeneratedRegex("^[0-9A-F]{4}$", RegexOptions.CultureInvariant)]
    private static partial Regex SystemIdRegex();
}
