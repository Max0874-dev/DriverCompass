using System.Runtime.InteropServices;
using DriverCompass.Core.Models;
using DriverCompass.VersionProviders.Dell;
using DriverCompass.VersionProviders.Hp;
using DriverCompass.VersionProviders.Lenovo;
using DriverCompass.VersionProviders.WindowsUpdate;

namespace DriverCompass.VersionProviders.Tests;

public class DellTests
{
    private static readonly SystemInfo Dell = new() { Manufacturer = "Dell Inc.", Model = "Latitude 7440", SystemSku = "0c08" };

    [Fact]
    public void Index_KeepsValidModels_SkipsUnsafePaths()
    {
        using var stream = File.OpenRead(Fixtures.PathOf("dell-index.xml"));
        var index = DellCatalogParser.ParseIndex(stream);

        var model = Assert.Single(index);
        Assert.Equal(["0C08"], model.SystemIds);
        Assert.Equal("FOLDER15243183M/1/Latitude_0C08.cab", model.Path);
        Assert.Equal(new string('A', 64), model.Sha256);
    }

    [Fact]
    public void ModelCatalog_BuildsIdsFromPciInfo_FiltersArchitectureAndNonDrivers()
    {
        using var stream = File.OpenRead(Fixtures.PathOf("dell-model.xml"));
        var entries = DellCatalogParser.ParseModelCatalog(stream, Architecture.X64);

        Assert.Equal(2, entries.Count);
        var wifi = entries[0];
        Assert.Equal([@"PCI\VEN_8086&DEV_2725&SUBSYS_00248086", @"PCI\VEN_8086&DEV_51F0"], wifi.HardwareIds);
        Assert.Equal("23.130.0.4", wifi.Version);
        Assert.Equal(new DateOnly(2025, 3, 12), wifi.Date);
        Assert.Equal("www.dell.com", wifi.SourceUri!.Host);

        // ImportantInfo outside dell.com is dropped.
        Assert.Null(entries[1].SourceUri);
    }

    [Fact]
    public async Task Provider_FindsNewerDriver_AndChecksHashAndSignature()
    {
        var model = Fixtures.PathOf("dell-model.xml");
        var downloader = new FakeDownloader(new()
        {
            ["https://downloads.dell.com/catalog/CatalogIndexPC.cab"] = Fixtures.PathOf("dell-index.xml"),
            ["https://downloads.dell.com/FOLDER15243183M/1/Latitude_0C08.cab"] = model,
        });
        var extractor = new FakeExtractor();
        var wifi = Fixtures.Device(@"PCI\WIFI\1", [@"PCI\VEN_8086&DEV_2725&SUBSYS_00248086&REV_1A", @"PCI\VEN_8086&DEV_2725&SUBSYS_00248086"], "23.60.1.2");
        var other = Fixtures.Device(@"PCI\OTHER\1", [@"PCI\VEN_10EC&DEV_8125"], "1.0");

        var result = await new DellVersionProvider(downloader, extractor).CheckAsync([wifi, other], Dell, TestContext.Current.CancellationToken);

        Assert.Equal(VersionSourceOutcome.Ok, result.Outcome);
        Assert.Equal("2026.11.15", result.Detail);
        var check = Assert.Single(result.Results).Value;
        Assert.Equal(VersionCheckState.NewerAvailable, check.State);
        Assert.Equal("23.130.0.4", check.LatestVersion);
        Assert.Equal("Intel AX211/AX210 WiFi Driver", check.PackageName);

        Assert.Equal(new string('A', 64), downloader.Requests[1].ExpectedSha256);
        Assert.All(extractor.RequiredSigners, s => Assert.Contains("Dell Technologies Inc.", s));
    }

    [Fact]
    public async Task Provider_UnknownModel_IsNotApplicable()
    {
        var downloader = new FakeDownloader(new() { ["https://downloads.dell.com/catalog/CatalogIndexPC.cab"] = Fixtures.PathOf("dell-index.xml") });
        var result = await new DellVersionProvider(downloader, new FakeExtractor())
            .CheckAsync([], Dell with { SystemSku = "0FFF" }, TestContext.Current.CancellationToken);

        Assert.Equal(VersionSourceOutcome.NotApplicable, result.Outcome);
        Assert.Single(downloader.Requests);
    }

