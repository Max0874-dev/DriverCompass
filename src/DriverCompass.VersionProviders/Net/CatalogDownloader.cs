using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using DriverCompass.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DriverCompass.VersionProviders.Net;

/// <param name="Uri">HTTPS address of the file.</param>
/// <param name="AllowedHosts">Exact host names this request (and every redirect) may use. Hard-coded per provider.</param>
/// <param name="MaxBytes">Download is aborted above this size.</param>
/// <param name="ExpectedSha256">Hex SHA-256 from a trusted parent file; content with another hash is rejected.</param>
/// <param name="MaxAge">How long a cached copy is used without asking the server (only without <paramref name="ExpectedSha256"/>).</param>
public sealed record DownloadRequest(
    Uri Uri,
    IReadOnlyCollection<string> AllowedHosts,
    long MaxBytes,
    string? ExpectedSha256 = null,
    TimeSpan? MaxAge = null);

public interface ICatalogDownloader
{
    /// <summary>Returns the path of a local (cached) copy. Throws <see cref="VersionSourceException"/> on failure.</summary>
    Task<string> DownloadAsync(DownloadRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Downloads catalog files for the version check:
/// HTTPS only, exact host allow-list (also for redirects), size limit, optional SHA-256 check,
/// local cache with ETag. Sends no cookies, no device data beyond what the URL itself contains.
/// </summary>
public sealed class CatalogDownloader : ICatalogDownloader
{
    private const int MaxRedirects = 3;
    private static readonly TimeSpan DefaultMaxAge = TimeSpan.FromHours(24);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);

    private readonly HttpClient _http;
    private readonly string _cacheDirectory;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;

    /// <param name="http">Must be created with <c>AllowAutoRedirect = false</c> – redirects are validated here.</param>
    public CatalogDownloader(HttpClient http, string cacheDirectory, TimeProvider? time = null, ILogger<CatalogDownloader>? logger = null)
    {
        _http = http;
        _cacheDirectory = cacheDirectory;
        _time = time ?? TimeProvider.System;
        _logger = logger ?? NullLogger<CatalogDownloader>.Instance;
    }

    public static bool IsAllowed(Uri? uri, IReadOnlyCollection<string> allowedHosts) =>
        uri is { IsAbsoluteUri: true } &&
        uri.Scheme == Uri.UriSchemeHttps &&
        uri.IsDefaultPort &&
        uri.UserInfo.Length == 0 &&
        allowedHosts.Any(h => string.Equals(uri.IdnHost, h, StringComparison.OrdinalIgnoreCase));

    public async Task<string> DownloadAsync(DownloadRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsAllowed(request.Uri, request.AllowedHosts))
        {
            throw new VersionSourceException(VersionSourceReasons.InvalidData, $"URL not allowed: {request.Uri}");
        }

        Directory.CreateDirectory(_cacheDirectory);
        var key = Hex(SHA256.HashData(Encoding.UTF8.GetBytes(request.Uri.AbsoluteUri)))[..32];
        var cachePath = Path.Combine(_cacheDirectory, key + ".bin");
        var metaPath = Path.Combine(_cacheDirectory, key + ".meta");
        var expected = request.ExpectedSha256?.Trim().ToUpperInvariant();

        // Content addressed by a trusted hash never changes – reuse it without asking the server.
        if (expected is not null && File.Exists(cachePath) && await HashFileAsync(cachePath, cancellationToken).ConfigureAwait(false) == expected)
        {
            return cachePath;
        }

        var meta = ReadMeta(metaPath);
        if (expected is null && File.Exists(cachePath) && meta is not null &&
            _time.GetUtcNow() - meta.Value.FetchedAt < (request.MaxAge ?? DefaultMaxAge))
        {
            return cachePath;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);

        try
        {
            var uri = request.Uri;
            for (var hop = 0; ; hop++)
            {
                using var message = new HttpRequestMessage(HttpMethod.Get, uri);
                if (File.Exists(cachePath) && meta?.ETag is { Length: > 0 } etag && expected is null &&
                    EntityTagHeaderValue.TryParse(etag, out var tag))
                {
                    message.Headers.IfNoneMatch.Add(tag);
                }

                using var response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);

                if (IsRedirect(response.StatusCode))
                {
                    var next = response.Headers.Location is { } location ? new Uri(uri, location) : null;
                    if (hop >= MaxRedirects || !IsAllowed(next, request.AllowedHosts))
                    {
                        throw new VersionSourceException(VersionSourceReasons.InvalidData, $"Redirect not allowed: {uri} → {next}");
                    }

                    uri = next!;
                    continue;
                }

                if (response.StatusCode == HttpStatusCode.NotModified && File.Exists(cachePath))
                {
                    WriteMeta(metaPath, meta?.ETag);
                    return cachePath;
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new VersionSourceException(VersionSourceReasons.ModelNotInCatalog, $"Not found: {uri}");
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new VersionSourceException(VersionSourceReasons.Offline, $"HTTP {(int)response.StatusCode} for {uri}");
                }

                if (response.Content.Headers.ContentLength > request.MaxBytes)
                {
                    throw new VersionSourceException(VersionSourceReasons.InvalidData, $"File too large: {uri}");
                }

                var hash = await SaveAsync(response, cachePath, request.MaxBytes, timeout.Token).ConfigureAwait(false);
                if (expected is not null && hash != expected)
                {
                    File.Delete(cachePath);
                    throw new VersionSourceException(VersionSourceReasons.HashMismatch, $"SHA-256 mismatch for {uri}");
                }

                WriteMeta(metaPath, response.Headers.ETag?.ToString());
                _logger.LogInformation("Downloaded {Uri} ({Bytes} bytes)", uri, new FileInfo(cachePath).Length);
                return cachePath;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new VersionSourceException(VersionSourceReasons.Offline, $"Timeout: {request.Uri}");
        }
        catch (HttpRequestException ex)
        {
            throw new VersionSourceException(VersionSourceReasons.Offline, $"Network error: {request.Uri}", ex);
        }
    }

    private static bool IsRedirect(HttpStatusCode code) =>
        code is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static async Task<string> SaveAsync(HttpResponseMessage response, string path, long maxBytes, CancellationToken ct)
    {
        var temp = path + ".tmp";
        try
        {
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var target = File.Create(temp))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > maxBytes)
                    {
                        throw new VersionSourceException(VersionSourceReasons.InvalidData, "File too large");
                    }

                    sha.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                }
            }

            File.Move(temp, path, overwrite: true);
            return Hex(sha.GetHashAndReset());
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    internal static async Task<string> HashFileAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return Hex(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false));
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes);

    private (string? ETag, DateTimeOffset FetchedAt)? ReadMeta(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var lines = File.ReadAllLines(path);
            return lines.Length >= 2 && long.TryParse(lines[1], out var ticks)
                ? (lines[0].Length > 0 ? lines[0] : null, new DateTimeOffset(ticks, TimeSpan.Zero))
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void WriteMeta(string path, string? etag) =>
        File.WriteAllLines(path, [etag ?? string.Empty, _time.GetUtcNow().UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
}
