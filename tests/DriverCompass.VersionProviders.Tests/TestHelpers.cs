using System.Net;
using System.Security.Cryptography;
using DriverCompass.Core.Models;
using DriverCompass.VersionProviders.Net;

namespace DriverCompass.VersionProviders.Tests;

internal static class Fixtures
{
    public static string PathOf(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    public static string Sha256Of(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    public static DeviceAssessment Device(string instanceId, string[] hardwareIds, string? version, DateOnly? date = null, string[]? compatibleIds = null) => new()
    {
        Device = new DeviceInfo
        {
            InstanceId = instanceId,
            HardwareIds = hardwareIds,
            CompatibleIds = compatibleIds ?? [],
            Driver = version is null && date is null ? null : new DriverInfo { ProviderName = "Vendor", Version = version, Date = date },
        },
        Status = DeviceStatus.Ok,
        Reason = StatusReason.VendorDriver,
    };
}

/// <summary>Serves fixture files for given URLs and records every request.</summary>
internal sealed class FakeDownloader(Dictionary<string, string> files) : ICatalogDownloader
{
    public List<DownloadRequest> Requests { get; } = [];

    public Task<string> DownloadAsync(DownloadRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        if (!CatalogDownloader.IsAllowed(request.Uri, request.AllowedHosts))
        {
            throw new InvalidOperationException("Provider asked for a URL outside its allow-list: " + request.Uri);
        }

        return files.TryGetValue(request.Uri.AbsoluteUri, out var path)
            ? Task.FromResult(path)
            : Task.FromException<string>(new VersionSourceException(VersionSourceReasons.ModelNotInCatalog, "404 " + request.Uri));
    }
}

/// <summary>Treats the "CAB" as the XML itself and records which signers were required.</summary>
internal sealed class FakeExtractor : ICabExtractor
{
    public List<IReadOnlyCollection<string>> RequiredSigners { get; } = [];

    public Task<ExtractedFile> ExtractAsync(string cabPath, IReadOnlyCollection<string> allowedSigners, CancellationToken cancellationToken = default)
    {
        RequiredSigners.Add(allowedSigners);
        return Task.FromResult(new ExtractedFile(cabPath, null));
    }
}

internal sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public int Calls { get; private set; }
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        Requests.Add(request);
        return Task.FromResult(respond(request));
    }

    public static HttpResponseMessage Ok(byte[] body, string? etag = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
        if (etag is not null)
        {
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(etag);
        }

        return response;
    }
}

internal sealed class FakeTime : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
