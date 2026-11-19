using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using DriverCompass.Core.Models;

namespace DriverCompass.Data.Tests;

public sealed class VendorCatalogProviderTests : IDisposable
{
    private static readonly Uri Remote = new("https://raw.githubusercontent.com/owner/DriverCompass/main/data/vendors.json");
    private readonly string _cacheDir = Path.Combine(Path.GetTempPath(), "dc-cache-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_cacheDir))
        {
            Directory.Delete(_cacheDir, recursive: true);
        }
    }

    private static string CatalogJson(string dataVersion, string vendorName = "Acme") => new JsonObject
    {
        ["schemaVersion"] = 1,
        ["dataVersion"] = dataVersion,
        ["chipVendors"] = new JsonArray(new JsonObject
        {
            ["id"] = "acme",
            ["name"] = vendorName,
            ["ids"] = new JsonObject { ["pci"] = new JsonArray("ABCD") },
            ["allowedDomains"] = new JsonArray("acme.example"),
            ["driverPages"] = new JsonArray(new JsonObject { ["url"] = "https://www.acme.example/drivers" }),
        }),
        ["oems"] = new JsonArray(),
        ["genericDrivers"] = new JsonArray(),
    }.ToJsonString();

    private VendorCatalogProvider Create(FakeHandler handler, string embedded, bool allowRemote = true, Uri? remote = null) => new(
        new HttpClient(handler),
        new VendorCatalogValidator(),
        new CatalogProviderOptions { RemoteUri = remote ?? Remote, AllowRemote = allowRemote, CacheDirectory = _cacheDir },
        () => embedded);

