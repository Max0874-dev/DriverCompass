using System.Runtime.InteropServices;
using System.Xml.Linq;
using DriverCompass.Core.Links;
using DriverCompass.Core.Versions;
using DriverCompass.VersionProviders.Xml;

namespace DriverCompass.VersionProviders.Dell;

/// <summary>Entry of CatalogIndexPC: one model catalog (Dell systemID → CAB path + SHA-256).</summary>
public sealed record DellModelCatalogRef(IReadOnlyList<string> SystemIds, string Path, string Sha256, string? Version);

/// <summary>
/// Parses Dell's client update catalogs (format "openmanage/cm/dm", schema 3.0):
/// the index (<c>CatalogIndexPC.xml</c>) and the per-model catalog.
/// </summary>
public static class DellCatalogParser
{
    public static IReadOnlyList<DellModelCatalogRef> ParseIndex(Stream stream)
    {
        var list = new List<DellModelCatalogRef>();
        foreach (var group in SafeXml.ReadChildren(stream, n => n == "GroupManifest"))
        {
            var info = group.Kid("ManifestInformation");
            var path = info.Attr("path");
            var sha = info?.Below("Hash").FirstOrDefault(h => string.Equals(h.Attr("algorithm"), "SHA256", StringComparison.OrdinalIgnoreCase)).Text();
            var systemIds = group.Below("Model").Select(m => m.Attr("systemID")).OfType<string>()
                .Select(s => s.ToUpperInvariant()).Distinct().ToList();

            if (path is null || sha is null || systemIds.Count == 0 || !IsSafeRelativePath(path))
            {
                continue;
            }

            list.Add(new DellModelCatalogRef(systemIds, path, sha.ToUpperInvariant(), info.Attr("version")));
        }

        return list;
    }

    /// <summary>Driver entries for the given CPU architecture (components without OS list count for all).</summary>
    public static IReadOnlyList<CatalogDriverEntry> ParseModelCatalog(Stream stream, Architecture architecture)
    {
        var arch = architecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            _ => "x64",
        };

        var entries = new List<CatalogDriverEntry>();
        foreach (var component in SafeXml.ReadChildren(stream, n => n == "SoftwareComponent"))
        {
            if (!string.Equals(component.Kid("ComponentType").Attr("value"), "DRVR", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var systems = component.Kid("SupportedOperatingSystems")?.Kids("OperatingSystem").ToList() ?? [];
            if (systems.Count > 0 && !systems.Any(os => string.Equals(os.Attr("osArch"), arch, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var name = component.Kid("Name")?.Kid("Display").Text();
            var date = SafeXml.ParseDate(component.Attr("releaseDate"), "MMMM dd, yyyy", "MMMM d, yyyy");
            var page = Uri.TryCreate(component.Kid("ImportantInfo").Attr("URL"), UriKind.Absolute, out var u) &&
                       UrlSafety.IsAllowedHttps(u, ["dell.com"])
                ? u
                : null;

            // Only the device entries carry the real INF driver version; vendorVersion is the package version.
            foreach (var device in component.Below("Device"))
            {
                var version = device.Attr("version");
                if (version is null)
                {
                    continue;
                }

                var ids = HardwareIds(device).ToList();
                if (ids.Count > 0)
                {
                    entries.Add(new CatalogDriverEntry(ids, version, date, name, page));
                }
            }
        }

        return entries;
    }

    private static IEnumerable<string> HardwareIds(XElement device)
    {
        foreach (var generic in device.Kids("Generic"))
        {
            if (generic.Text() is { } id)
            {
                yield return id;
            }
        }

        foreach (var pci in device.Kids("PCIInfo"))
        {
            var ven = Hex4(pci.Attr("vendorID"));
            var dev = Hex4(pci.Attr("deviceID"));
            if (ven is null || dev is null)
            {
                continue;
            }

            var subVen = Hex4(pci.Attr("subVendorID"));
            var subDev = Hex4(pci.Attr("subDeviceID"));
            yield return subVen is not null && subDev is not null
                ? $@"PCI\VEN_{ven}&DEV_{dev}&SUBSYS_{subDev}{subVen}"
                : $@"PCI\VEN_{ven}&DEV_{dev}";
        }
    }

    private static string? Hex4(string? value) =>
        value is { Length: 4 } && value.All(char.IsAsciiHexDigit) ? value.ToUpperInvariant() : null;

    /// <summary>Relative "FOLDER…/1/Name.cab" path – no scheme, no "..", no backslashes, no query.</summary>
    internal static bool IsSafeRelativePath(string path) =>
        path.Length is > 0 and < 300 &&
        !path.Contains("..", StringComparison.Ordinal) &&
        !path.Contains(':', StringComparison.Ordinal) &&
        !path.Contains('\\', StringComparison.Ordinal) &&
        !path.Contains('?', StringComparison.Ordinal) &&
        !path.Contains('#', StringComparison.Ordinal) &&
        !path.StartsWith('/');
}
