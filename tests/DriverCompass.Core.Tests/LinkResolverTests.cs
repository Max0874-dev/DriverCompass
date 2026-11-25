using DriverCompass.Core.Abstractions;
using DriverCompass.Core.Evaluation;
using DriverCompass.Core.Links;
using DriverCompass.Core.Models;

namespace DriverCompass.Core.Tests;

public class LinkResolverTests
{
    private readonly IVendorCatalog _catalog = TestCatalog.Create();
    private readonly LinkResolver _resolver = LinkResolver.CreateDefault();
    private readonly DeviceStatusEvaluator _evaluator = new();

    private DeviceAssessment Assess(string hwid, string? cls = null) =>
        _evaluator.Evaluate(new DeviceInfo { InstanceId = hwid + @"\1", HardwareIds = [hwid], DeviceClass = cls, ConfigManagerErrorCode = 28 }, _catalog);

    private IReadOnlyList<DriverLink> Resolve(DeviceAssessment a, SystemInfo s, bool useSerial = true) =>
        _resolver.Resolve(a, new LinkContext(s, _catalog, new AnalysisOptions { UseSerialNumberInLinks = useSerial }));

    [Fact]
    public void Order_is_oem_then_chip_vendor_then_microsoft_catalog()
    {
        var links = Resolve(Assess(@"PCI\VEN_10DE&DEV_2489", "Display"), TestCatalog.Dell());

        Assert.Equal(
            [DriverLinkKind.OemSupport, DriverLinkKind.OemSupport, DriverLinkKind.ChipVendor, DriverLinkKind.MicrosoftCatalog],
            links.Select(l => l.Kind));
    }

    [Fact]
    public void Dell_link_uses_service_tag_and_offers_generic_fallback()
    {
        var links = Resolve(Assess(@"PCI\VEN_10DE&DEV_2489"), TestCatalog.Dell()).Where(l => l.Kind == DriverLinkKind.OemSupport).ToList();

        Assert.Equal("https://www.dell.com/support/home/product-support/servicetag/ABC1234/drivers", links[0].Uri.AbsoluteUri);
        Assert.Equal(LinkSpecificity.Serial, links[0].Specificity);
        Assert.Equal("https://www.dell.com/support/home/drivers", links[1].Uri.AbsoluteUri);
        Assert.Equal(LinkSpecificity.Generic, links[1].Specificity);
    }

    [Fact]
    public void Oem_links_carry_the_vendor_list_id()
    {
        var links = Resolve(Assess(@"PCI\VEN_10DE&DEV_2489"), TestCatalog.Dell());

        Assert.All(links.Where(l => l.Kind == DriverLinkKind.OemSupport), l => Assert.Equal("dell", l.OemId));
        Assert.All(links.Where(l => l.Kind != DriverLinkKind.OemSupport), l => Assert.Null(l.OemId));
    }

