using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using DriverCompass.Core.Installation;
using DriverCompass.Core.Models;
using DriverCompass.Installer.Packages;
using DriverCompass.Installer.Sources;
using DriverCompass.VersionProviders.Dell;
using DriverCompass.VersionProviders.Hp;
using DriverCompass.VersionProviders.Net;
using DriverCompass.VersionProviders.Packages;

namespace DriverCompass.Installer.Tests;

internal sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dc-oem-" + Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public string File(string relative, string content)
    {
        var full = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, content);
        return full;
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, true);
        }
    }
}

public class OemRequestValidationTests
{
    private static InstallRequest Request(string source, string? packageId, string? updateId = null) => new()
    {
        RequestId = Guid.NewGuid(),
        Action = InstallAction.Install,
        DeviceInstanceId = @"PCI\VEN_8086&DEV_2725\4&3A1B2C3D&0&00E4",
        Source = source,
        PackageId = packageId,
        UpdateId = updateId,
    };

    [Theory]
    [InlineData("dell", "FOLDER14023085M/2/Intel-HID-Event-Filter-Driver_CPG7X_WIN64_2.2.2.17_A03.EXE", true)]
    [InlineData("dell", "../FOLDER/evil.EXE", false)]
    [InlineData("dell", "/FOLDER1/x.EXE", false)]
    [InlineData("dell", "FOLDER1/x.msi", false)]
    [InlineData("dell", "FOLDER1/x y.EXE", false)]
    [InlineData("dell", "https://evil.example.com/x.EXE", false)]
    [InlineData("hp", "sp157308", true)]
    [InlineData("hp", "sp15730x", false)]
    [InlineData("hp", "157308", false)]
    [InlineData("lenovo", "n32li04w", false)]
    public void PackageIds(string source, string packageId, bool valid) =>
        Assert.Equal(valid, InstallProtocol.Validate(Request(source, packageId)) is null);

    [Fact]
    public void OemRequest_MustNotCarryAnUpdateId() =>
        Assert.NotNull(InstallProtocol.Validate(Request("hp", "sp157308", Guid.NewGuid().ToString("D"))));
}

public class InfMatcherTests
{
    private const string DeviceId = @"PCI\VEN_8086&DEV_A0E8";

    private static string Inf(string models, string catalog = "CatalogFile = driver.cat") => $"""
        [Version]
        Signature="$WINDOWS NT$"
        {catalog}
        [Manufacturer]
        %Intel% = Intel, {models}
        [Intel.{models}]
        %Dev% = Install, PCI\VEN_8086&DEV_A0E8 ; serial IO
        """;

    private static readonly HashSet<string> Ids = new(StringComparer.OrdinalIgnoreCase) { DeviceId.ToLowerInvariant() };

    [Fact]
    public void MatchingSignedInf_IsChosen()
    {
        using var dir = new TempDir();
        var inf = dir.File(@"x64\driver.inf", Inf("NTamd64"));
        dir.File(@"x64\driver.cat", "cat");

        var found = InfMatcher.FindMatching(dir.Path, Ids, p => p.EndsWith(".cat", StringComparison.OrdinalIgnoreCase) ? "Microsoft Windows Hardware Compatibility Publisher" : null, Architecture.X64);

        var candidate = Assert.Single(found);
        Assert.Equal(inf, candidate.InfPath);
        Assert.Equal(64, candidate.Sha256.Length);
    }

    [Fact]
    public void WrongArchitecture_UnsignedCatalog_MissingCatalog_OrOtherDevice_AreSkipped()
    {
        using var dir = new TempDir();
        dir.File(@"x86\driver.inf", Inf("NTx86"));
        dir.File(@"x86\driver.cat", "cat");
        dir.File(@"nocat\driver.inf", Inf("NTamd64", catalog: "; CatalogFile = driver.cat"));
        dir.File(@"unsigned\driver.inf", Inf("NTamd64"));
        dir.File(@"unsigned\driver.cat", "cat");
        dir.File(@"other\driver.inf", Inf("NTamd64").Replace("DEV_A0E8", "DEV_FFFF", StringComparison.Ordinal));
        dir.File(@"other\driver.cat", "cat");
        dir.File(@"traversal\driver.inf", Inf("NTamd64", catalog: @"CatalogFile = ..\x64\driver.cat"));

        var found = InfMatcher.FindMatching(dir.Path, Ids, p => p.Contains("unsigned", StringComparison.Ordinal) ? null : "Signer", Architecture.X64);

        Assert.Empty(found);
    }

