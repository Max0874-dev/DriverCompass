using DriverCompass.Core.Models;
using DriverCompass.Core.Reporting;

namespace DriverCompass.Core.Tests;

public class DriverGuideTests
{
    private static readonly SystemInfo Hp = new() { Manufacturer = "HP", Model = "HP EliteBook 840 G8 Notebook PC", OperatingSystemVersion = "10.0.26100" };
    private static readonly Uri HpGeneral = new("https://support.hp.com/drivers");

    private static DeviceAssessment Device(DeviceStatus status, IReadOnlyList<DriverLink>? links = null, VersionCheckResult? check = null, params string[] hardwareIds) => new()
    {
        Device = new DeviceInfo { InstanceId = @"PCI\X\1", Name = "Wi-Fi", DeviceClass = "Net", HardwareIds = hardwareIds },
        Status = status,
        Reason = status == DeviceStatus.GenericDriver ? StatusReason.GenericDriver : StatusReason.NoDriverInstalled,
        Links = links ?? [],
        VersionCheck = check,
    };

    private static List<string> Texts(DriverGuideContent guide) => guide.Steps.Select(s => s.Text).ToList();

    [Fact]
    public void WorkingDevice_WithoutNewerVersion_HasNoGuide() =>
        Assert.Null(DriverGuide.Build(Device(DeviceStatus.Ok), Hp, canInstallHere: true, KeyTextProvider.Instance));

    [Fact]
    public void WindowsUpdate_OpensOptionalUpdates()
    {
        var check = new VersionCheckResult(VersionSourceIds.WindowsUpdate, VersionCheckState.NewerAvailable, "1.2", PackageName: "Intel - Net - 1.2") { PackageId = "guid" };
        var guide = DriverGuide.Build(Device(DeviceStatus.Ok, check: check), Hp, canInstallHere: false, KeyTextProvider.Instance)!;

        Assert.Equal(["Guide_Wu_Open", "Guide_Wu_Select", "Guide_Wu_Install", "Guide_Restart"], Texts(guide));
        Assert.Equal(GuideAction.OpenWindowsUpdate, guide.Steps[0].Action);
        Assert.Null(guide.Shortcut);
    }

    [Fact]
    public void InstallHere_OnlyWhenTheEditionCanInstall()
    {
        var check = new VersionCheckResult(VersionSourceIds.WindowsUpdate, VersionCheckState.NewerAvailable, "1.2") { PackageId = "guid" };

        var github = DriverGuide.Build(Device(DeviceStatus.Ok, check: check), Hp, canInstallHere: true, KeyTextProvider.Instance)!;
        Assert.Equal(GuideAction.Install, github.Shortcut!.Action);

        var store = DriverGuide.Build(Device(DeviceStatus.Ok, check: check), Hp, canInstallHere: false, KeyTextProvider.Instance)!;
        Assert.Null(store.Shortcut);
        Assert.DoesNotContain(store.Steps.Concat(store.Hints), s => s.Action == GuideAction.Install);
    }

    [Fact]
    public void HpPackage_PackagePage_SoftPaq_AndHpTool()
    {
        var check = new VersionCheckResult(VersionSourceIds.Hp, VersionCheckState.NewerAvailable, "30.0.1", SourceUri: new Uri("https://ftp.hp.com/pub/softpaq/sp1/sp149356.html"), PackageName: "Intel WLAN Driver") { PackageId = "sp149356" };
        var guide = DriverGuide.Build(Device(DeviceStatus.Ok, check: check), Hp, canInstallHere: false, KeyTextProvider.Instance)!;

        Assert.Equal(["Guide_Oem_OpenPackage", "Guide_Oem_DownloadVersion", "Guide_Oem_SoftPaq", "Guide_Oem_Run", "Guide_Restart"], Texts(guide));
        Assert.Equal(GuideAction.OpenVersionSource, guide.Steps[0].Action);
        Assert.Equal("Intel WLAN Driver", guide.Steps[1].Value);
        Assert.Equal("sp149356", guide.Steps[2].Value);
        Assert.Equal("Guide_Tool_hp", Assert.Single(guide.Hints).Text);
    }

