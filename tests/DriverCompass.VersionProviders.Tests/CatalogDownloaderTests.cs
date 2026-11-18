using System.Net;
using System.Security.Cryptography;
using System.Text;
using DriverCompass.Core.Models;
using DriverCompass.VersionProviders.Net;
using DriverCompass.VersionProviders.Xml;


namespace DriverCompass.VersionProviders.Tests;

public sealed class CatalogDownloaderTests : IDisposable
{
    private static readonly string[] Hosts = ["downloads.example.com"];
    private static readonly Uri Good = new("https://downloads.example.com/catalog/a.cab");
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("<x/>");

    private readonly string _cache = Path.Combine(Path.GetTempPath(), "dc-dl-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_cache))
        {
            Directory.Delete(_cache, true);
        }
    }

    private CatalogDownloader Create(StubHandler handler, TimeProvider? time = null) => new(new HttpClient(handler), _cache, time);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("http://downloads.example.com/a.cab")]
    [InlineData("https://evil.example.com/a.cab")]
    [InlineData("https://downloads.example.com.evil.com/a.cab")]
    [InlineData("https://downloads.example.com:8443/a.cab")]
    [InlineData("https://user@downloads.example.com/a.cab")]
    public async Task RejectsUrlsOutsideAllowList_WithoutNetwork(string url)
    {
        var handler = new StubHandler(_ => StubHandler.Ok(Body));
        var ex = await Assert.ThrowsAsync<VersionSourceException>(() =>
            Create(handler).DownloadAsync(new DownloadRequest(new Uri(url), Hosts, 1000), Ct));

        Assert.Equal(VersionSourceReasons.InvalidData, ex.Reason);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Downloads_ThenServesFromCacheWithinMaxAge()
    {
        var handler = new StubHandler(_ => StubHandler.Ok(Body));
        var time = new FakeTime();
        var downloader = Create(handler, time);

        var first = await downloader.DownloadAsync(new DownloadRequest(Good, Hosts, 1000), Ct);
        time.Advance(TimeSpan.FromHours(1));
        var second = await downloader.DownloadAsync(new DownloadRequest(Good, Hosts, 1000), Ct);

        Assert.Equal(first, second);
        Assert.Equal(Body, File.ReadAllBytes(first));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task AfterMaxAge_AsksWithETag_And304UsesCache()
    {
        var handler = new StubHandler(r => r.Headers.IfNoneMatch.Count > 0
            ? new HttpResponseMessage(HttpStatusCode.NotModified)
            : StubHandler.Ok(Body, "\"v1\""));
        var time = new FakeTime();
        var downloader = Create(handler, time);

        await downloader.DownloadAsync(new DownloadRequest(Good, Hosts, 1000), Ct);
        time.Advance(TimeSpan.FromHours(25));
        var path = await downloader.DownloadAsync(new DownloadRequest(Good, Hosts, 1000), Ct);

        Assert.Equal(2, handler.Calls);
        Assert.Equal("\"v1\"", handler.Requests[1].Headers.IfNoneMatch.Single().ToString());
        Assert.Equal(Body, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task RedirectToForeignHost_IsRejected()
    {
        var handler = new StubHandler(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.Found);
            r.Headers.Location = new Uri("https://evil.example.com/a.cab");
            return r;
        });

        var ex = await Assert.ThrowsAsync<VersionSourceException>(() => Create(handler).DownloadAsync(new DownloadRequest(Good, Hosts, 1000), Ct));
        Assert.Equal(VersionSourceReasons.InvalidData, ex.Reason);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task RedirectWithinAllowList_IsFollowed()
    {
        var handler = new StubHandler(r =>
        {
            if (r.RequestUri!.AbsolutePath == "/catalog/a.cab")
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.MovedPermanently);
                redirect.Headers.Location = new Uri("/catalog/b.cab", UriKind.Relative);
                return redirect;
            }

            return StubHandler.Ok(Body);
        });

        var path = await Create(handler).DownloadAsync(new DownloadRequest(Good, Hosts, 1000), Ct);
        Assert.Equal(Body, File.ReadAllBytes(path));
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task WrongSha256_IsRejected_AndNotCached()
    {
        var handler = new StubHandler(_ => StubHandler.Ok(Body));
        var ex = await Assert.ThrowsAsync<VersionSourceException>(() =>
            Create(handler).DownloadAsync(new DownloadRequest(Good, Hosts, 1000, new string('A', 64)), Ct));

        Assert.Equal(VersionSourceReasons.HashMismatch, ex.Reason);
        Assert.Empty(Directory.GetFiles(_cache, "*.bin"));
    }

    [Fact]
    public async Task MatchingSha256_IsReusedWithoutNetwork()
    {
        var handler = new StubHandler(_ => StubHandler.Ok(Body));
        var sha = Convert.ToHexString(SHA256.HashData(Body)).ToLowerInvariant();
        var downloader = Create(handler);

        await downloader.DownloadAsync(new DownloadRequest(Good, Hosts, 1000, sha), Ct);
        await downloader.DownloadAsync(new DownloadRequest(Good, Hosts, 1000, sha), Ct);

        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task TooLarge_IsRejected()
    {
        var handler = new StubHandler(_ => StubHandler.Ok(new byte[5000]));
        var ex = await Assert.ThrowsAsync<VersionSourceException>(() => Create(handler).DownloadAsync(new DownloadRequest(Good, Hosts, 1000), Ct));
        Assert.Equal(VersionSourceReasons.InvalidData, ex.Reason);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, VersionSourceReasons.ModelNotInCatalog)]
    [InlineData(HttpStatusCode.InternalServerError, VersionSourceReasons.Offline)]
    public async Task HttpErrors_AreMapped(HttpStatusCode status, string reason)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(status));
        var ex = await Assert.ThrowsAsync<VersionSourceException>(() => Create(handler).DownloadAsync(new DownloadRequest(Good, Hosts, 1000), Ct));
        Assert.Equal(reason, ex.Reason);
    }

    [Fact]
    public async Task NetworkError_IsOffline()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("no network"));
        var ex = await Assert.ThrowsAsync<VersionSourceException>(() => Create(handler).DownloadAsync(new DownloadRequest(Good, Hosts, 1000), Ct));
        Assert.Equal(VersionSourceReasons.Offline, ex.Reason);
    }
}

