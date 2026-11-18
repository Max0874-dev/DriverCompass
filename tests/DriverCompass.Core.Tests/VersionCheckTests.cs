using DriverCompass.Core.Abstractions;
using DriverCompass.Core.Models;
using DriverCompass.Core.Scanning;
using DriverCompass.Core.Versions;

namespace DriverCompass.Core.Tests;

public class DriverVersionTests
{
    [Theory]
    [InlineData("30.100.2129.8", 30, 100, 2129, 8)]
    [InlineData("30.100.2129.8^", 30, 100, 2129, 8)]
    [InlineData(" 6.0.2.114 ", 6, 0, 2, 114)]
    [InlineData("1.2", 1, 2, 0, 0)]
    [InlineData("7", 7, 0, 0, 0)]
    [InlineData("31.0.15.5222 (A03)", 31, 0, 15, 5222)]
    public void Parses(string text, int a, int b, int c, int d)
    {
        Assert.True(DriverVersion.TryParse(text, out var v));
        Assert.Equal(new DriverVersion(a, b, c, d), v);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("A03")]
    [InlineData("1.2.3.4.5")]
    [InlineData("99999999999.1")]
    public void RejectsInvalid(string? text) => Assert.False(DriverVersion.TryParse(text, out _));

    [Fact]
    public void ComparesNumerically_NotAsText()
    {
        Assert.True(DriverVersion.TryParse("23.100.0.1", out var newer));
        Assert.True(DriverVersion.TryParse("23.60.1.2", out var older));
        Assert.True(newer > older);
        Assert.True(DriverVersion.TryParse("1.2", out var shortForm));
        Assert.True(DriverVersion.TryParse("1.2.0.0", out var longForm));
        Assert.Equal(0, shortForm.CompareTo(longForm));
    }
}

public class VersionMatchingTests
{
    private static DeviceAssessment Device(string instanceId, string[] hardwareIds, DriverInfo? driver, string[]? compatibleIds = null) => new()
    {
        Device = new DeviceInfo { InstanceId = instanceId, HardwareIds = hardwareIds, CompatibleIds = compatibleIds ?? [], Driver = driver },
        Status = DeviceStatus.Ok,
        Reason = StatusReason.VendorDriver,
    };

    private static DriverInfo Driver(string version, int year = 2024) =>
        new() { ProviderName = "Intel", Version = version, Date = new DateOnly(year, 1, 1) };

    [Fact]
    public void NewerCatalogVersion_IsNewerAvailable()
    {
        var device = Device("PCI\\X\\1", [@"PCI\VEN_8086&DEV_2725&SUBSYS_00248086&REV_1A"], Driver("23.60.1.2"));
        var entries = new[] { new CatalogDriverEntry([@"pci\ven_8086&dev_2725&subsys_00248086&rev_1a"], "23.100.0.4", null, "WLAN") };

        var result = VersionMatching.Match([device], entries, "dell")["PCI\\X\\1"];

        Assert.Equal(VersionCheckState.NewerAvailable, result.State);
        Assert.Equal("23.100.0.4", result.LatestVersion);
        Assert.Equal("WLAN", result.PackageName);
    }

    [Fact]
    public void SameOrOlderVersion_IsUpToDate()
    {
        var device = Device("D1", [@"PCI\VEN_8086&DEV_2725"], Driver("23.60.1.2"));
        var entries = new[] { new CatalogDriverEntry([@"PCI\VEN_8086&DEV_2725"], "23.60.1.2^", null) };

        var result = VersionMatching.Match([device], entries, "lenovo")["D1"];

        Assert.Equal(VersionCheckState.UpToDate, result.State);
        Assert.Equal("23.60.1.2", result.LatestVersion);
    }

