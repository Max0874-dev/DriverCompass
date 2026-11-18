using System.Text.RegularExpressions;

namespace DriverCompass.Core.Links;

/// <summary>Every URL that DriverCompass opens or downloads from goes through these checks.</summary>
public static partial class UrlSafety
{
    /// <summary>Placeholders allowed in OEM URL templates.</summary>
    public static readonly IReadOnlySet<string> KnownPlaceholders =
        new HashSet<string>(StringComparer.Ordinal) { "serial", "model", "sku", "board", "family" };

    /// <summary>HTTPS, default port, no user info, host equal to or a subdomain of an allowed domain.</summary>
    public static bool IsAllowedHttps(Uri? uri, IEnumerable<string> allowedDomains)
    {
        if (uri is null || !uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length > 0)
        {
            return false;
        }

        var host = uri.IdnHost;
        return allowedDomains.Any(d =>
            host.Equals(d, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith("." + d, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<string> GetPlaceholders(string template) =>
        PlaceholderRegex().Matches(template).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>
    /// Replaces the placeholders with URL-encoded values and validates the result.
    /// Returns <c>null</c> if a value is missing or the result is not an allowed HTTPS URL.
    /// </summary>
    public static Uri? Expand(string template, IReadOnlyDictionary<string, string?> values, IEnumerable<string> allowedDomains)
    {
        var missing = false;
        var expanded = PlaceholderRegex().Replace(template, m =>
        {
            if (values.TryGetValue(m.Groups[1].Value, out var v) && !string.IsNullOrWhiteSpace(v))
            {
                return Uri.EscapeDataString(v.Trim());
            }

            missing = true;
            return string.Empty;
        });

        if (missing || !Uri.TryCreate(expanded, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return IsAllowedHttps(uri, allowedDomains) ? uri : null;
    }

    [GeneratedRegex(@"\{([a-z]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderRegex();
}