    [Fact]
    public void HardwareIdInCommentOnly_DoesNotMatch()
    {
        using var dir = new TempDir();
        dir.File("driver.inf", """
            [Version]
            CatalogFile = driver.cat
            [Models.NTamd64]
            ; %Dev% = Install, PCI\VEN_8086&DEV_A0E8
            %Dev% = Install, PCI\VEN_8086&DEV_A0E9
            """);
        dir.File("driver.cat", "cat");

        Assert.Empty(InfMatcher.FindMatching(dir.Path, Ids, _ => "Signer", Architecture.X64));
    }
}

public class SevenZipPayloadExtractorTests
{
    private static string Tar => Path.Combine(Environment.SystemDirectory, "tar.exe");

    private static void Run(params string[] args)
    {
        var psi = new ProcessStartInfo(Tar) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var p = Process.Start(psi)!;
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
    }

    [Fact]
    public void FindsAppendedArchive_BehindAFakeSignature_WithoutRunningTheExe()
    {
        Assert.SkipUnless(File.Exists(Tar), "Windows tar.exe not available");
        using var dir = new TempDir();
        dir.File(@"content\src\driver\driver.inf", "[Version]");
        dir.File(@"content\src\driver\driver.cat", "cat");
        var archive = Path.Combine(dir.Path, "payload.7z");
        Run("--format", "7zip", "-cf", archive, "-C", Path.Combine(dir.Path, "content"), "src");

        // "EXE" = stub bytes + a stray 7z signature (like the constant inside a real extractor) + the archive.
        var exe = Path.Combine(dir.Path, "package.exe");
        using (var output = File.Create(exe))
        {
            output.Write(Encoding.ASCII.GetBytes("MZ fake stub "));
            output.Write([0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C, 0x00, 0x04, 0x01, 0x02]);
            output.Write(new byte[4096]);
            output.Write(File.ReadAllBytes(archive));
        }

        var target = Path.Combine(dir.Path, "work", "extract");
        Assert.True(new SevenZipPayloadExtractor().TryExtract(exe, target));
        Assert.True(File.Exists(Path.Combine(target, "src", "driver", "driver.inf")));
        Assert.Equal(2, SevenZipPayloadExtractor.FindSignatures(exe).Count);
    }

    [Fact]
    public void NoArchive_ReturnsFalse()
    {
        using var dir = new TempDir();
        var exe = dir.File("package.exe", "MZ just an installer without payload");
        Assert.False(new SevenZipPayloadExtractor().TryExtract(exe, Path.Combine(dir.Path, "work", "extract")));
    }
}

public sealed class OemPackageSourceTests : IDisposable
{
    private const string HardwareId = @"PCI\VEN_8086&DEV_2725&SUBSYS_00248086";
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private static readonly DeviceInfo Wifi = new()
    {
        InstanceId = @"PCI\VEN_8086&DEV_2725&SUBSYS_00248086&REV_1A\4&3A1B2C3D&0&00E4",
        Name = "Intel Wi-Fi 6E AX210",
        HardwareIds = [HardwareId + "&REV_1A", HardwareId],
        Driver = new DriverInfo { ProviderName = "Intel", Version = "23.60.1.2", InfName = "oem45.inf" },
    };

    private static readonly InstallRequest Request = new()
    {
        RequestId = Guid.NewGuid(),
        Action = InstallAction.Install,
        DeviceInstanceId = Wifi.InstanceId,
        Source = "dell",
        PackageId = "FOLDER123M/1/Intel-WLAN_A01.EXE",
    };

