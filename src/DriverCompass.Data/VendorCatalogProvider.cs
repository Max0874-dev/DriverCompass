using System.Net;
using System.Net.Http.Headers;
using System.Text;
using DriverCompass.Core;
using DriverCompass.Core.Abstractions;
using DriverCompass.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DriverCompass.Data;

public sealed record CatalogProviderOptions
{
    /// <summary>Raw GitHub URL of data/vendors.json. <c>null</c> = offline only.</summary>
    public Uri? RemoteUri { get; init; }

    public bool AllowRemote { get; init; } = true;

    public required string CacheDirectory { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);

    public int MaxBytes { get; init; } = VendorCatalogValidator.MaxBytes;

    /// <summary>Default options: URL from Directory.Build.props, cache in %LOCALAPPDATA%\DriverCompass\cache.</summary>
    public static CatalogProviderOptions CreateDefault(bool allowRemote) => new()
    {
        RemoteUri = AppInfo.IsRepositoryConfigured && Uri.TryCreate(AppInfo.CatalogUrl, UriKind.Absolute, out var uri) ? uri : null,
        AllowRemote = allowRemote,
        CacheDirectory = Path.Combine(AppInfo.DataDirectory, "cache"),
    };
}

/// <summary>Warning codes in <see cref="CatalogInfo.Warnings"/> (localized by the app).</summary>
public static class CatalogWarnings
{
    /// <summary>Download failed; the built-in copy is used.</summary>
    public const string RemoteUnavailable = "Catalog_RemoteUnavailable";

    /// <summary>Download failed; the last downloaded copy from the cache is used.</summary>
    public const string RemoteUnavailableCached = "Catalog_RemoteUnavailableCached";
    public const string RemoteInvalid = "Catalog_RemoteInvalid";
    public const string RemoteOlder = "Catalog_RemoteOlder";

    /// <summary>The online list uses a newer schema; this app version cannot read it.</summary>
    public const string RemoteNeedsNewerApp = "Catalog_RemoteNeedsNewerApp";
    public const string CacheInvalid = "Catalog_CacheInvalid";
}

/// <summary>
/// Loads the vendor catalog: remote (HTTPS, ETag, size limit, validated) → local cache (validated) → embedded copy.
/// A remote or cached file older than the embedded one is ignored (downgrade protection).
/// The request carries no device data – it is a plain GET of a public file.
/// </summary>
public sealed class VendorCatalogProvider : IVendorCatalogProvider
{
    private const string CacheFileName = "vendors.json";
    private const string EtagFileName = "vendors.etag";

    private readonly HttpClient _http;
    private readonly IVendorCatalogValidator _validator;
    private readonly CatalogProviderOptions _options;
    private readonly Func<string> _embeddedJson;
    private readonly ILogger _logger;

    public VendorCatalogProvider(
        HttpClient http,
        IVendorCatalogValidator validator,
        CatalogProviderOptions options,
        Func<string>? embeddedJson = null,
        ILogger<VendorCatalogProvider>? logger = null)
    {
        _http = http;
        _validator = validator;
        _options = options;
        _embeddedJson = embeddedJson ?? (() => EmbeddedData.VendorsJson);
        _logger = logger ?? NullLogger<VendorCatalogProvider>.Instance;
    }

    private string CachePath => Path.Combine(_options.CacheDirectory, CacheFileName);
    private string EtagPath => Path.Combine(_options.CacheDirectory, EtagFileName);

    public async Task<CatalogLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();

        var embedded = _validator.Validate(_embeddedJson());
        if (!embedded.IsValid || embedded.Catalog is null)
        {
            // Guarded by unit tests and CI – should never happen in a release.
            throw new InvalidOperationException("Embedded vendor catalog is invalid: " + string.Join("; ", embedded.Errors));
        }

        var best = embedded.Catalog;
        var source = CatalogSource.Embedded;

        var cached = LoadCache(warnings);
        if (cached is not null && IsNotOlder(cached, best))
        {
            best = cached;
            source = CatalogSource.Cache;
        }

