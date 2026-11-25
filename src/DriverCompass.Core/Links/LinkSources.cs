using DriverCompass.Core.Abstractions;
using DriverCompass.Core.Evaluation;
using DriverCompass.Core.Models;

namespace DriverCompass.Core.Links;

/// <summary>1st choice: support page of the PC or mainboard manufacturer, as specific as possible.</summary>
public sealed class OemSupportLinkSource : ILinkSource
{
    public int Priority => 0;

    public IEnumerable<DriverLink> GetLinks(DeviceAssessment assessment, LinkContext context)
    {
        var match = context.Catalog.FindOem(context.System);
        if (match is null)
        {
            yield break;
        }

        // Board vendors don't ship drivers for external USB/Bluetooth peripherals.
        if (match.Source == OemMatchSource.BaseBoard &&
            assessment.PrimaryId?.Bus is HardwareBus.Usb or HardwareBus.Hid or HardwareBus.Bluetooth)
        {
            yield break;
        }

        var values = BuildValues(context, match.Source);
        var oem = match.Vendor;
        DriverLink? specific = null;

        foreach (var template in oem.SupportUrls)
        {
            var uri = UrlSafety.Expand(template.Template, values, oem.AllowedDomains);
            if (uri is null)
            {
                continue;
            }

            var link = new DriverLink(DriverLinkKind.OemSupport, uri, SpecificityOf(template), oem.Name, oem.Id);
            if (specific is null)
            {
                specific = link;
                yield return link;
                if (link.Specificity == LinkSpecificity.Generic)
                {
                    yield break;
                }

                continue;
            }

            // Also offer the general support page as a fallback.
            if (link.Specificity == LinkSpecificity.Generic)
            {
                yield return link;
                yield break;
            }
        }
    }

    internal static Dictionary<string, string?> BuildValues(LinkContext context, OemMatchSource source)
    {
        var s = context.System;
        var board = SystemInfoSanitizer.Clean(s.BaseBoardProduct);
        return new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["serial"] = context.Options.UseSerialNumberInLinks && source == OemMatchSource.ComputerSystem
                ? SystemInfoSanitizer.Clean(s.SerialNumber)
                : null,
            ["model"] = source == OemMatchSource.ComputerSystem ? SystemInfoSanitizer.Clean(s.Model) : board,
            ["sku"] = source == OemMatchSource.ComputerSystem ? SystemInfoSanitizer.Clean(s.SystemSku) : null,
            ["family"] = source == OemMatchSource.ComputerSystem ? SystemInfoSanitizer.Clean(s.SystemFamily) : null,
            ["board"] = board,
        };
    }

    private static LinkSpecificity SpecificityOf(SupportUrlTemplate t) =>
        t.Requires.Contains("serial") ? LinkSpecificity.Serial :
        t.Requires.Count > 0 ? LinkSpecificity.Model :
        LinkSpecificity.Generic;
}

/// <summary>2nd choice: official driver page of the chip vendor (NVIDIA, AMD, Intel, Realtek …).</summary>
public sealed class ChipVendorLinkSource : ILinkSource
{
    public int Priority => 1;

    public IEnumerable<DriverLink> GetLinks(DeviceAssessment assessment, LinkContext context)
    {
        var chip = assessment.ChipVendor;
        if (chip is null)
        {
            return [];
        }

        var deviceClass = assessment.Device.DeviceClass;
        var classSpecific = chip.DriverPages.Where(p =>
            p.DeviceClasses.Count > 0 && deviceClass is not null &&
            p.DeviceClasses.Contains(deviceClass, StringComparer.OrdinalIgnoreCase));
        var general = chip.DriverPages.Where(p => p.DeviceClasses.Count == 0);

        return classSpecific.Concat(general)
            .Where(p => UrlSafety.IsAllowedHttps(p.Url, chip.AllowedDomains))
            .Take(2)
            .Select(p => new DriverLink(DriverLinkKind.ChipVendor, p.Url, LinkSpecificity.Generic, chip.Name))
            .ToList();
    }
}

/// <summary>Fallback: Microsoft Update Catalog search with the hardware ID.</summary>
public sealed class MicrosoftCatalogLinkSource : ILinkSource
{
    public const string SourceName = "Microsoft Update Catalog";
    private const string SearchBase = "https://www.catalog.update.microsoft.com/Search.aspx?q=";

    public int Priority => 2;

    public static Uri BuildSearchUri(string hardwareId) =>
        new(SearchBase + Uri.EscapeDataString(hardwareId.Trim()));

    public IEnumerable<DriverLink> GetLinks(DeviceAssessment assessment, LinkContext context)
    {
        var id = assessment.Device.HardwareIds.FirstOrDefault(h => !string.IsNullOrWhiteSpace(h));
        if (id is null)
        {
            return [];
        }

        return [new DriverLink(DriverLinkKind.MicrosoftCatalog, BuildSearchUri(id), LinkSpecificity.HardwareId, SourceName)];
    }
}

/// <summary>Runs all sources in priority order and removes duplicate URLs.</summary>
public sealed class LinkResolver(IEnumerable<ILinkSource> sources) : ILinkResolver
{
    private readonly IReadOnlyList<ILinkSource> _sources = sources.OrderBy(s => s.Priority).ToList();

    public static LinkResolver CreateDefault() =>
        new([new OemSupportLinkSource(), new ChipVendorLinkSource(), new MicrosoftCatalogLinkSource()]);

    public IReadOnlyList<DriverLink> Resolve(DeviceAssessment assessment, LinkContext context)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<DriverLink>();
        foreach (var source in _sources)
        {
            foreach (var link in source.GetLinks(assessment, context))
            {
                if (seen.Add(link.Uri.AbsoluteUri))
                {
                    result.Add(link);
                }
            }
        }

        return result;
    }
}
