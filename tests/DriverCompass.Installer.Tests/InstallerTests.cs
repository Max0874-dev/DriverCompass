using DriverCompass.Core.Installation;
using DriverCompass.Core.Models;
using DriverCompass.Installer.Client;
using Microsoft.Extensions.Logging.Abstractions;

namespace DriverCompass.Installer.Tests;

internal sealed class FakeDevices(params DeviceInfo?[] sequence) : IDeviceQuery
{
    private int _calls;

    public DeviceInfo? Find(string instanceId) => sequence.Length == 0 ? null : sequence[Math.Min(_calls++, sequence.Length - 1)];
}

internal sealed class FakeWindowsUpdate(OfferedUpdate? update, UpdateInstallResult? result = null) : IWindowsUpdateInstaller
{
    public List<string> Calls { get; } = [];

    public OfferedUpdate? Find(string updateId)
    {
        Calls.Add("find");
        return update;
    }

    public UpdateInstallResult Install(OfferedUpdate offered)
    {
        Calls.Add("install");
        return result ?? new UpdateInstallResult(true, false, "Install:2:0x00000000");
    }
}

internal sealed class FakeRestorePoints(RestorePointInfo info, List<string> calls) : IRestorePointCreator
{
    public RestorePointInfo Create(string description)
    {
        calls.Add("restorepoint");
        return info;
    }
}

internal sealed class FakeDriverStore(List<string> calls) : IDriverStore
{
    public Dictionary<string, string> Files { get; } = new(StringComparer.OrdinalIgnoreCase) { ["oem1.inf"] = "AAA" };
    public Dictionary<string, string>? AfterInstall { get; init; }
    public List<string> Deleted { get; } = [];
    public int DeleteExitCode { get; init; }

    public IReadOnlyDictionary<string, string> Snapshot()
    {
        calls.Add("snapshot");
        if (calls.Contains("install") && AfterInstall is not null)
        {
            return AfterInstall;
        }

        return new Dictionary<string, string>(Files, StringComparer.OrdinalIgnoreCase);
    }

    public string? HashOf(string publishedName) => Files.GetValueOrDefault(publishedName);

    public int DeleteDriver(string publishedName)
    {
        Deleted.Add(publishedName);
        return DeleteExitCode;
    }
}

internal sealed class MemoryStore : IInstallStore
{
    public List<InstallLogEntry> Log { get; } = [];
    public List<InstallResult> Results { get; } = [];

    public IReadOnlyList<InstallLogEntry> ReadLog() => [.. Log];

    public void AppendLog(InstallLogEntry entry) => Log.Add(entry);

    public void WriteResult(InstallResult result) => Results.Add(result);
}

public class InstallerEngineTests
{
    private const string InstanceId = @"USB\VID_8087&PID_0029\5&1C8E1F0A&0&10";
    private const string UpdateId = "6f1c2b7e-0d1a-4c5e-9b0a-3e2d1c4b5a69";

    private static readonly DeviceInfo Bluetooth = new()
    {
        InstanceId = InstanceId,
        Name = "Intel(R) Wireless Bluetooth(R)",
        HardwareIds = [@"USB\VID_8087&PID_0029&REV_0001", @"USB\VID_8087&PID_0029"],
        Driver = new DriverInfo { ProviderName = "Intel", Version = "22.240.0.2", InfName = "oem1.inf" },
    };

    private static readonly OfferedUpdate Offer = new(UpdateId, 201, IsDriver: true, EulaAccepted: true,
        "Intel Corporation - Bluetooth - 23.150.0.3", @"usb\vid_8087&pid_0029", "Intel", new DateOnly(2025, 9, 18));

    private static InstallRequest Request(bool allowWithoutRestorePoint = false) => new()
    {
        RequestId = Guid.NewGuid(),
        Action = InstallAction.Install,
        DeviceInstanceId = InstanceId,
        UpdateId = UpdateId,
        Revision = 201,
        AllowWithoutRestorePoint = allowWithoutRestorePoint,
    };

    private sealed record Setup(InstallerEngine Engine, List<string> Calls, FakeWindowsUpdate WindowsUpdate, FakeDriverStore DriverStore, MemoryStore Store);

