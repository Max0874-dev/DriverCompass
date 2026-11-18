using System.Text.RegularExpressions;
using DriverCompass.Core.Abstractions;
using DriverCompass.Core.Models;
using DriverCompass.Core.Versions;
using DriverCompass.VersionProviders.Net;

namespace DriverCompass.VersionProviders.Hp;

/// <summary>
/// HP Image Assistant reference files. The platform list carries no device data; downloading the
/// reference file tells HP the platform ID (from the mainboard product, e.g. "8B41") and Windows release.
/// Both CABs are Authenticode-signed by HP Inc.
/// </summary>
public sealed partial class HpVersionProvider(ICatalogDownloader downloader, ICabExtractor extractor) : IVersionProvider
{
    internal static readonly Uri PlatformListUri = new("https://hpia.hpcloud.hp.com/ref/platformList.cab");
    internal static readonly string[] Hosts = ["hpia.hpcloud.hp.com"];
    internal static readonly string[] Signers = ["HP Inc."];
    private const long MaxCabBytes = 30L * 1024 * 1024;

    public VersionSourceInfo Source { get; } = new(VersionSourceIds.Hp, TransmitsDeviceData: true);

    public bool IsApplicable(SystemInfo system) =>
        system.Manufacturer?.Trim() is { } m &&
        (m.StartsWith("HP", StringComparison.OrdinalIgnoreCase) || m.StartsWith("Hewlett", StringComparison.OrdinalIgnoreCase));

    internal static string? PlatformId(SystemInfo system) =>
        system.BaseBoardProduct?.Trim() is { } p && PlatformRegex().IsMatch(p) ? p.ToUpperInvariant() : null;

    public async Task<VersionProviderResult> CheckAsync(
        IReadOnlyList<DeviceAssessment> devices,
        SystemInfo system,
        CancellationToken cancellationToken = default)
    {
        var platformId = PlatformId(system);
        if (platformId is null)
        {
            return VersionProviderResult.NotApplicable(VersionSourceReasons.ModelNotInCatalog);
        }

        var listCab = await downloader.DownloadAsync(new DownloadRequest(PlatformListUri, Hosts, MaxCabBytes), cancellationToken).ConfigureAwait(false);
        IReadOnlyList<HpPlatformOs> releases;
        using (var xml = await extractor.ExtractAsync(listCab, Signers, cancellationToken).ConfigureAwait(false))
        await using (var stream = xml.OpenRead())
        {
            releases = HpCatalogParser.ParsePlatformList(stream, platformId);
        }

        var os = HpCatalogParser.ChooseOs(releases, system.IsWindows11, system.OperatingSystemDisplayVersion, system.OperatingSystemBuild);
        if (os is null)
        {
            return VersionProviderResult.NotApplicable(VersionSourceReasons.ModelNotInCatalog);
        }

        string referenceCab;
        try
        {
            referenceCab = await downloader.DownloadAsync(
                new DownloadRequest(HpCatalogParser.ReferenceUri(platformId, os), Hosts, MaxCabBytes), cancellationToken).ConfigureAwait(false);
        }
        catch (VersionSourceException ex) when (ex.Reason == VersionSourceReasons.ModelNotInCatalog)
        {
            return VersionProviderResult.NotApplicable(VersionSourceReasons.ModelNotInCatalog);
        }

        IReadOnlyList<CatalogDriverEntry> entries;
        using (var xml = await extractor.ExtractAsync(referenceCab, Signers, cancellationToken).ConfigureAwait(false))
        await using (var stream = xml.OpenRead())
        {
            entries = HpCatalogParser.ParseReference(stream);
        }

        var results = VersionMatching.Match(devices, entries, Source.Id);
        return new VersionProviderResult(results, VersionSourceOutcome.Ok, $"{platformId} {os.ReleaseIdFilename}");
    }

    [GeneratedRegex("^[0-9A-Fa-f]{4}$", RegexOptions.CultureInvariant)]
    private static partial Regex PlatformRegex();
}
