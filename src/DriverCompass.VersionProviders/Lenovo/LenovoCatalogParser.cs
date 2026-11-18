using DriverCompass.Core.Versions;
using DriverCompass.VersionProviders.Xml;

namespace DriverCompass.VersionProviders.Lenovo;

/// <summary>One package of a Lenovo machine-type catalog: descriptor XML + its SHA-256.</summary>
public sealed record LenovoPackageRef(Uri Location, string? Category, string Sha256);

/// <summary>Parses Lenovo's update catalogs (<c>catalog/&lt;MT&gt;_Win11.xml</c>) and package descriptors.</summary>
public static class LenovoCatalogParser
{
    public const int MaxPackages = 400;

    public static IReadOnlyList<LenovoPackageRef> ParseCatalog(Stream stream)
    {
        var list = new List<LenovoPackageRef>();
        foreach (var package in SafeXml.ReadChildren(stream, n => n == "package"))
        {
            var sha = package.Kids("checksum")
                .FirstOrDefault(c => string.Equals(c.Attr("type"), "sha256", StringComparison.OrdinalIgnoreCase)).Text();
            if (!Uri.TryCreate(package.Kid("location").Text(), UriKind.Absolute, out var location) ||
                sha is not { Length: 64 } || !sha.All(char.IsAsciiHexDigit))
            {
                continue;
            }

            list.Add(new LenovoPackageRef(location, package.Kid("category").Text(), sha.ToUpperInvariant()));
            if (list.Count >= MaxPackages)
            {
                break;
            }
        }

        return list;
    }

    /// <summary>
    /// Driver entries from a package descriptor: every <c>DetectInstall/…/_Driver</c> with hardware IDs and a version or date.
    /// Packages without driver detection (BIOS, applications) yield nothing.
    /// </summary>
    public static IReadOnlyList<CatalogDriverEntry> ParseDescriptor(Stream stream)
    {
        var root = SafeXml.Load(stream);
        if (root.Name.LocalName != "Package")
        {
            throw SafeXml.Invalid("not a package descriptor");
        }

        var title = root.Kid("Title")?.Kid("Desc").Text() ?? root.Attr("name");
        var releaseDate = SafeXml.ParseDate(root.Kid("ReleaseDate").Text(), "yyyy-MM-dd");
        var detect = root.Kid("DetectInstall");
        if (detect is null)
        {
            return [];
        }

        var entries = new List<CatalogDriverEntry>();
        foreach (var driver in detect.DescendantsAndSelf().Where(e => e.Name.LocalName == "_Driver"))
        {
            var ids = driver.Kids("HardwareID").Select(h => h.Text()).OfType<string>().ToList();
            var version = driver.Kid("Version").Text();
            var date = SafeXml.ParseDate(driver.Kid("Date").Text(), "yyyy-MM-dd") ?? (version is null ? releaseDate : null);
            if (ids.Count > 0 && (version is not null || date is not null))
            {
                entries.Add(new CatalogDriverEntry(ids, version, date, title));
            }
        }

        return entries;
    }
}
