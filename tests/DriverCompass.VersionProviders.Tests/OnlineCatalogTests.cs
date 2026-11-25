using DriverCompass.Core.Models;
using DriverCompass.VersionProviders.Dell;
using DriverCompass.VersionProviders.Hp;
using DriverCompass.VersionProviders.Lenovo;
using DriverCompass.VersionProviders.Net;

namespace DriverCompass.VersionProviders.Tests;

/// <summary>
/// Checks the real vendor catalogs with public example models (not the PC running the test).
/// Off by default and in CI; run with the environment variable DRIVERCOMPASS_ONLINE_TESTS=1.
/// </summary>
public sealed class OnlineCatalogTests : IDisposable
{
    private readonly string _cache = Path.Combine(Path.GetTempPath(), "dc-online-" + Guid.NewGuid().ToString("N"));
    private readonly HttpClient _http = VersionProviderFactory.CreateHttpClient();

    public void Dispose()
    {
        _http.Dispose();
        if (Directory.Exists(_cache))
        {
            Directory.Delete(_cache, true);
        }
    }

    private static void RequireOnline() =>
        Assert.SkipUnless(Environment.GetEnvironmentVariable("DRIVERCOMPASS_ONLINE_TESTS") == "1", "Set DRIVERCOMPASS_ONLINE_TESTS=1 to run against the real catalogs.");

    private CatalogDownloader Downloader => new(_http, _cache);

    private static void AssertNewer(VersionProviderResult result, string instanceId)
    {
        Assert.Equal(VersionSourceOutcome.Ok, result.Outcome);
        Assert.Equal(VersionCheckState.NewerAvailable, result.Results[instanceId].State);
        Assert.NotNull(result.Results[instanceId].LatestVersion ?? result.Results[instanceId].LatestDate?.ToString());
    }

    [Fact]
    public async Task Dell_Latitude5440()
    {
        RequireOnline();
        var device = Fixtures.Device("USB\\REALTEK\\1", [@"USB\VID_0BDA&PID_4001&REV_0001&MI_00"], "1.0.0.0", new DateOnly(2015, 1, 1));
        var system = new SystemInfo { Manufacturer = "Dell Inc.", SystemSku = "0C00", OperatingSystemVersion = "10.0.26100" };

        var result = await new DellVersionProvider(Downloader, new ExpandCabExtractor()).CheckAsync([device], system, TestContext.Current.CancellationToken);

        AssertNewer(result, device.Device.InstanceId);
    }

    [Fact]
    public async Task Lenovo_20XW()
    {
        RequireOnline();
        var device = Fixtures.Device("PCI\\SIO\\1", [@"PCI\VEN_8086&DEV_A0E8&SUBSYS_22AA17AA&REV_20"], "1.0.0.0", new DateOnly(2015, 1, 1),
            [@"PCI\VEN_8086&DEV_A0E8"]);
        var system = new SystemInfo { Manufacturer = "LENOVO", Model = "20XWCTO1WW", OperatingSystemVersion = "10.0.26100" };

        var result = await new LenovoVersionProvider(Downloader).CheckAsync([device], system, TestContext.Current.CancellationToken);

        AssertNewer(result, device.Device.InstanceId);
    }

    [Fact]
    public async Task Hp_8B41()
    {
        RequireOnline();
        var device = Fixtures.Device("PCI\\AUDIO\\1", [@"PCI\VEN_1022&DEV_15E2&REV_60"], "1.0.0.0", new DateOnly(2015, 1, 1));
        var system = new SystemInfo { Manufacturer = "HP", BaseBoardProduct = "8B41", OperatingSystemVersion = "10.0.26100", OperatingSystemDisplayVersion = "24H2" };

        var result = await new HpVersionProvider(Downloader, new ExpandCabExtractor()).CheckAsync([device], system, TestContext.Current.CancellationToken);

        AssertNewer(result, device.Device.InstanceId);
    }

    /// <summary>HP's platform list → product number → support.hp.com model page (checked by hand: "HP EliteBook 840 G8 Notebook PC").</summary>
    [Fact]
    public async Task Hp_880D_ModelPage()
    {
        RequireOnline();
        var system = new SystemInfo { Manufacturer = "HP", Model = "HP EliteBook 840 G8 Notebook PC", BaseBoardProduct = "880D" };

        var page = await new HpModelPageProvider(Downloader, new ExpandCabExtractor()).FindModelPageAsync(system, TestContext.Current.CancellationToken);

        Assert.Equal("https://support.hp.com/drivers/model/38460774", page?.AbsoluteUri);
    }
}