    private static OemPackage Package(params string[] hardwareIds) => new(
        "dell", "FOLDER123M/1/Intel-WLAN_A01.EXE", new Uri("https://downloads.dell.com/FOLDER123M/1/Intel-WLAN_A01.EXE"),
        new string('A', 64), 1000, "Intel WLAN", "23.130.0.4", hardwareIds.Length > 0 ? hardwareIds : [HardwareId],
        ["Dell Technologies Inc."], ["downloads.dell.com"]);

    private sealed class FakeCatalog(OemPackage? package, bool applicable = true) : IOemPackageCatalog
    {
        public string SourceId => "dell";

        public bool IsApplicable(SystemInfo system) => applicable;

        public Task<OemPackage?> FindPackageAsync(SystemInfo system, string packageId, CancellationToken cancellationToken = default) =>
            Task.FromResult(package);
    }

    private sealed class FakeSystem : ISystemQuery
    {
        public SystemInfo Read() => new() { Manufacturer = "Dell Inc.", SystemSku = "0C00" };
    }

    private sealed class FakeDownloader(string path) : ICatalogDownloader
    {
        public List<DownloadRequest> Requests { get; } = [];

        public Task<string> DownloadAsync(DownloadRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(path);
        }
    }

    private sealed class FakeSignatures(string? packageSigner, string? catalogSigner = "Microsoft Windows Hardware Compatibility Publisher") : IFileSignatureVerifier
    {
        public string? GetTrustedSigner(string path) => path.EndsWith(".cat", StringComparison.OrdinalIgnoreCase) ? catalogSigner : packageSigner;
    }

    /// <summary>"Extracts" a driver folder with an INF for the given hardware ID.</summary>
    private sealed class FakeExtractor(string? infHardwareId, bool succeed = true) : IPayloadExtractor
    {
        public bool TryExtract(string packagePath, string targetDirectory)
        {
            if (!succeed)
            {
                return false;
            }

            Directory.CreateDirectory(Path.Combine(targetDirectory, "x64"));
            File.WriteAllText(Path.Combine(targetDirectory, "x64", "netwtw.inf"), $"[Version]\nCatalogFile=netwtw.cat\n[Intel.NTamd64]\n%Dev% = Install, {infHardwareId}\n");
            File.WriteAllText(Path.Combine(targetDirectory, "x64", "netwtw.cat"), "cat");
            return true;
        }
    }

    private OemPackageSource Create(
        OemPackage? package = null,
        string? packageSigner = "Dell Technologies Inc.",
        string? infHardwareId = HardwareId,
        bool extracts = true,
        FakeDriverStore? store = null,
        bool applicable = true) =>
        new(new FakeCatalog(package ?? Package(), applicable), new FakeSystem(), new FakeDownloader(_dir.File("pkg.exe", "MZ")),
            new FakeSignatures(packageSigner), new FakeExtractor(infHardwareId, extracts), store ?? new FakeDriverStore([]), Path.Combine(_dir.Path, "work"));

    [Fact]
    public async Task VerifiedPackage_WithMatchingInf_IsReady()
    {
        var result = await Create().PrepareAsync(Request, Wifi, TestContext.Current.CancellationToken);

        Assert.NotNull(result.Package);
        Assert.Equal("Intel WLAN", result.Package.Title);
        Assert.Single(result.Package.ExpectedInfHashes!);
    }

