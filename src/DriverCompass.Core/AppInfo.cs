using System.Reflection;

namespace DriverCompass.Core;

/// <summary>
/// Central product identity. The values come from <c>Directory.Build.props</c>
/// (single source of truth) via <see cref="AssemblyMetadataAttribute"/>.
/// </summary>
public static class AppInfo
{
    private static readonly Dictionary<string, string> Metadata =
        typeof(AppInfo).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(a => a.Value is not null)
            .GroupBy(a => a.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Value!, StringComparer.Ordinal);

    /// <summary>User-visible product name (window title, reports).</summary>
    public static string DisplayName => Get("DriverCompass.DisplayName", "DriverCompass");

    /// <summary>Technical, stable identifier used for folders. Not meant to change with the display name.</summary>
    public const string TechnicalName = "DriverCompass";

    public static string RepositoryUrl => Get("DriverCompass.RepositoryUrl", string.Empty);

    /// <summary>URL of the online vendor catalog (raw GitHub file).</summary>
    public static string CatalogUrl => Get("DriverCompass.CatalogUrl", string.Empty);

    /// <summary>False if no catalog URL is set or it still contains an OWNER placeholder.</summary>
    public static bool IsRepositoryConfigured =>
        !string.IsNullOrWhiteSpace(CatalogUrl) && !CatalogUrl.Contains("/OWNER/", StringComparison.Ordinal);

    public static string Version
    {
        get
        {
            var info = typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrEmpty(info))
            {
                return typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            }

            var plus = info.IndexOf('+', StringComparison.Ordinal);
            return plus >= 0 ? info[..plus] : info;
        }
    }

    /// <summary>%LOCALAPPDATA%\DriverCompass – settings, cache and logs live below this folder.</summary>
    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), TechnicalName);

    private static string Get(string key, string fallback) =>
        Metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
}