    [Fact]
    public async Task Provider_WithoutSystemId_DoesNotDownloadAnything()
    {
        var downloader = new FakeDownloader([]);
        var result = await new DellVersionProvider(downloader, new FakeExtractor())
            .CheckAsync([], Dell with { SystemSku = "Latitude" }, TestContext.Current.CancellationToken);

        Assert.Equal(VersionSourceOutcome.NotApplicable, result.Outcome);
        Assert.Empty(downloader.Requests);
    }

    [Fact]
    public void Applicability()
    {
        var provider = new DellVersionProvider(new FakeDownloader([]), new FakeExtractor());
        Assert.True(provider.IsApplicable(Dell));
        Assert.False(provider.IsApplicable(new SystemInfo { Manufacturer = "LENOVO" }));
        Assert.False(provider.IsApplicable(SystemInfo.Empty));
    }
}

public sealed class LenovoTests : IDisposable
{
    private static readonly SystemInfo Lenovo = new() { Manufacturer = "LENOVO", Model = "20xwcto1ww", OperatingSystemVersion = "10.0.26100" };
    private readonly string _catalog;

    public LenovoTests()
    {
        // The catalog must carry the real SHA-256 of the descriptor fixture.
        _catalog = Path.Combine(Path.GetTempPath(), "dc-lenovo-" + Guid.NewGuid().ToString("N") + ".xml");
        File.WriteAllText(_catalog, File.ReadAllText(Fixtures.PathOf("lenovo-catalog.xml"))
            .Replace("{{SHA_SERIALIO}}", Fixtures.Sha256Of(Fixtures.PathOf("lenovo-descriptor.xml")), StringComparison.Ordinal));
    }

    public void Dispose() => File.Delete(_catalog);

    [Fact]
    public void Catalog_SkipsEntriesWithoutSha256()
    {
        using var stream = File.OpenRead(_catalog);
        var packages = LenovoCatalogParser.ParseCatalog(stream);

        Assert.Equal(2, packages.Count);
        Assert.Equal("download.lenovo.com", packages[0].Location.Host);
    }

    [Fact]
    public void Descriptor_ReadsDetectInstallDriver()
    {
        using var stream = File.OpenRead(Fixtures.PathOf("lenovo-descriptor.xml"));
        var entry = Assert.Single(LenovoCatalogParser.ParseDescriptor(stream));

        Assert.Equal([@"ACPI\INT34C5", @"PCI\VEN_8086&DEV_A0E8"], entry.HardwareIds);
        Assert.Equal("30.100.2129.8^", entry.Version);
        Assert.Equal("Intel Serial IO Driver", entry.PackageName);
    }

    [Fact]
    public async Task Provider_UsesMachineType_SkipsForeignHosts_FallsBackToWin10()
    {
        var downloader = new FakeDownloader(new()
        {
            ["https://download.lenovo.com/catalog/20XW_Win10.xml"] = _catalog,
            ["https://download.lenovo.com/pccbbs/mobiles/n32li04w_2_.xml"] = Fixtures.PathOf("lenovo-descriptor.xml"),
        });
        var serialIo = Fixtures.Device(@"PCI\SIO\1", [@"PCI\VEN_8086&DEV_A0E8&SUBSYS_22AA17AA&REV_20"], "30.100.2000.1",
            compatibleIds: [@"PCI\VEN_8086&DEV_A0E8", @"PCI\CC_0C80"]);

        var result = await new LenovoVersionProvider(downloader).CheckAsync([serialIo], Lenovo, TestContext.Current.CancellationToken);

        Assert.Equal("https://download.lenovo.com/catalog/20XW_Win11.xml", downloader.Requests[0].Uri.AbsoluteUri);
        Assert.Equal("https://download.lenovo.com/catalog/20XW_Win10.xml", downloader.Requests[1].Uri.AbsoluteUri);
        Assert.DoesNotContain(downloader.Requests, r => r.Uri.Host != "download.lenovo.com");
        Assert.NotNull(downloader.Requests[2].ExpectedSha256);

        var check = Assert.Single(result.Results).Value;
        Assert.Equal(VersionCheckState.NewerAvailable, check.State);
        Assert.Equal("30.100.2129.8", check.LatestVersion);
    }