    [Theory]
    [InlineData("wrong-signer")]
    [InlineData("unsigned")]
    [InlineData("other-device")]
    [InlineData("not-in-catalog")]
    [InlineData("no-archive")]
    [InlineData("inf-for-other-device")]
    [InlineData("other-manufacturer")]
    public async Task Problems_StopBeforeAnyChange(string problem)
    {
        var (source, outcome, reason) = problem switch
        {
            "wrong-signer" => (Create(packageSigner: "Evil Corp"), InstallOutcome.Failed, VersionSourceReasons.SignatureInvalid),
            "unsigned" => (Create(packageSigner: null), InstallOutcome.Failed, VersionSourceReasons.SignatureInvalid),
            "other-device" => (Create(package: Package(@"PCI\VEN_10DE&DEV_2489")), InstallOutcome.DeviceMismatch, "HardwareIdMismatch"),
            "not-in-catalog" => (new OemPackageSource(new FakeCatalog(null), new FakeSystem(), new FakeDownloader("x"), new FakeSignatures(null), new FakeExtractor(null), new FakeDriverStore([]), _dir.Path), InstallOutcome.NotOffered, "PackageNotInCatalog"),
            "no-archive" => (Create(extracts: false), InstallOutcome.NotInstallable, "UnsupportedPackage"),
            "inf-for-other-device" => (Create(infHardwareId: @"PCI\VEN_8086&DEV_FFFF"), InstallOutcome.NotInstallable, "NoMatchingDriver"),
            _ => (Create(applicable: false), InstallOutcome.DeviceMismatch, "WrongManufacturer"),
        };

        var result = await source.PrepareAsync(Request, Wifi, TestContext.Current.CancellationToken);

        Assert.Null(result.Package);
        Assert.Equal(outcome, result.Outcome);
        Assert.Equal(reason, result.Reason);
    }

    [Fact]
    public async Task Download_UsesCatalogHashHostAndLimit()
    {
        var downloader = new FakeDownloader(_dir.File("pkg.exe", "MZ"));
        var source = new OemPackageSource(new FakeCatalog(Package()), new FakeSystem(), downloader, new FakeSignatures("Dell Technologies Inc."),
            new FakeExtractor(HardwareId), new FakeDriverStore([]), Path.Combine(_dir.Path, "work"));

        await source.PrepareAsync(Request, Wifi, TestContext.Current.CancellationToken);

        var request = Assert.Single(downloader.Requests);
        Assert.Equal(new string('A', 64), request.ExpectedSha256);
        Assert.Equal(["downloads.dell.com"], request.AllowedHosts);
        Assert.Equal(OemPackageSource.MaxPackageBytes, request.MaxBytes);
    }

    [Theory]
    [InlineData(0, true, false, "pnputil:0")]
    [InlineData(3010, true, true, "pnputil:3010")]
    [InlineData(259, false, false, UpdateCodes.NoDeviceUpdated)]
    [InlineData(5, false, false, "pnputil:5")]
    public async Task PnpUtilExitCodes(int exit, bool succeeded, bool reboot, string code)
    {
        var store = new FakeDriverStore([]) { AddExitCode = exit };
        var source = Create(store: store);
        var prepared = (await source.PrepareAsync(Request, Wifi, TestContext.Current.CancellationToken)).Package!;

        var result = source.Install(prepared);
        source.Cleanup(prepared);

        Assert.Equal((succeeded, reboot, code), (result.Succeeded, result.RebootRequired, result.Code));
        Assert.EndsWith("netwtw.inf", Assert.Single(store.Added), StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_dir.Path, "work", Request.RequestId.ToString("N"))));
    }

    [Fact]
    public async Task Engine_NoDeviceUpdated_RemovesOnlyOurOwnPackage()
    {
        var calls = new List<string>();
        var store = new FakeDriverStore(calls) { AddExitCode = 259 };
        var source = Create(store: store);
        var expected = (await source.PrepareAsync(Request, Wifi, TestContext.Current.CancellationToken)).Package!.ExpectedInfHashes!.Single();

        // After pnputil: our INF is in the store as oem90.inf, plus an unrelated package that appeared meanwhile.
        var storeAfter = new FakeDriverStore(calls)
        {
            AddExitCode = 259,
            AfterInstall = new(StringComparer.OrdinalIgnoreCase) { ["oem1.inf"] = "AAA", ["oem90.inf"] = expected, ["oem91.inf"] = "UNRELATED" },
        };
        var memory = new MemoryStore();
        var engine = new InstallerEngine(new FakeDevices(Wifi), [Create(store: storeAfter)], new FakeRestorePoints(new RestorePointInfo(true), calls), storeAfter, memory);

        var result = await engine.ExecuteAsync(Request with { RequestId = Guid.NewGuid() }, TestContext.Current.CancellationToken);

        Assert.Equal(InstallOutcome.NotInstallable, result.Outcome);
        Assert.Equal(UpdateCodes.NoDeviceUpdated, result.Message);
        Assert.Equal(["oem90.inf"], storeAfter.Deleted);
    }

    [Fact]
    public async Task Engine_FailedPreparation_CreatesNoRestorePoint()
    {
        var calls = new List<string>();
        var engine = new InstallerEngine(new FakeDevices(Wifi), [Create(packageSigner: "Evil Corp")], new FakeRestorePoints(new RestorePointInfo(true), calls),
            new FakeDriverStore(calls), new MemoryStore());

        var result = await engine.ExecuteAsync(Request, TestContext.Current.CancellationToken);

        Assert.Equal(InstallOutcome.Failed, result.Outcome);
        Assert.DoesNotContain("restorepoint", calls);
    }
}