    private static Setup Create(
        OfferedUpdate? offer = null,
        RestorePointInfo? restorePoint = null,
        UpdateInstallResult? installResult = null,
        DeviceInfo?[]? devices = null,
        FakeDriverStore? driverStore = null,
        bool noOffer = false)
    {
        var calls = new List<string>();
        var wu = new FakeWindowsUpdate(noOffer ? null : offer ?? Offer, installResult);
        var ds = driverStore ?? new FakeDriverStore(calls)
        {
            AfterInstall = new(StringComparer.OrdinalIgnoreCase) { ["oem1.inf"] = "AAA", ["oem87.inf"] = "BBB" },
        };
        var store = new MemoryStore();
        var updatedDevice = Bluetooth with { Driver = Bluetooth.Driver! with { Version = "23.150.0.3", InfName = "oem87.inf" } };
        var engine = new InstallerEngine(
            new FakeDevices(devices ?? [Bluetooth, updatedDevice]),
            new LoggingWindowsUpdate(wu, calls),
            new FakeRestorePoints(restorePoint ?? new RestorePointInfo(true), calls),
            ds,
            store);
        return new Setup(engine, calls, wu, ds, store);
    }

    /// <summary>Records WU calls into the shared call list to check the order of steps.</summary>
    private sealed class LoggingWindowsUpdate(FakeWindowsUpdate inner, List<string> calls) : IWindowsUpdateInstaller
    {
        public OfferedUpdate? Find(string updateId)
        {
            calls.Add("find");
            return inner.Find(updateId);
        }

        public UpdateInstallResult Install(OfferedUpdate update)
        {
            calls.Add("install");
            return inner.Install(update);
        }
    }

    [Fact]
    public void Install_RunsStepsInOrder_AndLogsNewPackages()
    {
        var s = Create();
        var result = s.Engine.Execute(Request());

        Assert.Equal(InstallOutcome.Installed, result.Outcome);
        Assert.Equal(["find", "restorepoint", "snapshot", "install", "snapshot"], s.Calls);

        var entry = Assert.Single(s.Store.Log);
        Assert.Equal([new PublishedInf("oem87.inf", "BBB")], entry.PublishedInfs);
        Assert.Equal("22.240.0.2", entry.PreviousVersion);
        Assert.Equal("23.150.0.3", entry.InstalledVersion);
        Assert.True(entry.RestorePointCreated);
        Assert.Equal(result, Assert.Single(s.Store.Results));
    }

    [Fact]
    public void RebootRequired_IsReported()
    {
        var s = Create(installResult: new UpdateInstallResult(true, true, "Install:2:0x00000000"));
        Assert.Equal(InstallOutcome.RebootRequired, s.Engine.Execute(Request()).Outcome);
    }

    [Fact]
    public void FailedInstall_IsLoggedAsFailed()
    {
        var s = Create(installResult: new UpdateInstallResult(false, false, "Install:4:0x80240022"));
        var result = s.Engine.Execute(Request());

        Assert.Equal(InstallOutcome.Failed, result.Outcome);
        Assert.Equal("Install:4:0x80240022", result.Message);
        Assert.Equal(InstallOutcome.Failed, Assert.Single(s.Store.Log).Outcome);
    }

    [Fact]
    public void NoRestorePoint_WithoutPermission_StopsBeforeInstalling()
    {
        var s = Create(restorePoint: new RestorePointInfo(false, "FrequencyLimit", DateTimeOffset.Now.AddHours(-2)));
        var result = s.Engine.Execute(Request());

        Assert.Equal(InstallOutcome.RestorePointFailed, result.Outcome);
        Assert.Equal("FrequencyLimit", result.RestorePoint!.Reason);
        Assert.DoesNotContain("install", s.Calls);
    }

    [Fact]
    public void NoRestorePoint_WithPermission_Installs()
    {
        var s = Create(restorePoint: new RestorePointInfo(false, "Disabled"));
        var result = s.Engine.Execute(Request(allowWithoutRestorePoint: true));

        Assert.Equal(InstallOutcome.Installed, result.Outcome);
        Assert.False(Assert.Single(s.Store.Log).RestorePointCreated);
    }

