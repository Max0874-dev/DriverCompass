using DriverCompass.Core.Installation;
using DriverCompass.Core.Models;
using DriverCompass.Installer.Packages;
using DriverCompass.Installer.Sources;
using DriverCompass.VersionProviders.Lenovo;
using DriverCompass.VersionProviders.Net;
using DriverCompass.VersionProviders.Packages;

namespace DriverCompass.Installer.Tests;

public class LenovoInnoExtractorTests
{
    private sealed class FakeRunner(int? exit, bool writeFiles = true) : IExtractorProcessRunner
    {
        public List<(string File, IReadOnlyList<string> Args, string WorkDir)> Calls { get; } = [];

        public int? Run(string fileName, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeout)
        {
            Calls.Add((fileName, arguments, workingDirectory));
            if (writeFiles)
            {
                var target = arguments.Single(a => a.StartsWith("/DIR=", StringComparison.Ordinal))[5..];
                File.WriteAllText(Path.Combine(target, "driver.inf"), "[Version]");
            }

            return exit;
        }
    }

    private static OemPackage Lenovo(string? installer = "n32li04w.exe", PackageExtraction extraction = PackageExtraction.LenovoInno) => new(
        "lenovo", "n32li04w", new Uri("https://download.lenovo.com/pccbbs/mobiles/n32li04w.exe"), new string('A', 64), null, "Serial IO", "30.100.2129.8",
        [@"PCI\VEN_8086&DEV_A0E8"], ["Lenovo"], ["download.lenovo.com"], extraction, installer);

    [Fact]
    public void RunsTheVerifiedExe_InTheWorkFolder_WithFixedArgumentsOnly()
    {
        using var dir = new TempDir();
        var cached = dir.File(@"cache\abc.bin", "MZ");
        var target = Path.Combine(dir.Path, "work", "req", "extract");
        var runner = new FakeRunner(0);

        Assert.True(new LenovoInnoExtractor(runner).TryExtract(Lenovo(), cached, target));

        var call = Assert.Single(runner.Calls);
        Assert.Equal(Path.Combine(dir.Path, "work", "req", "n32li04w.exe"), call.File);
        Assert.Equal(["/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/DIR=" + target, "/EXTRACT=YES"], call.Args);
        Assert.Equal(Path.Combine(dir.Path, "work", "req"), call.WorkDir);
        Assert.False(File.Exists(call.File));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(null)]
    public void FailedOrTimedOutExtraction_ReturnsFalse(int? exit)
    {
        using var dir = new TempDir();
        Assert.False(new LenovoInnoExtractor(new FakeRunner(exit)).TryExtract(Lenovo(), dir.File("pkg.bin", "MZ"), Path.Combine(dir.Path, "w", "extract")));
    }

    [Theory]
    [InlineData("..\\evil.exe")]
    [InlineData("setup.cmd")]
    [InlineData(null)]
    public void SuspiciousInstallerNames_AreNeverStarted(string? installer)
    {
        using var dir = new TempDir();
        var runner = new FakeRunner(0);
        Assert.False(new LenovoInnoExtractor(runner).TryExtract(Lenovo(installer), dir.File("pkg.bin", "MZ"), Path.Combine(dir.Path, "w", "extract")));
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void Dispatcher_UsesLenovoExtractorOnlyForLenovoPackages()
    {
        using var dir = new TempDir();
        var lenovoRunner = new FakeRunner(0);
        var extractor = new PackageExtractor(new SevenZipPayloadExtractor(), new LenovoInnoExtractor(lenovoRunner));

        // A Dell/HP package never reaches the Lenovo extractor (the fake "EXE" has no 7z payload → false).
        Assert.False(extractor.TryExtract(Lenovo(extraction: PackageExtraction.SevenZip), dir.File("dell.bin", "MZ"), Path.Combine(dir.Path, "a", "extract")));
        Assert.Empty(lenovoRunner.Calls);

        Assert.True(extractor.TryExtract(Lenovo(), dir.File("lenovo.bin", "MZ"), Path.Combine(dir.Path, "b", "extract")));
        Assert.Single(lenovoRunner.Calls);
    }

    [Theory]
    [InlineData("n32li04w", true)]
    [InlineData("n4ody12w_w10w11", true)]
    [InlineData("N32LI04W", false)]
    [InlineData("../x", false)]
    [InlineData("n3", false)]
    public void LenovoPackageIds(string id, bool valid) => Assert.Equal(valid, InstallProtocol.IsLenovoPackageId(id));
}

/// <summary>
/// Real Lenovo package: catalog 20XW → n32li04w (Intel Serial IO) → download → SHA-256 → signer "Lenovo"
/// → Lenovo's EXE runs ONCE, WITHOUT admin rights, only with the extract switch into a temp folder → matching signed INF.
/// Nothing is installed. Run with DRIVERCOMPASS_ONLINE_TESTS=1.
/// </summary>
public sealed class OnlineLenovoPackageTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly HttpClient _http = DriverCompass.VersionProviders.VersionProviderFactory.CreateHttpClient();

    public void Dispose()
    {
        _http.Dispose();
        _dir.Dispose();
    }

    private sealed class FixedSystem(SystemInfo system) : ISystemQuery
    {
        public SystemInfo Read() => system;
    }

    [Fact]
    public async Task Lenovo_20XW_SerialIo()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("DRIVERCOMPASS_ONLINE_TESTS") == "1", "Set DRIVERCOMPASS_ONLINE_TESTS=1 to run against the real packages.");
        var system = new SystemInfo { Manufacturer = "LENOVO", Model = "20XWCTO1WW", OperatingSystemVersion = "10.0.26100" };
        var downloader = new CatalogDownloader(_http, Path.Combine(_dir.Path, "cache"));
        var catalog = new LenovoVersionProvider(downloader);

        var package = await catalog.FindPackageAsync(system, "n32li04w", TestContext.Current.CancellationToken);
        Assert.NotNull(package);

        var device = new DeviceInfo { InstanceId = @"PCI\VEN_8086&DEV_A0E8\0", HardwareIds = [@"PCI\VEN_8086&DEV_A0E8&SUBSYS_22AA17AA&REV_20"], CompatibleIds = [@"PCI\VEN_8086&DEV_A0E8"] };
        var source = new OemPackageSource(catalog, new FixedSystem(system), downloader, new AuthenticodeFileVerifier(),
            new PackageExtractor(new SevenZipPayloadExtractor(), new LenovoInnoExtractor(new ExtractorProcessRunner())),
            new FakeDriverStore([]), Path.Combine(_dir.Path, "work"));

        var request = new InstallRequest { RequestId = Guid.NewGuid(), Action = InstallAction.Install, DeviceInstanceId = device.InstanceId, Source = "lenovo", PackageId = "n32li04w" };
        var result = await source.PrepareAsync(request, device, TestContext.Current.CancellationToken);
        if (result.Package is { } prepared)
        {
            source.Cleanup(prepared);
        }

        Assert.True(result.Package is not null, $"{result.Outcome} {result.Reason}");
    }
}