public class SafeXmlTests
{
    [Fact]
    public void Dtd_IsRejected()
    {
        const string xml = """<?xml version="1.0"?><!DOCTYPE x [<!ENTITY a "aaaaaaaaaa">]><packages><package>&a;</package></packages>""";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        var ex = Assert.Throws<VersionSourceException>(() => SafeXml.ReadChildren(stream, _ => true).ToList());
        Assert.Equal(VersionSourceReasons.InvalidData, ex.Reason);
    }

    [Fact]
    public void BrokenXml_IsInvalidData()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("<packages><package></packages>"));
        Assert.Throws<VersionSourceException>(() => SafeXml.ReadChildren(stream, _ => true).ToList());
    }
}

public class CabExtractorTests
{
    [Fact]
    public async Task UnsignedFile_IsRejected_BeforeExtraction()
    {
        var path = Path.Combine(Path.GetTempPath(), "dc-unsigned-" + Guid.NewGuid().ToString("N") + ".cab");
        await File.WriteAllTextAsync(path, "MSCF not really a cab", TestContext.Current.CancellationToken);
        try
        {
            var ex = await Assert.ThrowsAsync<VersionSourceException>(() =>
                new ExpandCabExtractor().ExtractAsync(path, ["Dell Technologies Inc."], TestContext.Current.CancellationToken));
            Assert.Equal(VersionSourceReasons.SignatureInvalid, ex.Reason);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