    [Fact]
    public void Serial_is_not_used_when_disabled_in_settings()
    {
        var oem = Resolve(Assess(@"PCI\VEN_10DE&DEV_2489"), TestCatalog.Dell(), useSerial: false)
            .First(l => l.Kind == DriverLinkKind.OemSupport);

        Assert.Equal(LinkSpecificity.Model, oem.Specificity);
        Assert.Equal("https://www.dell.com/support/search?q=Latitude%207440", oem.Uri.AbsoluteUri);
        Assert.DoesNotContain("ABC1234", oem.Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public void Placeholder_serial_is_ignored()
    {
        var oem = Resolve(Assess(@"PCI\VEN_10DE&DEV_2489"), TestCatalog.Dell(serial: "To Be Filled By O.E.M."))
            .First(l => l.Kind == DriverLinkKind.OemSupport);
        Assert.Equal(LinkSpecificity.Model, oem.Specificity);
    }

    [Fact]
    public void White_box_pc_falls_back_to_mainboard_vendor()
    {
        var oem = Resolve(Assess(@"PCI\VEN_10EC&DEV_8125"), TestCatalog.WhiteBoxAsus()).First(l => l.Kind == DriverLinkKind.OemSupport);

        Assert.Equal("ASUS", oem.SourceName);
        Assert.Equal("https://www.asus.com/search?q=ROG%20STRIX%20B550-F%20GAMING", oem.Uri.AbsoluteUri);
    }

    [Fact]
    public void Mainboard_vendor_link_is_skipped_for_usb_peripherals()
    {
        var links = Resolve(Assess(@"USB\VID_0BDA&PID_8153"), TestCatalog.WhiteBoxAsus());
        Assert.DoesNotContain(links, l => l.Kind == DriverLinkKind.OemSupport);
        Assert.Contains(links, l => l.Kind == DriverLinkKind.ChipVendor);
    }

    [Fact]
    public void Unknown_oem_gives_no_oem_link()
    {
        var links = Resolve(Assess(@"PCI\VEN_10DE&DEV_2489"), new SystemInfo { Manufacturer = "Acme PCs GmbH" });
        Assert.DoesNotContain(links, l => l.Kind == DriverLinkKind.OemSupport);
    }

    [Fact]
    public void Class_specific_chip_page_comes_first()
    {
        var chip = Resolve(Assess(@"HDAUDIO\FUNC_01&VEN_10EC&DEV_1220", "MEDIA"), SystemInfo.Empty)
            .Where(l => l.Kind == DriverLinkKind.ChipVendor).ToList();

        Assert.Equal("https://www.realtek.com/audio", chip[0].Uri.AbsoluteUri);
        Assert.Equal("https://www.realtek.com/Download/Overview", chip[1].Uri.AbsoluteUri);
    }

    [Fact]
    public void Class_specific_page_is_not_used_for_other_classes()
    {
        var chip = Resolve(Assess(@"PCI\VEN_10EC&DEV_8125", "Net"), SystemInfo.Empty).Where(l => l.Kind == DriverLinkKind.ChipVendor).ToList();
        Assert.Single(chip);
        Assert.Equal("https://www.realtek.com/Download/Overview", chip[0].Uri.AbsoluteUri);
    }

    [Fact]
    public void Chip_page_outside_allowed_domains_is_dropped()
    {
        var links = Resolve(Assess(@"USB\VID_DEAD&PID_BEEF"), SystemInfo.Empty);
        Assert.DoesNotContain(links, l => l.Kind == DriverLinkKind.ChipVendor);
    }

    [Fact]
    public void Microsoft_catalog_link_encodes_hardware_id()
    {
        var link = Resolve(Assess(@"PCI\VEN_10EC&DEV_8125&SUBSYS_86791043&REV_05"), SystemInfo.Empty).Single(l => l.Kind == DriverLinkKind.MicrosoftCatalog);

        Assert.Equal("www.catalog.update.microsoft.com", link.Uri.Host);
        Assert.Equal("https", link.Uri.Scheme);
        Assert.Contains("PCI%5CVEN_10EC%26DEV_8125%26SUBSYS_86791043%26REV_05", link.Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public void Device_without_hardware_ids_has_no_links()
    {
        var a = _evaluator.Evaluate(new DeviceInfo { InstanceId = @"ROOT\X\0" }, _catalog);
        Assert.Empty(Resolve(a, SystemInfo.Empty));
    }

    [Fact]
    public void Duplicate_urls_are_removed()
    {
        var resolver = new LinkResolver([new MicrosoftCatalogLinkSource(), new MicrosoftCatalogLinkSource()]);
        var links = resolver.Resolve(Assess(@"PCI\VEN_10DE&DEV_2489"), new LinkContext(SystemInfo.Empty, _catalog, AnalysisOptions.Default));
        Assert.Single(links);
    }
}

public class UrlSafetyTests
{
    private static readonly string[] Domains = ["dell.com"];

    [Theory]
    [InlineData("https://www.dell.com/x", true)]
    [InlineData("https://dell.com/x", true)]
    [InlineData("http://www.dell.com/x", false)]
    [InlineData("https://dell.com.evil.example/x", false)]
    [InlineData("https://evildell.com/x", false)]
    [InlineData("https://user:pw@www.dell.com/x", false)]
    [InlineData("https://www.dell.com:8443/x", false)]
    public void Allowed_https(string url, bool expected) =>
        Assert.Equal(expected, UrlSafety.IsAllowedHttps(new Uri(url), Domains));

    [Fact]
    public void Expand_encodes_values_so_they_cannot_change_the_path()
    {
        var uri = UrlSafety.Expand("https://www.dell.com/s/{model}", new Dictionary<string, string?> { ["model"] = "../../evil?x=1#y" }, Domains);
        Assert.NotNull(uri);
        Assert.Equal("https://www.dell.com/s/..%2F..%2Fevil%3Fx%3D1%23y", uri.AbsoluteUri);
    }

    [Fact]
    public void Expand_returns_null_for_missing_values() =>
        Assert.Null(UrlSafety.Expand("https://www.dell.com/s/{serial}", new Dictionary<string, string?>(), Domains));
}