        if (_options.AllowRemote && _options.RemoteUri is not null)
        {
            var etag = cached is not null ? ReadEtag() : null;
            var remote = await FetchAsync(_options.RemoteUri, etag, cancellationToken).ConfigureAwait(false);

            switch (remote.Outcome)
            {
                case FetchOutcome.NotModified:
                    _logger.LogInformation("Vendor catalog not modified on server");
                    break;

                case FetchOutcome.Ok:
                    var validation = _validator.Validate(remote.Body!);
                    if (validation.RequiresNewerApp)
                    {
                        _logger.LogWarning("Remote vendor catalog needs a newer app version – ignored");
                        warnings.Add(CatalogWarnings.RemoteNeedsNewerApp);
                    }
                    else if (!validation.IsValid || validation.Catalog is null)
                    {
                        _logger.LogWarning("Remote vendor catalog rejected: {Errors}", string.Join("; ", validation.Errors));
                        warnings.Add(CatalogWarnings.RemoteInvalid);
                    }
                    else if (!IsNotOlder(validation.Catalog, embedded.Catalog))
                    {
                        _logger.LogWarning("Remote vendor catalog {Remote} is older than embedded {Embedded} – ignored",
                            validation.Catalog.DataVersion, embedded.Catalog.DataVersion);
                        warnings.Add(CatalogWarnings.RemoteOlder);
                    }
                    else
                    {
                        best = validation.Catalog;
                        source = CatalogSource.Remote;
                        SaveCache(remote.Body!, remote.ETag);
                    }

                    break;

                default:
                    warnings.Add(source == CatalogSource.Cache ? CatalogWarnings.RemoteUnavailableCached : CatalogWarnings.RemoteUnavailable);
                    break;
            }
        }

        _logger.LogInformation("Vendor catalog in use: {Source} {Version}", source, best.DataVersion);
        return new CatalogLoadResult(best, new CatalogInfo(source, best.DataVersion, warnings));
    }

    private static bool IsNotOlder(IVendorCatalog candidate, IVendorCatalog reference) =>
        VendorCatalogValidator.TryParseDataVersion(candidate.DataVersion, out var c) &&
        VendorCatalogValidator.TryParseDataVersion(reference.DataVersion, out var r) &&
        c >= r;

    private IVendorCatalog? LoadCache(List<string> warnings)
    {
        try
        {
            if (!File.Exists(CachePath))
            {
                return null;
            }

            var json = File.ReadAllText(CachePath, Encoding.UTF8);
            var result = _validator.Validate(json);
            if (result.IsValid)
            {
                return result.Catalog;
            }

            if (result.RequiresNewerApp)
            {
                // Only after going back to an older app version – keep it for when the user updates again.
                _logger.LogInformation("Cached vendor catalog needs a newer app version – not used");
                return null;
            }

            _logger.LogWarning("Cached vendor catalog invalid, deleting: {Errors}", string.Join("; ", result.Errors));
            warnings.Add(CatalogWarnings.CacheInvalid);
            DeleteCache();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Vendor catalog cache unreadable");
        }

        return null;
    }

    private string? ReadEtag()
    {
        try
        {
            return File.Exists(EtagPath) ? File.ReadAllText(EtagPath).Trim() : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void SaveCache(string json, string? etag)
    {
        try
        {
            Directory.CreateDirectory(_options.CacheDirectory);
            var temp = CachePath + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            File.Move(temp, CachePath, overwrite: true);

            if (!string.IsNullOrWhiteSpace(etag))
            {
                File.WriteAllText(EtagPath, etag);
            }
            else if (File.Exists(EtagPath))
            {
                File.Delete(EtagPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not write vendor catalog cache");
        }
    }

    private void DeleteCache()
    {
        try
        {
            File.Delete(CachePath);
            File.Delete(EtagPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not delete vendor catalog cache");
        }
    }

    private async Task<FetchResult> FetchAsync(Uri uri, string? etag, CancellationToken cancellationToken)
    {
        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            _logger.LogWarning("Remote catalog URL must use HTTPS – skipped");
            return FetchResult.Failed;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.Timeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (!string.IsNullOrWhiteSpace(etag) && EntityTagHeaderValue.TryParse(etag, out var tag))
            {
                request.Headers.IfNoneMatch.Add(tag);
            }

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return new FetchResult(FetchOutcome.NotModified, null, etag);
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Remote vendor catalog: HTTP {Status}", (int)response.StatusCode);
                return FetchResult.Failed;
            }

            if (response.Content.Headers.ContentLength > _options.MaxBytes)
            {
                _logger.LogWarning("Remote vendor catalog too large ({Length} bytes)", response.Content.Headers.ContentLength);
                return FetchResult.Failed;
            }

            var body = await ReadLimitedAsync(response.Content, _options.MaxBytes, timeout.Token).ConfigureAwait(false);
            if (body is null)
            {
                _logger.LogWarning("Remote vendor catalog exceeded the size limit");
                return FetchResult.Failed;
            }

            return new FetchResult(FetchOutcome.Ok, body, response.Headers.ETag?.ToString());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Remote vendor catalog: timeout");
            return FetchResult.Failed;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("Remote vendor catalog unavailable: {Message}", ex.Message);
            return FetchResult.Failed;
        }
    }

    private static async Task<string?> ReadLimitedAsync(HttpContent content, int maxBytes, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length).TrimStart('﻿');
    }

    private enum FetchOutcome
    {
        Failed,
        NotModified,
        Ok,
    }

    private sealed record FetchResult(FetchOutcome Outcome, string? Body, string? ETag)
    {
        public static FetchResult Failed { get; } = new(FetchOutcome.Failed, null, null);
    }
}