/// <summary>
/// The real chain with real vendor packages, without admin rights and WITHOUT installing anything:
/// signed catalog → package URL + SHA-256 → download → signer → 7z payload via tar → matching signed INF.
/// Run with DRIVERCOMPASS_ONLINE_TESTS=1.
/// </summary>
public sealed class OnlineOemPackageTests : IDisposable
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

    private async Task<PrepareResult> PrepareAsync(IOemPackageCatalog catalog, SystemInfo system, string packageId)
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("DRIVERCOMPASS_ONLINE_TESTS") == "1", "Set DRIVERCOMPASS_ONLINE_TESTS=1 to run against the real packages.");
        var package = await catalog.FindPackageAsync(system, packageId, TestContext.Current.CancellationToken);
        Assert.NotNull(package);

        // A device that carries the first hardware ID the catalog lists for this package.
        var device = new DeviceInfo { InstanceId = package.HardwareIds[0] + @"\0", HardwareIds = [package.HardwareIds[0]] };
        var downloader = new CatalogDownloader(_http, Path.Combine(_dir.Path, "cache"));
        var source = new OemPackageSource(catalog, new FixedSystem(system), downloader, new AuthenticodeFileVerifier(),
            new SevenZipPayloadExtractor(), new FakeDriverStore([]), Path.Combine(_dir.Path, "work"));

        var request = new InstallRequest { RequestId = Guid.NewGuid(), Action = InstallAction.Install, DeviceInstanceId = device.InstanceId, Source = source.SourceId, PackageId = packageId };
        var result = await source.PrepareAsync(request, device, TestContext.Current.CancellationToken);
        if (result.Package is { } prepared)
        {
            source.Cleanup(prepared);
        }

        return result;
    }

    private (CatalogDownloader Downloader, ExpandCabExtractor Cabs) Infrastructure() =>
        (new CatalogDownloader(_http, Path.Combine(_dir.Path, "catalogs")), new ExpandCabExtractor(Path.Combine(_dir.Path, "cab")));

    [Fact]
    public async Task Dell_Latitude5440_IntelHidEventFilter()
    {
        var (downloader, cabs) = Infrastructure();
        var result = await PrepareAsync(new DellVersionProvider(downloader, cabs),
            new SystemInfo { Manufacturer = "Dell Inc.", SystemSku = "0C00", OperatingSystemVersion = "10.0.26100" },
            "FOLDER14023085M/2/Intel-HID-Event-Filter-Driver_CPG7X_WIN64_2.2.2.17_A03.EXE");

        Assert.True(result.Package is not null, $"{result.Outcome} {result.Reason}");
    }

    [Fact]
    public async Task Hp_8B41_IntelHidEventFilter()
    {
        var (downloader, cabs) = Infrastructure();
        var result = await PrepareAsync(new HpVersionProvider(downloader, cabs),
            new SystemInfo { Manufacturer = "HP", BaseBoardProduct = "8B41", OperatingSystemVersion = "10.0.26100", OperatingSystemDisplayVersion = "24H2" },
            "sp157308");

        Assert.True(result.Package is not null, $"{result.Outcome} {result.Reason}");
    }
}