    [Fact]
    public void UpdateNoLongerOffered_ChangesNothing()
    {
        var s = Create(noOffer: true);
        Assert.Equal(InstallOutcome.NotOffered, s.Engine.Execute(Request()).Outcome);
        Assert.DoesNotContain("restorepoint", s.Calls);
        Assert.DoesNotContain("install", s.Calls);
    }

    [Theory]
    [InlineData(false, true, @"USB\VID_8087&PID_0029", "NotADriver")]
    [InlineData(true, true, @"PCI\VEN_10DE&DEV_2489", "HardwareIdMismatch")]
    [InlineData(true, true, null, "HardwareIdMismatch")]
    public void UpdateNotForThisDevice_IsRefused(bool isDriver, bool eula, string? hardwareId, string reason)
    {
        var s = Create(offer: Offer with { IsDriver = isDriver, EulaAccepted = eula, DriverHardwareId = hardwareId });
        var result = s.Engine.Execute(Request());

        Assert.Equal(InstallOutcome.DeviceMismatch, result.Outcome);
        Assert.Equal(reason, result.Message);
        Assert.DoesNotContain("install", s.Calls);
    }

    [Fact]
    public void LicenseAcceptance_IsNeverDoneSilently()
    {
        var s = Create(offer: Offer with { EulaAccepted = false });
        var result = s.Engine.Execute(Request());

        Assert.Equal("EulaRequired", result.Message);
        Assert.DoesNotContain("install", s.Calls);
    }

    [Fact]
    public void UnknownDevice_IsRefused()
    {
        var s = Create(devices: [null]);
        Assert.Equal(InstallOutcome.DeviceMismatch, s.Engine.Execute(Request()).Outcome);
        Assert.DoesNotContain("find", s.Calls);
    }

    [Fact]
    public void InvalidRequest_IsRefused_ButStillAnswered()
    {
        var s = Create();
        var result = s.Engine.Execute(Request() with { UpdateId = "not-a-guid" });

        Assert.Equal(InstallOutcome.Failed, result.Outcome);
        Assert.Empty(s.Calls);
        Assert.Single(s.Store.Results);
    }

    [Fact]
    public void Rollback_RemovesExactlyTheLoggedPackage()
    {
        var calls = new List<string>();
        var ds = new FakeDriverStore(calls) { DeleteExitCode = 0 };
        ds.Files["oem87.inf"] = "BBB";
        var s = Create(driverStore: ds);
        var installed = InstalledEntry();
        s.Store.Log.Add(installed);

        var result = s.Engine.Execute(new InstallRequest { RequestId = Guid.NewGuid(), Action = InstallAction.Rollback, LogEntryId = installed.Id });

        Assert.Equal(InstallOutcome.RolledBack, result.Outcome);
        Assert.Equal(["oem87.inf"], ds.Deleted);
        Assert.Equal(installed.Id, s.Store.Log[^1].RolledBackEntryId);
        Assert.False(InstallProtocol.CanRollBack(installed, s.Store.Log));
    }

    [Fact]
    public void Rollback_RefusesChangedPackage()
    {
        var calls = new List<string>();
        var ds = new FakeDriverStore(calls);
        ds.Files["oem87.inf"] = "DIFFERENT";
        var s = Create(driverStore: ds);
        var installed = InstalledEntry();
        s.Store.Log.Add(installed);

        var result = s.Engine.Execute(new InstallRequest { RequestId = Guid.NewGuid(), Action = InstallAction.Rollback, LogEntryId = installed.Id });

        Assert.Equal("InfChanged", result.Message);
        Assert.Empty(ds.Deleted);
    }

    [Fact]
    public void Rollback_RefusesSuspiciousNamesAndUnknownEntries()
    {
        var calls = new List<string>();
        var ds = new FakeDriverStore(calls);
        ds.Files["display.inf"] = "BBB";
        var s = Create(driverStore: ds);
        var tampered = InstalledEntry() with { PublishedInfs = [new PublishedInf("display.inf", "BBB")] };
        s.Store.Log.Add(tampered);

        Assert.Equal("InvalidInfName", s.Engine.Execute(new InstallRequest { RequestId = Guid.NewGuid(), Action = InstallAction.Rollback, LogEntryId = tampered.Id }).Message);
        Assert.Equal("EntryNotRollbackable", s.Engine.Execute(new InstallRequest { RequestId = Guid.NewGuid(), Action = InstallAction.Rollback, LogEntryId = Guid.NewGuid() }).Message);
        Assert.Empty(ds.Deleted);
    }