    [Fact]
    public async Task Provider_NoCatalogForMachineType_IsNotApplicable()
    {
        var result = await new LenovoVersionProvider(new FakeDownloader([])).CheckAsync([], Lenovo, TestContext.Current.CancellationToken);
        Assert.Equal(VersionSourceOutcome.NotApplicable, result.Outcome);
    }

    [Theory]
    [InlineData("20XWCTO1WW", "20XW")]
    [InlineData("81ym", "81YM")]
    [InlineData("ThinkPad", "THIN")]
    [InlineData("X1", null)]
    [InlineData("2-XW", null)]
    public void MachineType(string model, string? expected) =>
        Assert.Equal(expected, LenovoVersionProvider.MachineType(new SystemInfo { Model = model }));
}

public class HpTests
{
    private static readonly SystemInfo Hp = new()
    {
        Manufacturer = "HP",
        BaseBoardProduct = "8b41",
        OperatingSystemVersion = "10.0.26100",
        OperatingSystemDisplayVersion = "24H2",
    };

    [Fact]
    public void PlatformList_AndOsChoice()
    {
        using var stream = File.OpenRead(Fixtures.PathOf("hp-platformlist.xml"));
        var releases = HpCatalogParser.ParsePlatformList(stream, "8b41");

        Assert.Equal(3, releases.Count);
        Assert.Equal("24H2", HpCatalogParser.ChooseOs(releases, true, "24H2", 26100)!.ReleaseIdFilename);
        Assert.Equal("24H2", HpCatalogParser.ChooseOs(releases, true, "25H2", 26200)!.ReleaseIdFilename);
        Assert.Equal("23H2", HpCatalogParser.ChooseOs(releases, true, "23H2", 22631)!.ReleaseIdFilename);
        Assert.Equal("21H2", HpCatalogParser.ChooseOs(releases, false, "22H2", 19045)!.ReleaseIdFilename);
        Assert.Null(HpCatalogParser.ChooseOs([], true, "24H2", 26100));
    }

    [Fact]
    public void UnsafeReleaseNames_AreDropped()
    {
        using var stream = File.OpenRead(Fixtures.PathOf("hp-platformlist.xml"));
        Assert.Empty(HpCatalogParser.ParsePlatformList(stream, "0000"));
    }

    [Fact]
    public void ReferenceUri_IsLowerCase() =>
        Assert.Equal(
            "https://hpia.hpcloud.hp.com/ref/8b41/8b41_64_11.0.24h2.cab",
            HpCatalogParser.ReferenceUri("8B41", new HpPlatformOs(true, "24H2", 26100)).AbsoluteUri);

    [Fact]
    public void Reference_OnlyDevicesWithSoftPaq_SafeNotesUrl()
    {
        using var stream = File.OpenRead(Fixtures.PathOf("hp-reference.xml"));
        var entries = HpCatalogParser.ParseReference(stream);

        Assert.Equal(2, entries.Count);
        Assert.Equal([@"PCI\VEN_1022&DEV_15E2&REV_60"], entries[0].HardwareIds);
        Assert.Equal("6.0.2.114", entries[0].Version);
        Assert.Equal("AMD Audio Driver", entries[0].PackageName);
        Assert.Equal("https://ftp.hp.com/pub/softpaq/sp174001-174500/sp174175.html", entries[0].SourceUri!.AbsoluteUri);

        // Instance-path DeviceId is not used, HardwareId is; foreign notes URL is dropped.
        Assert.Equal([@"HDAUDIO\FUNC_01&VEN_10EC&DEV_0236&SUBSYS_103C8B41"], entries[1].HardwareIds);
        Assert.Null(entries[1].SourceUri);
    }