    [Fact]
    public void VendorCompatibleId_Matches_ButClassOnlyCompatibleId_DoesNot()
    {
        var withVendorId = Device("D1", [@"PCI\VEN_8086&DEV_A0E8&SUBSYS_1234&REV_01"], Driver("30.0.0.1"), [@"PCI\VEN_8086&DEV_A0E8", @"PCI\CC_0C80"]);
        var classOnly = Device("D2", [@"PCI\VEN_10EC&DEV_8125&SUBSYS_1"], Driver("1.0"), [@"PCI\CC_0C80"]);
        var entries = new[]
        {
            new CatalogDriverEntry([@"PCI\VEN_8086&DEV_A0E8"], "30.100.2129.8", null),
            new CatalogDriverEntry([@"PCI\CC_0C80"], "99.0", null),
        };

        var results = VersionMatching.Match([withVendorId, classOnly], entries, "lenovo");

        Assert.Equal(VersionCheckState.NewerAvailable, results["D1"].State);
        Assert.Equal("30.100.2129.8", results["D1"].LatestVersion);
        Assert.False(results.ContainsKey("D2"));
    }

    [Fact]
    public void SeveralEntries_NewestWins()
    {
        var device = Device("D1", [@"USB\VID_0BDA&PID_4001"], Driver("6.3.9600.2000"));
        var entries = new[]
        {
            new CatalogDriverEntry([@"USB\VID_0BDA&PID_4001"], "6.3.9600.2330", null),
            new CatalogDriverEntry([@"USB\VID_0BDA&PID_4001"], "6.3.9600.2400", null),
            new CatalogDriverEntry([@"USB\VID_0BDA&PID_4001"], "6.3.9600.2100", null),
        };

        Assert.Equal("6.3.9600.2400", VersionMatching.Match([device], entries, "dell")["D1"].LatestVersion);
    }

    [Fact]
    public void WithoutComparableVersion_FallsBackToDate_ThenUnknown()
    {
        var installed = new DriverInfo { ProviderName = "X", Version = "A01", Date = new DateOnly(2023, 5, 1) };

        Assert.Equal(VersionCheckState.NewerAvailable, VersionMatching.Compare(installed, null, new DateOnly(2024, 1, 1)));
        Assert.Equal(VersionCheckState.UpToDate, VersionMatching.Compare(installed, null, new DateOnly(2023, 5, 1)));
        Assert.Equal(VersionCheckState.Unknown, VersionMatching.Compare(installed with { Date = null }, null, new DateOnly(2024, 1, 1)));
        Assert.Equal(VersionCheckState.Unknown, VersionMatching.Compare(null, "1.0", null));
    }
}

public class VersionCheckServiceTests
{
    private sealed class FakeProvider(string id, Func<VersionProviderResult>? result = null, Exception? error = null, bool applicable = true) : IVersionProvider
    {
        public int Calls { get; private set; }
        public VersionSourceInfo Source { get; } = new(id, true);
        public bool IsApplicable(SystemInfo system) => applicable;

        public Task<VersionProviderResult> CheckAsync(IReadOnlyList<DeviceAssessment> devices, SystemInfo system, CancellationToken cancellationToken = default)
        {
            Calls++;
            return error is not null ? Task.FromException<VersionProviderResult>(error) : Task.FromResult(result!());
        }
    }

    private static readonly DeviceAssessment Wifi = new()
    {
        Device = MockDeviceScanner.Devices.Single(d => d.InstanceId.StartsWith(@"PCI\VEN_8086&DEV_2725", StringComparison.Ordinal)),
        Status = DeviceStatus.Ok,
        Reason = StatusReason.VendorDriver,
    };

    private static VersionProviderResult One(string provider, VersionCheckState state, string? version = null) =>
        new(new Dictionary<string, VersionCheckResult> { [Wifi.Device.InstanceId] = new(provider, state, version) });

