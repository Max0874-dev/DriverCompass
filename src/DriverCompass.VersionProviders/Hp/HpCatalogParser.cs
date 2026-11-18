using System.Globalization;
using DriverCompass.Core.Installation;
using DriverCompass.Core.Links;
using DriverCompass.Core.Models;
using DriverCompass.Core.Versions;
using DriverCompass.VersionProviders.Net;
using DriverCompass.VersionProviders.Packages;
using DriverCompass.VersionProviders.Xml;

namespace DriverCompass.VersionProviders.Hp;

/// <summary>A Windows release for which HP publishes a reference file for one platform.</summary>
public sealed record HpPlatformOs(bool IsWindows11, string ReleaseIdFilename, int Build);

/// <summary>
/// Parses HP Image Assistant data: <c>platformList.xml</c> (platform ID → supported Windows releases)
/// and the per-platform reference file (devices with driver version + SoftPaq).
/// </summary>
public static class HpCatalogParser
{
    public static IReadOnlyList<HpPlatformOs> ParsePlatformList(Stream stream, string platformId)
    {
        foreach (var platform in SafeXml.ReadChildren(stream, n => n == "Platform"))
        {
            if (!string.Equals(platform.Kid("SystemID").Text(), platformId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return platform.Kids("OS")
                .Select(os => new HpPlatformOs(
                    string.Equals(os.Kid("IsWindows11").Text(), "true", StringComparison.OrdinalIgnoreCase),
                    os.Kid("OSReleaseIdFilename").Text() ?? string.Empty,
                    int.TryParse(os.Kid("OSBuildId").Text(), NumberStyles.None, CultureInfo.InvariantCulture, out var b) ? b : 0))
                .Where(os => os.ReleaseIdFilename.Length > 0 && os.ReleaseIdFilename.All(char.IsAsciiLetterOrDigit))
                .ToList();
        }

        return [];
    }

    /// <summary>Picks the reference file closest to the installed Windows: same generation, same release, else newest not newer than this build.</summary>
    public static HpPlatformOs? ChooseOs(IReadOnlyList<HpPlatformOs> available, bool isWindows11, string? displayVersion, int? build)
    {
        var sameGeneration = available.Where(o => o.IsWindows11 == isWindows11).ToList();
        if (sameGeneration.Count == 0)
        {
            return null;
        }

        return sameGeneration.FirstOrDefault(o => string.Equals(o.ReleaseIdFilename, displayVersion, StringComparison.OrdinalIgnoreCase))
            ?? sameGeneration.Where(o => build is null || o.Build <= build).OrderByDescending(o => o.Build).FirstOrDefault()
            ?? sameGeneration.OrderByDescending(o => o.Build).First();
    }

    public static Uri ReferenceUri(string platformId, HpPlatformOs os)
    {
        var id = platformId.ToLowerInvariant();
        var generation = os.IsWindows11 ? "11.0" : "10.0";
        return new Uri($"https://hpia.hpcloud.hp.com/ref/{id}/{id}_64_{generation}.{os.ReleaseIdFilename.ToLowerInvariant()}.cab");
    }

    /// <summary>
    /// Devices of HP's reference image that have a SoftPaq solution. The device's <c>DriverVersion</c> is the
    /// version that SoftPaq installs; IDs come from <c>HardwareId</c> or a hardware-ID-shaped <c>DeviceId</c>.
    /// </summary>
    public static IReadOnlyList<CatalogDriverEntry> ParseReference(Stream stream)
    {
        var (devices, solutions) = Read(stream);
        return devices
            .Where(d => d.Version is not null || d.Date is not null)
            .Select(d =>
            {
                var solution = solutions.GetValueOrDefault(d.Ref);
                return new CatalogDriverEntry(d.Ids, d.Version, d.Date, solution?.Name, solution?.Notes, solution?.Download is not null ? solution.Id : null);
            })
            .ToList();
    }

    /// <summary>Stage 3: the SoftPaq with download URL (ftp.hp.com, HTTPS) and SHA-256 from the signed reference file.</summary>
    public static OemPackage? FindPackage(Stream stream, string softPaqId)
    {
        var (devices, solutions) = Read(stream);
        if (solutions.GetValueOrDefault(softPaqId) is not { Download: { } download, Sha256: { } sha } solution)
        {
            return null;
        }

        var forPackage = devices.Where(d => string.Equals(d.Ref, solution.Id, StringComparison.OrdinalIgnoreCase)).ToList();
        if (forPackage.Count == 0)
        {
            return null;
        }

        return new OemPackage(
            VersionSourceIds.Hp,
            solution.Id,
            download,
            sha,
            solution.Size,
            solution.Name,
            forPackage.Select(d => d.Version).OfType<string>().OrderByDescending(v => DriverVersion.TryParse(v, out var dv) ? dv : default).FirstOrDefault(),
            [.. forPackage.SelectMany(d => d.Ids).Distinct(StringComparer.OrdinalIgnoreCase)],
            HpVersionProvider.Signers,
            HpVersionProvider.PackageHosts);
    }

    private sealed record Solution(string Id, string? Name, Uri? Notes, Uri? Download, string? Sha256, long? Size);

    private static (List<(List<string> Ids, string? Version, DateOnly? Date, string Ref)> Devices, Dictionary<string, Solution> Solutions) Read(Stream stream)
    {
        var devices = new List<(List<string> Ids, string? Version, DateOnly? Date, string Ref)>();
        var solutions = new Dictionary<string, Solution>(StringComparer.OrdinalIgnoreCase);

        foreach (var section in SafeXml.ReadChildren(stream, n => n is "Devices" or "Solutions"))
        {
            if (section.Name.LocalName == "Devices")
            {
                foreach (var device in section.Kids("Device"))
                {
                    var solutionRef = device.Kid("Solutions")?.Kids("UpdateInfo").Select(u => u.Attr("IdRef")).OfType<string>().FirstOrDefault();
                    if (solutionRef is null)
                    {
                        continue;
                    }

                    var ids = new List<string>();
                    if (device.Kid("HardwareId").Text() is { } hardwareId)
                    {
                        ids.Add(hardwareId);
                    }

                    // DeviceId is either a hardware ID ("PCI\VEN_…&DEV_…&REV_60") or an instance path (two or more '\').
                    if (device.Kid("DeviceId").Text() is { } deviceId && deviceId.Count(c => c == '\\') == 1)
                    {
                        ids.Add(deviceId);
                    }

                    if (ids.Count > 0)
                    {
                        devices.Add((ids, device.Kid("DriverVersion").Text(), SafeXml.ParseDate(device.Kid("DriverDate").Text(), "MM/dd/yyyy"), solutionRef));
                    }
                }
            }
            else
            {
                foreach (var update in section.Kids("UpdateInfo"))
                {
                    if (update.Kid("Id").Text() is not { } id)
                    {
                        continue;
                    }

                    var notes = update.Kid("ReleaseNotesUrl").Text();
                    var uri = notes is not null && Uri.TryCreate(notes.Contains("://", StringComparison.Ordinal) ? notes : "https://" + notes, UriKind.Absolute, out var u) &&
                              UrlSafety.IsAllowedHttps(u, ["hp.com"])
                        ? u
                        : null;
                    // Installable only as a SoftPaq number with an HTTPS URL on ftp.hp.com and a SHA-256.
                    var url = update.Kid("Url").Text();
                    var download = InstallProtocol.IsHpSoftPaqId(id) && url is not null &&
                                   Uri.TryCreate(url.Contains("://", StringComparison.Ordinal) ? url : "https://" + url, UriKind.Absolute, out var d) &&
                                   CatalogDownloader.IsAllowed(d, HpVersionProvider.PackageHosts)
                        ? d
                        : null;
                    var sha = update.Kid("SHA256").Text();
                    if (sha is not { Length: 64 } || !sha.All(char.IsAsciiHexDigit))
                    {
                        download = null;
                        sha = null;
                    }

                    var size = long.TryParse(update.Kid("Size").Text(), NumberStyles.None, CultureInfo.InvariantCulture, out var s) ? s : (long?)null;
                    solutions[id] = new Solution(id, update.Kid("Name").Text(), uri, download, sha?.ToUpperInvariant(), size);
                }
            }
        }

        return (devices, solutions);
    }
}