    [Fact]
    public async Task Provider_EndToEnd()
    {
        var downloader = new FakeDownloader(new()
        {
            ["https://hpia.hpcloud.hp.com/ref/platformList.cab"] = Fixtures.PathOf("hp-platformlist.xml"),
            ["https://hpia.hpcloud.hp.com/ref/8b41/8b41_64_11.0.24h2.cab"] = Fixtures.PathOf("hp-reference.xml"),
        });
        var extractor = new FakeExtractor();
        var audio = Fixtures.Device(@"PCI\AUDIO\1", [@"PCI\VEN_1022&DEV_15E2&SUBSYS_8B41103C&REV_60", @"PCI\VEN_1022&DEV_15E2&REV_60"], "6.0.2.114");

        var result = await new HpVersionProvider(downloader, extractor).CheckAsync([audio], Hp, TestContext.Current.CancellationToken);

        Assert.Equal("8B41 24H2", result.Detail);
        Assert.Equal(VersionCheckState.UpToDate, Assert.Single(result.Results).Value.State);
        Assert.All(extractor.RequiredSigners, s => Assert.Equal(["HP Inc."], s));
    }

    [Fact]
    public async Task Provider_MissingReferenceFile_IsNotApplicable()
    {
        var downloader = new FakeDownloader(new() { ["https://hpia.hpcloud.hp.com/ref/platformList.cab"] = Fixtures.PathOf("hp-platformlist.xml") });
        var result = await new HpVersionProvider(downloader, new FakeExtractor()).CheckAsync([], Hp, TestContext.Current.CancellationToken);
        Assert.Equal(VersionSourceOutcome.NotApplicable, result.Outcome);
    }

    [Theory]
    [InlineData("HP", true)]
    [InlineData("Hewlett-Packard", true)]
    [InlineData("Micro-Star International Co., Ltd.", false)]
    public void Applicability(string manufacturer, bool expected) =>
        Assert.Equal(expected, new HpVersionProvider(new FakeDownloader([]), new FakeExtractor()).IsApplicable(new SystemInfo { Manufacturer = manufacturer }));
}

public class WindowsUpdateTests
{
    private sealed class FakeSearcher(params WindowsUpdateDriver[] drivers) : IWindowsUpdateSearcher
    {
        public Task<IReadOnlyList<WindowsUpdateDriver>> SearchDriversAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WindowsUpdateDriver>>(drivers);
    }

    [Fact]
    public async Task OfferedDriver_IsNewer_OthersHaveNothingNewer_MissingDriversStayUnknown()
    {
        var bt = Fixtures.Device(@"USB\BT\1", [@"USB\VID_8087&PID_0029&REV_0001", @"USB\VID_8087&PID_0029"], "22.240.0.2");
        var nvme = Fixtures.Device(@"PCI\NVME\1", [@"PCI\VEN_144D&DEV_A80A"], "10.0.26100.1");
        var missing = Fixtures.Device(@"PCI\NIC\1", [@"PCI\VEN_10EC&DEV_8125"], null);
        var searcher = new FakeSearcher(
            new WindowsUpdateDriver("Intel Corporation - Bluetooth - 23.150.0.3", @"usb\vid_8087&pid_0029", new DateOnly(2025, 9, 18), "Intel", "Intel Wireless Bluetooth"),
            new WindowsUpdateDriver("Some other driver", @"PCI\VEN_FFFF&DEV_0001", new DateOnly(2025, 1, 1), "X", "Y"));

        var result = await new WindowsUpdateVersionProvider(searcher).CheckAsync([bt, nvme, missing], SystemInfo.Empty, TestContext.Current.CancellationToken);

        Assert.Equal(VersionCheckState.NewerAvailable, result.Results[bt.Device.InstanceId].State);
        Assert.Equal("23.150.0.3", result.Results[bt.Device.InstanceId].LatestVersion);
        Assert.Equal(new DateOnly(2025, 9, 18), result.Results[bt.Device.InstanceId].LatestDate);
        Assert.Equal(VersionCheckState.UpToDate, result.Results[nvme.Device.InstanceId].State);
        Assert.False(result.Results.ContainsKey(missing.Device.InstanceId));
        Assert.Equal("2", result.Detail);
    }

