using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DriverCompass.Core;
using DriverCompass.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DriverCompass.Data;

/// <summary>
/// Asks the public GitHub API for the latest published release of the app's own repository.
/// <list type="bullet">
/// <item>Plain GET of <c>api.github.com/repos/&lt;owner&gt;/&lt;repo&gt;/releases/latest</c>, HTTPS only, size and time limits.</item>
/// <item>Only the version number is taken from the answer. The link shown to the user is built from the
/// repository URL compiled into the app, never taken from the response.</item>
/// <item>Any failure simply means "no hint" – the check is a convenience, not a security feature.</item>
/// </list>
/// </summary>
public sealed partial class GitHubReleaseChecker : IAppUpdateChecker
{
    public const string ApiHost = "api.github.com";
    public const int MaxBytes = 512 * 1024;

    private readonly HttpClient _http;
    private readonly Uri? _repository;
    private readonly TimeSpan _timeout;
    private readonly ILogger _logger;

    public GitHubReleaseChecker(HttpClient http, string repositoryUrl, TimeSpan? timeout = null, ILogger<GitHubReleaseChecker>? logger = null)
    {
        _http = http;
        _repository = ParseRepository(repositoryUrl);
        _timeout = timeout ?? TimeSpan.FromSeconds(10);
        _logger = logger ?? NullLogger<GitHubReleaseChecker>.Instance;
    }

    /// <summary>The repository from <c>Directory.Build.props</c>.</summary>
    public static GitHubReleaseChecker CreateDefault(HttpClient http, ILogger<GitHubReleaseChecker>? logger = null) =>
        new(http, AppInfo.RepositoryUrl, logger: logger);

    public async Task<AppUpdateInfo?> CheckAsync(string currentVersion, CancellationToken cancellationToken = default)
    {
        if (_repository is null)
        {
            _logger.LogInformation("Update check skipped: no GitHub repository configured");
            return null;
        }

        var api = new Uri($"https://{ApiHost}/repos{_repository.AbsolutePath}/releases/latest");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, api);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            var finalUri = response.RequestMessage?.RequestUri;
            if (finalUri is not null && (finalUri.Scheme != Uri.UriSchemeHttps || !finalUri.Host.Equals(ApiHost, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogWarning("Update check: redirected away from {Host} – ignored", ApiHost);
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Update check: HTTP {Status}", (int)response.StatusCode);
                return null;
            }

            if (response.Content.Headers.ContentLength > MaxBytes)
            {
                _logger.LogWarning("Update check: response too large");
                return null;
            }

            var body = await ReadLimitedAsync(response.Content, timeout.Token).ConfigureAwait(false);
            if (body is null)
            {
                _logger.LogWarning("Update check: response exceeded the size limit");
                return null;
            }

            var latest = ParseLatestVersion(body);
            if (latest is null)
            {
                _logger.LogWarning("Update check: no usable release version in the response");
                return null;
            }

            if (!IsNewer(latest, currentVersion))
            {
                _logger.LogInformation("Update check: {Current} is up to date (latest {Latest})", currentVersion, latest);
                return null;
            }

            _logger.LogInformation("Update check: {Latest} is available (installed {Current})", latest, currentVersion);
            return new AppUpdateInfo(latest, ReleasePage(_repository, latest));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Update check: timeout");
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogInformation("Update check failed: {Message}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Reads <c>tag_name</c> of a published, final release. Returns the plain version ("0.2.0") or <c>null</c>
    /// for drafts, pre-releases, malformed JSON or tags that are not <c>vX.Y.Z</c>.
    /// </summary>
    public static string? ParseLatestVersion(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                IsTrue(root, "draft") || IsTrue(root, "prerelease") ||
                !root.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var match = TagPattern().Match(tag.GetString()!);
            return match.Success ? match.Groups["v"].Value : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>True if <paramref name="latest"/> is a higher X.Y.Z than <paramref name="current"/>. A suffix like "-dev" is ignored.</summary>
    public static bool IsNewer(string latest, string current) =>
        TryParse(latest, out var l) && TryParse(current, out var c) && l > c;

    private static bool TryParse(string value, out Version version)
    {
        version = new Version(0, 0, 0);
        var core = value.Split('-', '+')[0].TrimStart('v', 'V');
        var parts = core.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        var numbers = new int[3];
        for (var i = 0; i < 3; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
            {
                return false;
            }
        }

        version = new Version(numbers[0], numbers[1], numbers[2]);
        return true;
    }

    private static bool IsTrue(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    /// <summary>Accepts only <c>https://github.com/&lt;owner&gt;/&lt;repo&gt;</c> with plain names.</summary>
    private static Uri? ParseRepository(string url)
    {
        if (!Uri.TryCreate(url.TrimEnd('/'), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            !RepositoryPathPattern().IsMatch(uri.AbsolutePath) ||
            uri.AbsolutePath.Contains("/OWNER/", StringComparison.Ordinal))
        {
            return null;
        }

        return uri;
    }

    private static Uri ReleasePage(Uri repository, string version) =>
        new($"https://github.com{repository.AbsolutePath}/releases/tag/v{version}");

    private static async Task<string?> ReadLimitedAsync(HttpContent content, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16384];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    [GeneratedRegex(@"^v(?<v>\d{1,4}\.\d{1,4}\.\d{1,4})$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"^/[A-Za-z0-9-]{1,39}/[A-Za-z0-9._-]{1,100}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex RepositoryPathPattern();
}
