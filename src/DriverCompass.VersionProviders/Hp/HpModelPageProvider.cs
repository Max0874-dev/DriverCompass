using DriverCompass.Core.Abstractions;
using DriverCompass.Core.Links;
using DriverCompass.Core.Models;
using DriverCompass.VersionProviders.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DriverCompass.VersionProviders.Hp;

/// <summary>
/// HP's vendor-list link only opens the general driver page. HP's signed platform list (the same public
/// file the HP version check uses, no device data in the request) maps the platform ID from the mainboard
/// (e.g. "880D") to HP's product numbers, and support.hp.com/drivers/model/{number} opens that model's page.
/// Used only when the user opens the HP link.
/// </summary>
public sealed class HpModelPageProvider(ICatalogDownloader downloader, ICabExtractor extractor, ILogger<HpModelPageProvider>? logger = null)
    : IOemModelPageProvider
{
    private const long MaxCabBytes = 30L * 1024 * 1024;
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
    private static readonly string[] SupportDomains = ["hp.com"];
    private readonly ILogger _logger = (ILogger?)logger ?? NullLogger.Instance;

    /// <summary>Id of HP in data/vendors.json.</summary>
    public string OemId => "hp";

    public bool CanResolve(SystemInfo system) => HpVersionProvider.IsHp(system) && HpVersionProvider.PlatformId(system) is not null;

    public async Task<Uri?> FindModelPageAsync(SystemInfo system, CancellationToken cancellationToken = default)
    {
        var platformId = HpVersionProvider.PlatformId(system);
        if (!HpVersionProvider.IsHp(system) || platformId is null)
        {
            return null;
        }

        try
        {
            var cab = await downloader.DownloadAsync(
                new DownloadRequest(HpVersionProvider.PlatformListUri, HpVersionProvider.Hosts, MaxCabBytes, MaxAge: MaxAge), cancellationToken).ConfigureAwait(false);

            IReadOnlyList<HpProduct> products;
            using (var xml = await extractor.ExtractAsync(cab, HpVersionProvider.Signers, cancellationToken).ConfigureAwait(false))
            await using (var stream = xml.OpenRead())
            {
                products = HpCatalogParser.ParseProducts(stream, platformId);
            }

            var product = HpCatalogParser.ChooseProduct(products, system.Model);
            if (product is null)
            {
                _logger.LogInformation("HP platform {Platform} not in HP's platform list", platformId);
                return null;
            }

            var uri = HpCatalogParser.ModelPageUri(product);
            return UrlSafety.IsAllowedHttps(uri, SupportDomains) ? uri : null;
        }
        catch (Exception ex) when (ex is VersionSourceException or HttpRequestException or IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            _logger.LogWarning("HP model page not found ({Error})", ex.GetType().Name);
            return null;
        }
    }
}