    [Fact]
    public async Task OfferForDeviceWithoutDriver_IsNewer()
    {
        var missing = Fixtures.Device(@"PCI\NIC\1", [@"PCI\VEN_10EC&DEV_8125&SUBSYS_86791043&REV_05"], null);
        var searcher = new FakeSearcher(new WindowsUpdateDriver("Realtek - Net - 10.68.815.2023", @"PCI\VEN_10EC&DEV_8125&SUBSYS_86791043&REV_05", new DateOnly(2023, 8, 15), "Realtek", "Realtek Gaming 2.5GbE"));

        var result = await new WindowsUpdateVersionProvider(searcher).CheckAsync([missing], SystemInfo.Empty, TestContext.Current.CancellationToken);

        Assert.Equal(VersionCheckState.NewerAvailable, result.Results[missing.Device.InstanceId].State);
    }

    [Theory]
    [InlineData("Intel Corporation - Bluetooth - 23.150.0.3", "23.150.0.3")]
    [InlineData("Realtek Semiconductor Corp. - Extension - 6/1/2024 12:00:00 AM - 10.0.1.5", "10.0.1.5")]
    [InlineData("NVIDIA - Display - 32.0.15.6094", "32.0.15.6094")]
    [InlineData("Intel - System - 2023", null)]
    [InlineData("Something - 1.0 beta", null)]
    [InlineData(null, null)]
    public void VersionFromTitle(string? title, string? expected) =>
        Assert.Equal(expected, WindowsUpdateVersionProvider.VersionFromTitle(title));
}

public class OemPackageCatalogTests
{
    [Fact]
    public void Dell_FindPackage_ReadsPathHashAndDevices_OnlySafePaths()
    {
        const string path = "FOLDER123M/1/Intel-WLAN_ABC12_WIN64_23.130.0.4_A01.EXE";
        using (var stream = File.OpenRead(Fixtures.PathOf("dell-model.xml")))
        {
            var package = DellCatalogParser.FindPackage(stream, path, System.Runtime.InteropServices.Architecture.X64);

            Assert.NotNull(package);
            Assert.Equal("https://downloads.dell.com/" + path, package.DownloadUri.AbsoluteUri);
            Assert.Equal(new string('A', 0) + "ABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCD", package.Sha256);
            Assert.Equal(52428800, package.Size);
            Assert.Equal("23.130.0.4", package.Version);
            Assert.Contains(@"PCI\VEN_8086&DEV_2725&SUBSYS_00248086", package.HardwareIds);
            Assert.Equal(["downloads.dell.com"], package.AllowedHosts);
        }

        using (var stream = File.OpenRead(Fixtures.PathOf("dell-model.xml")))
        {
            Assert.Null(DellCatalogParser.FindPackage(stream, "../../evil.EXE", System.Runtime.InteropServices.Architecture.X64));
        }

        using (var stream = File.OpenRead(Fixtures.PathOf("dell-model.xml")))
        {
            // Stage 2 marks only the safe, hashed component as installable.
            var entries = DellCatalogParser.ParseModelCatalog(stream, System.Runtime.InteropServices.Architecture.X64);
            Assert.Equal(path, entries[0].PackageId);
            Assert.Null(entries[1].PackageId);
        }
    }

    [Fact]
    public void Hp_FindPackage_RequiresFtpHpComHttpsAndHash()
    {
        using (var stream = File.OpenRead(Fixtures.PathOf("hp-reference.xml")))
        {
            var package = HpCatalogParser.FindPackage(stream, "sp174175");
            Assert.NotNull(package);
            Assert.Equal("https://ftp.hp.com/pub/softpaq/sp174001-174500/sp174175.exe", package.DownloadUri.AbsoluteUri);
            Assert.Equal(9000000, package.Size);
            Assert.Equal([@"PCI\VEN_1022&DEV_15E2&REV_60"], package.HardwareIds);
            Assert.Equal(["HP Inc."], package.Signers);
        }

        using (var stream = File.OpenRead(Fixtures.PathOf("hp-reference.xml")))
        {
            Assert.Null(HpCatalogParser.FindPackage(stream, "sp170000"));
        }

        using (var stream = File.OpenRead(Fixtures.PathOf("hp-reference.xml")))
        {
            var entries = HpCatalogParser.ParseReference(stream);
            Assert.Equal("sp174175", entries[0].PackageId);
            Assert.Null(entries[1].PackageId);
        }
    }
}
