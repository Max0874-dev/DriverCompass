using System.Text.RegularExpressions;
using DriverCompass.Core.Installation;
using DriverCompass.Core.Models;
using DriverCompass.Core.Versions;
using DriverCompass.VersionProviders.Net;
using DriverCompass.VersionProviders.Packages;
using DriverCompass.VersionProviders.Xml;

namespace DriverCompass.VersionProviders.Lenovo;

/// <summary>One package of a Lenovo machine-type catalog: descriptor XML + its SHA-256.</summary>
public sealed record LenovoPackageRef(Uri Location, string? Category, string Sha256);

/// <summary>Parses Lenovo's update catalogs (<c>catalog/&lt;MT&gt;_Win11.xml</c>) and package descriptors.</summary>
public static partial class LenovoCatalogParser
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
    public static IReadOnlyList<CatalogDriverEntry> ParseDescriptor(Stream stream) => ReadDescriptor(stream).Entries;

    /// <summary>A parsed descriptor: its driver entries plus what stage 3 needs to install it.</summary>
    /// <param name="Id">Package id (e.g. "n32li04w").</param>
    /// <param name="Title">Display name.</param>
    /// <param name="Entries">Driver entries (with <see cref="CatalogDriverEntry.PackageId"/> set when installable).</param>
    /// <param name="InstallerFileName">Installer EXE name from <c>Files/Installer</c>.</param>
    /// <param name="InstallerSha256">Its SHA-256 (Lenovo calls the field "CRC").</param>
    /// <param name="IsInstallable">Id, installer, hash and the standard extract command are all valid.</param>
    public sealed record LenovoDescriptor(
        string? Id,
        string? Title,
        IReadOnlyList<CatalogDriverEntry> Entries,
        string? InstallerFileName,
        string? InstallerSha256,
        bool IsInstallable);

    public static LenovoDescriptor ReadDescriptor(Stream stream)
    {
        var root = SafeXml.Load(stream);
        if (root.Name.LocalName != "Package")
        {
            throw SafeXml.Invalid("not a package descriptor");
        }

        var id = root.Attr("id");
        var title = root.Kid("Title")?.Kid("Desc").Text() ?? root.Attr("name");
        var releaseDate = SafeXml.ParseDate(root.Kid("ReleaseDate").Text(), "yyyy-MM-dd");

        // Stage 3: only packages with the standard Lenovo extract switch, a plain installer name and a SHA-256.
        var installer = root.Kid("Files")?.Kid("Installer")?.Kid("File");
        var installerName = installer?.Kid("Name").Text();
        var sha = installer?.Kid("CRC").Text();
        var installable =
            InstallProtocol.IsLenovoPackageId(id) &&
            installerName is not null && InstallerNameRegex().IsMatch(installerName) &&
            sha is { Length: 64 } && sha.All(char.IsAsciiHexDigit) &&
            IsStandardExtractCommand(root.Kid("ExtractCommand").Text(), installerName);

        var entries = new List<CatalogDriverEntry>();
        var detect = root.Kid("DetectInstall");
        foreach (var driver in detect?.DescendantsAndSelf().Where(e => e.Name.LocalName == "_Driver") ?? [])
        {
            var ids = driver.Kids("HardwareID").Select(h => h.Text()).OfType<string>().ToList();
            var version = driver.Kid("Version").Text();
            var date = SafeXml.ParseDate(driver.Kid("Date").Text(), "yyyy-MM-dd") ?? (version is null ? releaseDate : null);
            if (ids.Count > 0 && (version is not null || date is not null))
            {
                entries.Add(new CatalogDriverEntry(ids, version, date, title, null, installable ? id : null));
            }
        }

        return new LenovoDescriptor(id, title, entries, installerName, installable ? sha!.ToUpperInvariant() : null, installable && entries.Count > 0);
    }

    /// <summary>Builds the installable package from a descriptor found at <paramref name="descriptorLocation"/>.</summary>
    public static OemPackage? ToPackage(LenovoDescriptor descriptor, Uri descriptorLocation)
    {
        if (!descriptor.IsInstallable || descriptor.Id is null || descriptor.InstallerFileName is null || descriptor.InstallerSha256 is null)
        {
            return null;
        }

        var download = new Uri(descriptorLocation, descriptor.InstallerFileName);
        if (!CatalogDownloader.IsAllowed(download, LenovoVersionProvider.Hosts))
        {
            return null;
        }

        return new OemPackage(
            VersionSourceIds.Lenovo,
            descriptor.Id,
            download,
            descriptor.InstallerSha256,
            null,
            descriptor.Title,
            descriptor.Entries.Select(e => VersionMatching.Clean(e.Version)).OfType<string>()
                .OrderByDescending(v => DriverVersion.TryParse(v, out var dv) ? dv : default).FirstOrDefault(),
            [.. descriptor.Entries.SelectMany(e => e.HardwareIds).Distinct(StringComparer.OrdinalIgnoreCase)],
            LenovoVersionProvider.Signers,
            LenovoVersionProvider.Hosts,
            PackageExtraction.LenovoInno,
            descriptor.InstallerFileName);
    }

    /// <summary>
    /// Exactly Lenovo's standard command "&lt;installer&gt; /VERYSILENT /DIR=%PACKAGEPATH% /EXTRACT="YES"" – nothing else.
    /// DriverCompass never runs this text; it only proves the package supports extraction and builds fixed arguments itself.
    /// </summary>
    public static bool IsStandardExtractCommand(string? command, string installerName) =>
        command is not null &&
        ExtractCommandRegex().Match(command.Trim()) is { Success: true } m &&
        string.Equals(m.Groups["exe"].Value, installerName, StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"^(?<exe>[A-Za-z0-9_\-]{1,64}\.exe) /VERYSILENT /DIR=%PACKAGEPATH% /EXTRACT=""YES""$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExtractCommandRegex();

    [GeneratedRegex(@"^[A-Za-z0-9_\-]{1,64}\.exe$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InstallerNameRegex();
}