    [Fact]
    public void Rollback_PnpUtilReboot_IsReported()
    {
        var calls = new List<string>();
        var ds = new FakeDriverStore(calls) { DeleteExitCode = 3010 };
        ds.Files["oem87.inf"] = "BBB";
        var s = Create(driverStore: ds);
        var installed = InstalledEntry();
        s.Store.Log.Add(installed);

        Assert.Equal(InstallOutcome.RebootRequired,
            s.Engine.Execute(new InstallRequest { RequestId = Guid.NewGuid(), Action = InstallAction.Rollback, LogEntryId = installed.Id }).Outcome);
    }

    private static InstallLogEntry InstalledEntry() => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTimeOffset.Now,
        Action = InstallAction.Install,
        Outcome = InstallOutcome.Installed,
        Title = "Intel Bluetooth",
        PublishedInfs = [new PublishedInf("oem87.inf", "BBB")],
    };
}

public class InstallProtocolTests
{
    private static InstallRequest Valid() => new()
    {
        RequestId = Guid.NewGuid(),
        Action = InstallAction.Install,
        DeviceInstanceId = @"PCI\VEN_8086&DEV_2725\4&3A1B2C3D&0&00E4",
        UpdateId = Guid.NewGuid().ToString("D"),
        Revision = 200,
    };

    [Fact]
    public void ValidRequest_Passes() => Assert.Null(InstallProtocol.Validate(Valid()));

    [Theory]
    [InlineData("device")]
    [InlineData("quote")]
    [InlineData("update")]
    [InlineData("revision")]
    [InlineData("logentry")]
    [InlineData("requestid")]
    public void BrokenRequests_AreRejected(string defect)
    {
        var request = defect switch
        {
            "device" => Valid() with { DeviceInstanceId = "x" },
            "quote" => Valid() with { DeviceInstanceId = @"PCI\VEN_1' OR 1=1\x" },
            "update" => Valid() with { UpdateId = "{6f1c2b7e-0d1a-4c5e-9b0a-3e2d1c4b5a69}" },
            "revision" => Valid() with { Revision = -1 },
            "logentry" => Valid() with { LogEntryId = Guid.NewGuid() },
            _ => Valid() with { RequestId = Guid.Empty },
        };

        Assert.NotNull(InstallProtocol.Validate(request));
    }

    [Fact]
    public void RollbackRequest_MustOnlyCarryTheEntryId()
    {
        var ok = new InstallRequest { RequestId = Guid.NewGuid(), Action = InstallAction.Rollback, LogEntryId = Guid.NewGuid() };
        Assert.Null(InstallProtocol.Validate(ok));
        Assert.NotNull(InstallProtocol.Validate(ok with { UpdateId = Guid.NewGuid().ToString("D") }));
        Assert.NotNull(InstallProtocol.Validate(ok with { LogEntryId = null }));
    }

