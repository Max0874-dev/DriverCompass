using System.Net;
using DriverCompass.Core;
using DriverCompass.Core.Abstractions;
using DriverCompass.VersionProviders.Dell;
using DriverCompass.VersionProviders.Hp;
using DriverCompass.VersionProviders.Lenovo;
using DriverCompass.VersionProviders.Net;
using DriverCompass.VersionProviders.WindowsUpdate;
using Microsoft.Extensions.Logging;

namespace DriverCompass.VersionProviders;

/// <summary>Creates the real version sources with shared download/extract infrastructure.</summary>
public static class VersionProviderFactory
{
    public static string DefaultCacheDirectory => Path.Combine(AppInfo.DataDirectory, "cache", "versions");

    /// <summary>HttpClient for catalog downloads: no automatic redirects (validated manually), no cookies.</summary>
    public static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(15),
        };

        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppInfo.TechnicalName}/{AppInfo.Version}");
        return client;
    }

    public static IReadOnlyList<IVersionProvider> CreateAll(HttpClient http, ILoggerFactory loggerFactory, string? cacheDirectory = null)
    {
        var downloader = new CatalogDownloader(http, cacheDirectory ?? DefaultCacheDirectory, logger: loggerFactory.CreateLogger<CatalogDownloader>());
        var extractor = new ExpandCabExtractor(logger: loggerFactory.CreateLogger<ExpandCabExtractor>());

        return
        [
            new WindowsUpdateVersionProvider(new ComWindowsUpdateSearcher(), loggerFactory.CreateLogger<WindowsUpdateVersionProvider>()),
            new DellVersionProvider(downloader, extractor),
            new LenovoVersionProvider(downloader),
            new HpVersionProvider(downloader, extractor),
        ];
    }
}