    [Fact]
    public void LenovoPackage_WithoutPackagePage_OpensModelPage()
    {
        var link = new DriverLink(DriverLinkKind.OemSupport, new Uri("https://pcsupport.lenovo.com/products/PF123"), LinkSpecificity.Serial, "Lenovo", "lenovo");
        var check = new VersionCheckResult(VersionSourceIds.Lenovo, VersionCheckState.NewerAvailable, PackageName: "Intel WLAN") { PackageId = "n3xwl01w" };
        var guide = DriverGuide.Build(Device(DeviceStatus.Ok, [link], check), Hp, canInstallHere: false, KeyTextProvider.Instance)!;

        Assert.Equal(["Guide_Oem_OpenModel", "Guide_Oem_Download", "Guide_Oem_Run", "Guide_Restart"], Texts(guide));
        Assert.Equal(GuideAction.OpenPrimaryLink, guide.Steps[0].Action);
        Assert.Equal("Guide_Tool_lenovo", Assert.Single(guide.Hints).Text);
    }

    [Fact]
    public void MissingDriver_MakerPage_WithModelOsAndCategory()
    {
        var link = new DriverLink(DriverLinkKind.OemSupport, HpGeneral, LinkSpecificity.Generic, "HP", "hp");
        var guide = DriverGuide.Build(Device(DeviceStatus.MissingDriver, [link]), Hp, canInstallHere: true, KeyTextProvider.Instance)!;

        Assert.Equal(["Guide_Page_Open", "Guide_Page_Model", "Guide_Page_Os", "Guide_Page_Category", "Guide_Page_Run", "Guide_Restart"], Texts(guide));
        Assert.Equal(GuideAction.OpenPrimaryLink, guide.Steps[0].Action);
        Assert.Equal("HP EliteBook 840 G8 Notebook PC", guide.Steps[1].Value);
        Assert.Equal(["Guide_Hint_Inf", "Guide_Tool_hp", "Guide_Hint_WindowsUpdate"], guide.Hints.Select(h => h.Text));
        Assert.Null(guide.Intro);
    }

    [Fact]
    public void GenericDriver_ChipVendorPage_HasIntroAndProductStep()
    {
        var link = new DriverLink(DriverLinkKind.ChipVendor, new Uri("https://www.intel.com/content/www/us/en/download-center/home.html"), LinkSpecificity.Generic, "Intel");
        var guide = DriverGuide.Build(Device(DeviceStatus.GenericDriver, [link]), SystemInfo.Empty, canInstallHere: false, KeyTextProvider.Instance)!;

        Assert.Equal("Guide_Intro_Generic", guide.Intro);
        Assert.Equal("Guide_Page_Product", guide.Steps[1].Text);
        Assert.DoesNotContain(guide.Hints, h => h.Text.StartsWith("Guide_Tool_", StringComparison.Ordinal));
    }

    [Fact]
    public void OnlyCatalog_CopySearchAndDeviceManager()
    {
        var link = new DriverLink(DriverLinkKind.MicrosoftCatalog, new Uri("https://www.catalog.update.microsoft.com/Search.aspx?q=x"), LinkSpecificity.HardwareId, "Microsoft Update Catalog");
        var guide = DriverGuide.Build(Device(DeviceStatus.Error, [link], null, @"PCI\VEN_8086&DEV_2725"), Hp, canInstallHere: false, KeyTextProvider.Instance)!;

        Assert.Equal(["Guide_Cat_Copy", "Guide_Cat_Open", "Guide_Cat_Pick", "Guide_Cat_Install", "Guide_Restart"], Texts(guide));
        Assert.Equal(@"PCI\VEN_8086&DEV_2725", guide.Steps[1].Value);
        Assert.Equal(GuideAction.OpenCatalogSearch, guide.Steps[1].Action);
    }

    [Fact]
    public void NoSourceAtAll_PointsToWindowsUpdateAndMaker()
    {
        var guide = DriverGuide.Build(Device(DeviceStatus.MissingDriver), Hp, canInstallHere: false, KeyTextProvider.Instance)!;
        Assert.Equal(["Guide_None_WindowsUpdate", "Guide_None_Maker"], Texts(guide));
    }

    [Fact]
    public void WindowsName_NamesGeneration()
    {
        Assert.StartsWith("Windows 11 (", DriverGuide.WindowsName(Hp), StringComparison.Ordinal);
        Assert.StartsWith("Windows 10 (", DriverGuide.WindowsName(new SystemInfo { OperatingSystemVersion = "10.0.19045" }), StringComparison.Ordinal);
    }
}