    [Fact]
    public void RequestFile_RoundTrip_UnknownFieldsAndOversizeRejected()
    {
        var path = Path.Combine(Path.GetTempPath(), "dc-req-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var request = Valid();
            File.WriteAllText(path, InstallProtocol.Serialize(request));
            Assert.Equal(request, InstallProtocol.ReadRequest(path, out _));

            File.WriteAllText(path, InstallProtocol.Serialize(request).Replace("{", "{\"installerPath\":\"C:\\\\evil.exe\",", StringComparison.Ordinal));
            Assert.Null(InstallProtocol.ReadRequest(path, out var error));
            Assert.NotNull(error);

            File.WriteAllText(path, new string(' ', InstallProtocol.MaxRequestBytes + 1));
            Assert.Null(InstallProtocol.ReadRequest(path, out _));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Log_SkipsBrokenLines()
    {
        var entry = new InstallLogEntry { Id = Guid.NewGuid(), Timestamp = DateTimeOffset.Now, Action = InstallAction.Install, Outcome = InstallOutcome.Installed };
        var parsed = InstallProtocol.ParseLog([InstallProtocol.Serialize(entry), "{ broken", "", InstallProtocol.Serialize(entry with { Id = Guid.NewGuid() })]);

        Assert.Equal(2, parsed.Count);
        Assert.Equal(entry.Id, parsed[0].Id);
    }
}

public sealed class ElevatedInstallerClientTests : IDisposable
{
    private readonly string _protected = Path.Combine(Path.GetTempPath(), "dc-prot-" + Guid.NewGuid().ToString("N"));

    public ElevatedInstallerClientTests() => Directory.CreateDirectory(Path.Combine(_protected, "results"));

    public void Dispose() => Directory.Delete(_protected, true);

    private sealed class FakeLauncher(Func<IReadOnlyList<string>, int> run) : IProcessLauncher
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];

        public Task<int> RunElevatedAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            Calls.Add(arguments);
            return Task.FromResult(run(arguments));
        }
    }

    private static InstallRequest Request() => new()
    {
        RequestId = Guid.NewGuid(),
        Action = InstallAction.Install,
        DeviceInstanceId = @"USB\VID_8087&PID_0029\5&1C8E1F0A&0&10",
        UpdateId = Guid.NewGuid().ToString("D"),
    };

    [Fact]
    public async Task PassesOnlyTheRequestFile_AndReadsTheProtectedResult()
    {
        var request = Request();
        var launcher = new FakeLauncher(args =>
        {
            // The "elevated process" sees a valid request file and answers in the protected folder.
            Assert.Equal(request, InstallProtocol.ReadRequest(args[2], out _));
            File.WriteAllText(InstallProtocol.ResultPath(_protected, request.RequestId),
                InstallProtocol.Serialize(new InstallResult { RequestId = request.RequestId, Outcome = InstallOutcome.Installed, Title = "T" }));
            return 0;
        });

        var result = await new ElevatedInstallerClient(launcher, NullLogger<ElevatedInstallerClient>.Instance, @"C:\x\DriverCompass.exe", _protected)
            .RunAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(InstallOutcome.Installed, result.Outcome);
        Assert.Equal([InstallProtocol.ElevatedSwitch, InstallProtocol.RequestSwitch], launcher.Calls[0].Take(2));
        Assert.False(File.Exists(launcher.Calls[0][2]));
    }

    [Fact]
    public async Task DeclinedUac_IsCancelled()
    {
        var launcher = new FakeLauncher(_ => throw new OperationCanceledException());
        var result = await new ElevatedInstallerClient(launcher, NullLogger<ElevatedInstallerClient>.Instance, @"C:\x\DriverCompass.exe", _protected)
            .RunAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(InstallOutcome.Cancelled, result.Outcome);
    }

    [Fact]
    public async Task MissingOrForeignResult_IsFailure()
    {
        var request = Request();
        var launcher = new FakeLauncher(_ =>
        {
            File.WriteAllText(InstallProtocol.ResultPath(_protected, request.RequestId),
                InstallProtocol.Serialize(new InstallResult { RequestId = Guid.NewGuid(), Outcome = InstallOutcome.Installed }));
            return 0;
        });

        var result = await new ElevatedInstallerClient(launcher, NullLogger<ElevatedInstallerClient>.Instance, @"C:\x\DriverCompass.exe", _protected)
            .RunAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(InstallOutcome.Failed, result.Outcome);
        Assert.Equal("NoResult:0", result.Message);
    }

    [Fact]
    public async Task InvalidRequest_NeverStartsAProcess()
    {
        var launcher = new FakeLauncher(_ => 0);
        var result = await new ElevatedInstallerClient(launcher, NullLogger<ElevatedInstallerClient>.Instance, @"C:\x\DriverCompass.exe", _protected)
            .RunAsync(Request() with { UpdateId = "x" }, TestContext.Current.CancellationToken);

        Assert.Equal(InstallOutcome.Failed, result.Outcome);
        Assert.Empty(launcher.Calls);
    }
}