    [Fact]
    public async Task Valid_newer_remote_is_used_and_cached()
    {
        var handler = FakeHandler.Json(CatalogJson("2026.2.1.1", "Remote Acme"), etag: "\"v2\"");
        var result = await Create(handler, CatalogJson("2026.1.1.1")).LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CatalogSource.Remote, result.Info.Source);
        Assert.Equal("2026.2.1.1", result.Info.DataVersion);
        Assert.Empty(result.Info.Warnings);
        Assert.True(File.Exists(Path.Combine(_cacheDir, "vendors.json")));
        Assert.Equal("\"v2\"", File.ReadAllText(Path.Combine(_cacheDir, "vendors.etag")));
    }

    [Fact]
    public async Task Offline_falls_back_to_cache_then_embedded()
    {
        var embedded = CatalogJson("2026.1.1.1");

        // 1) Nothing cached, network down → embedded.
        var first = await Create(FakeHandler.Throws(), embedded).LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CatalogSource.Embedded, first.Info.Source);
        Assert.Equal(new[] { CatalogWarnings.RemoteUnavailable }, first.Info.Warnings);

        // 2) Fill the cache with a newer version.
        await Create(FakeHandler.Json(CatalogJson("2026.3.1.1")), embedded).LoadAsync(TestContext.Current.CancellationToken);

        // 3) Network down again → cache.
        var third = await Create(FakeHandler.Throws(), embedded).LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CatalogSource.Cache, third.Info.Source);
        Assert.Equal("2026.3.1.1", third.Info.DataVersion);
        Assert.Equal(new[] { CatalogWarnings.RemoteUnavailableCached }, third.Info.Warnings);
    }

    [Fact]
    public async Task Invalid_remote_is_rejected()
    {
        var bad = CatalogJson("2026.5.1.1").Replace("https://www.acme.example", "http://www.acme.example", StringComparison.Ordinal);
        var result = await Create(FakeHandler.Json(bad), CatalogJson("2026.1.1.1")).LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CatalogSource.Embedded, result.Info.Source);
        Assert.Contains(CatalogWarnings.RemoteInvalid, result.Info.Warnings);
        Assert.False(File.Exists(Path.Combine(_cacheDir, "vendors.json")));
    }

    [Fact]
    public async Task Older_remote_is_rejected_as_downgrade()
    {
        var result = await Create(FakeHandler.Json(CatalogJson("2025.1.1.1")), CatalogJson("2026.1.1.1")).LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CatalogSource.Embedded, result.Info.Source);
        Assert.Contains(CatalogWarnings.RemoteOlder, result.Info.Warnings);
    }

    [Fact]
    public async Task Remote_list_with_a_newer_schema_asks_for_an_app_update()
    {
        var newer = CatalogJson("2027.1.1.1").Replace("\"schemaVersion\":1", "\"schemaVersion\":2", StringComparison.Ordinal);
        var result = await Create(FakeHandler.Json(newer), CatalogJson("2026.1.1.1")).LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CatalogSource.Embedded, result.Info.Source);
        Assert.Equal(new[] { CatalogWarnings.RemoteNeedsNewerApp }, result.Info.Warnings);
        Assert.False(File.Exists(Path.Combine(_cacheDir, "vendors.json")));
    }

    [Fact]
    public async Task Cached_list_with_a_newer_schema_is_kept_but_not_used()
    {
        Directory.CreateDirectory(_cacheDir);
        var cachePath = Path.Combine(_cacheDir, "vendors.json");
        var newer = CatalogJson("2027.1.1.1").Replace("\"schemaVersion\":1", "\"schemaVersion\":2", StringComparison.Ordinal);
        await File.WriteAllTextAsync(cachePath, newer, TestContext.Current.CancellationToken);

        var result = await Create(FakeHandler.Throws(), CatalogJson("2026.1.1.1")).LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CatalogSource.Embedded, result.Info.Source);
        Assert.DoesNotContain(CatalogWarnings.CacheInvalid, result.Info.Warnings);
        Assert.True(File.Exists(cachePath));
    }

    [Fact]
    public async Task Corrupted_cache_is_deleted()
    {
        Directory.CreateDirectory(_cacheDir);
        await File.WriteAllTextAsync(Path.Combine(_cacheDir, "vendors.json"), "{ broken", TestContext.Current.CancellationToken);

        var result = await Create(FakeHandler.Throws(), CatalogJson("2026.1.1.1")).LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CatalogSource.Embedded, result.Info.Source);
        Assert.Contains(CatalogWarnings.CacheInvalid, result.Info.Warnings);
        Assert.False(File.Exists(Path.Combine(_cacheDir, "vendors.json")));
    }

    [Fact]
    public async Task Not_modified_keeps_cache_and_sends_etag()
    {
        var embedded = CatalogJson("2026.1.1.1");
        await Create(FakeHandler.Json(CatalogJson("2026.3.1.1"), etag: "\"abc\""), embedded).LoadAsync(TestContext.Current.CancellationToken);

        var handler = FakeHandler.Status(HttpStatusCode.NotModified);
        var result = await Create(handler, embedded).LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CatalogSource.Cache, result.Info.Source);
        Assert.Equal("\"abc\"", handler.LastRequest!.Headers.IfNoneMatch.Single().ToString());
    }

    [Fact]
    public async Task Http_error_falls_back()
    {
        var result = await Create(FakeHandler.Status(HttpStatusCode.NotFound), CatalogJson("2026.1.1.1")).LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CatalogSource.Embedded, result.Info.Source);
        Assert.Contains(CatalogWarnings.RemoteUnavailable, result.Info.Warnings);
    }

    [Fact]
    public async Task Oversized_response_is_rejected()
    {
        var handler = FakeHandler.Json("{\"x\":\"" + new string('a', VendorCatalogValidator.MaxBytes + 10) + "\"}");
        var result = await Create(handler, CatalogJson("2026.1.1.1")).LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CatalogSource.Embedded, result.Info.Source);
        Assert.Contains(CatalogWarnings.RemoteUnavailable, result.Info.Warnings);
    }

    [Fact]
    public async Task Non_https_remote_url_is_never_requested()
    {
        var handler = FakeHandler.Json(CatalogJson("2026.9.1.1"));
        var result = await Create(handler, CatalogJson("2026.1.1.1"), remote: new Uri("http://example.com/vendors.json")).LoadAsync(TestContext.Current.CancellationToken);

        Assert.Null(handler.LastRequest);
        Assert.Equal(CatalogSource.Embedded, result.Info.Source);
    }

    [Fact]
    public async Task Remote_disabled_makes_no_request()
    {
        var handler = FakeHandler.Json(CatalogJson("2026.9.1.1"));
        var result = await Create(handler, CatalogJson("2026.1.1.1"), allowRemote: false).LoadAsync(TestContext.Current.CancellationToken);

        Assert.Null(handler.LastRequest);
        Assert.Equal(CatalogSource.Embedded, result.Info.Source);
        Assert.Empty(result.Info.Warnings);
    }

    [Fact]
    public async Task Invalid_embedded_catalog_throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Create(FakeHandler.Throws(), "{}", allowRemote: false).LoadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Real_embedded_catalog_loads_offline()
    {
        var provider = new VendorCatalogProvider(
            new HttpClient(FakeHandler.Throws()),
            new VendorCatalogValidator(),
            new CatalogProviderOptions { AllowRemote = false, CacheDirectory = _cacheDir });

        var result = await provider.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CatalogSource.Embedded, result.Info.Source);
        Assert.NotEmpty(result.Catalog.ChipVendors);
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        public static FakeHandler Json(string body, string? etag = null) => new(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            if (etag is not null)
            {
                response.Headers.ETag = System.Net.Http.Headers.EntityTagHeaderValue.Parse(etag);
            }

            return response;
        });

        public static FakeHandler Status(HttpStatusCode code) => new(_ => new HttpResponseMessage(code));

        public static FakeHandler Throws() => new(_ => throw new HttpRequestException("offline"));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(respond(request));
        }
    }
}