    [Fact]
    public async Task OnlyEnabledSourcesRun()
    {
        var dell = new FakeProvider(VersionSourceIds.Dell, () => One("dell", VersionCheckState.UpToDate));
        var wu = new FakeProvider(VersionSourceIds.WindowsUpdate, () => One("windowsupdate", VersionCheckState.UpToDate));
        var service = new VersionCheckService([dell, wu]);

        var run = await service.CheckAllAsync([Wifi], SystemInfo.Empty, new HashSet<string> { "dell" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, dell.Calls);
        Assert.Equal(0, wu.Calls);
        Assert.Single(run.Sources);
    }

    [Fact]
    public async Task NoSourceEnabled_EverythingUnknown_NothingCalled()
    {
        var dell = new FakeProvider(VersionSourceIds.Dell, () => One("dell", VersionCheckState.NewerAvailable, "99"));
        var run = await new VersionCheckService([dell]).CheckAllAsync([Wifi], SystemInfo.Empty, new HashSet<string>(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, dell.Calls);
        Assert.Equal(VersionCheckState.Unknown, run.Devices[0].VersionCheck!.State);
    }

    [Fact]
    public async Task NewerAvailableBeatsUpToDate_FromAnotherSource()
    {
        var dell = new FakeProvider(VersionSourceIds.Dell, () => One("dell", VersionCheckState.UpToDate, "23.60.1.2"));
        var wu = new FakeProvider(VersionSourceIds.WindowsUpdate, () => One("windowsupdate", VersionCheckState.NewerAvailable));
        var run = await new VersionCheckService([dell, wu]).CheckAllAsync(
            [Wifi], SystemInfo.Empty, new HashSet<string> { "dell", "windowsupdate" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("windowsupdate", run.Devices[0].VersionCheck!.ProviderId);
        Assert.Equal(VersionCheckState.NewerAvailable, run.Devices[0].VersionCheck!.State);
    }

    [Fact]
    public void Merge_HighestNewerVersionWins()
    {
        var merged = VersionCheckService.Merge(
        [
            new("lenovo", VersionCheckState.NewerAvailable, "2.0"),
            new("dell", VersionCheckState.NewerAvailable, "10.0"),
            new("hp", VersionCheckState.UpToDate, "1.0"),
        ]);

        Assert.Equal("dell", merged.ProviderId);
    }

    [Fact]
    public async Task FailingProvider_IsReported_OthersStillRun()
    {
        var broken = new FakeProvider(VersionSourceIds.Dell, error: new VersionSourceException(VersionSourceReasons.Offline, "offline"));
        var crash = new FakeProvider(VersionSourceIds.Lenovo, error: new InvalidOperationException("bug"));
        var wu = new FakeProvider(VersionSourceIds.WindowsUpdate, () => One("windowsupdate", VersionCheckState.UpToDate));

        var run = await new VersionCheckService([broken, crash, wu]).CheckAllAsync(
            [Wifi], SystemInfo.Empty, new HashSet<string> { "dell", "lenovo", "windowsupdate" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(VersionSourceOutcome.Failed, run.Sources.Single(s => s.SourceId == "dell").Outcome);
        Assert.Equal(VersionSourceReasons.Offline, run.Sources.Single(s => s.SourceId == "dell").Detail);
        Assert.Equal(VersionSourceReasons.Unexpected, run.Sources.Single(s => s.SourceId == "lenovo").Detail);
        Assert.Equal(VersionCheckState.UpToDate, run.Devices[0].VersionCheck!.State);
    }

    [Fact]
    public async Task NotApplicableProvider_IsSkipped()
    {
        var dell = new FakeProvider(VersionSourceIds.Dell, () => One("dell", VersionCheckState.NewerAvailable), applicable: false);
        var run = await new VersionCheckService([dell]).CheckAllAsync([Wifi], SystemInfo.Empty, new HashSet<string> { "dell" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, dell.Calls);
        Assert.Equal(VersionSourceOutcome.NotApplicable, run.Sources[0].Outcome);
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var dell = new FakeProvider(VersionSourceIds.Dell, () => One("dell", VersionCheckState.UpToDate));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new VersionCheckService([dell]).CheckAllAsync([Wifi], SystemInfo.Empty, new HashSet<string> { "dell" }, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task MockProvider_FindsDemoUpdate()
    {
        var devices = MockDeviceScanner.Devices.Select(d => new DeviceAssessment { Device = d, Status = DeviceStatus.Ok, Reason = StatusReason.VendorDriver }).ToList();
        var run = await new VersionCheckService([new MockVersionProvider("dell"), new MockVersionProvider("windowsupdate")]).CheckAllAsync(
            devices, SystemInfo.Empty, new HashSet<string> { "dell", "windowsupdate" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, run.NewerCount);
        Assert.Equal(1, run.UpToDateCount);
    }

    [Fact]
    public void SystemInfo_DetectsWindows11ByBuild()
    {
        Assert.True(new SystemInfo { OperatingSystemVersion = "10.0.26100" }.IsWindows11);
        Assert.False(new SystemInfo { OperatingSystemVersion = "10.0.19045" }.IsWindows11);
        Assert.Null(new SystemInfo().OperatingSystemBuild);
    }
}
