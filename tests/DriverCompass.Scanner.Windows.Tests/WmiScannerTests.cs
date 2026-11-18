using System.Management;

namespace DriverCompass.Scanner.Windows.Tests;

public class WmiScannerTests
{
    private static Dictionary<string, object?> Row(params (string Key, object? Value)[] values) =>
        values.ToDictionary(v => v.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);

    private sealed class FakeWmi(Func<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> answer) : IWmiQuery
    {
        public List<string> Queries { get; } = [];

        public IReadOnlyList<IReadOnlyDictionary<string, object?>> Query(string wql)
        {
            Queries.Add(wql);
            return answer(wql);
        }
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> Answer(string wql)
    {
        if (wql.Contains("Win32_PnPEntity", StringComparison.Ordinal))
        {
            return
            [
                Row(("PNPDeviceID", @"PCI\VEN_10EC&DEV_8125\1"), ("Name", "Ethernet-Controller"), ("ConfigManagerErrorCode", 28u),
                    ("HardwareID", new[] { @"PCI\VEN_10EC&DEV_8125&SUBSYS_86791043", "" }), ("CompatibleID", new[] { @"PCI\CC_020000" })),
                Row(("PNPDeviceID", @"PCI\VEN_10DE&DEV_2489\2"), ("Name", "Microsoft Basic Display Adapter"), ("PNPClass", "Display"),
                    ("ConfigManagerErrorCode", 0u), ("HardwareID", new[] { @"PCI\VEN_10DE&DEV_2489" }), ("Present", true)),
                Row(("Name", "row without id")),
            ];
        }

        if (wql.Contains("Win32_PnPSignedDriver", StringComparison.Ordinal))
        {
            return
            [
                Row(("DeviceID", @"pci\ven_10de&dev_2489\2"), ("DriverProviderName", "Microsoft"), ("DriverVersion", "10.0.26100.1"),
                    ("DriverDate", "20060621000000.******+000"), ("InfName", "display.inf"), ("DeviceClass", "DISPLAY"), ("IsSigned", true)),
                Row(("DeviceID", @"PCI\VEN_10EC&DEV_8125\1"), ("DriverProviderName", null), ("DriverVersion", null)),
            ];
        }

        if (wql.Contains("Win32_ComputerSystem", StringComparison.Ordinal))
        {
            return [Row(("Manufacturer", " Dell Inc. "), ("Model", "Latitude 7440"), ("SystemSKUNumber", "0C08"))];
        }

        if (wql.Contains("Win32_BaseBoard", StringComparison.Ordinal))
        {
            return [Row(("Manufacturer", "Dell Inc."), ("Product", "0XYZ12"))];
        }

        if (wql.Contains("Win32_BIOS", StringComparison.Ordinal))
        {
            return [Row(("SerialNumber", "ABC1234"), ("SMBIOSBIOSVersion", "1.12.0"))];
        }

        return [];
    }

    [Fact]
    public async Task Entities_and_drivers_are_joined()
    {
        var scanner = new WmiDeviceScanner(new FakeWmi(Answer));
        var result = await scanner.ScanAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Devices.Count);

        var nic = result.Devices[0];
        Assert.Equal(28, nic.ConfigManagerErrorCode);
        Assert.Equal([@"PCI\VEN_10EC&DEV_8125&SUBSYS_86791043"], nic.HardwareIds);
        Assert.Null(nic.Driver); // driver row without data is dropped

        var gpu = result.Devices[1];
        Assert.Equal("Display", gpu.DeviceClass);
        Assert.Equal("display.inf", gpu.Driver?.InfName);
        Assert.Equal(new DateOnly(2006, 6, 21), gpu.Driver?.Date);
        Assert.True(gpu.IsPresent);
    }

    [Fact]
    public async Task System_info_is_mapped_and_trimmed()
    {
        var result = await new WmiDeviceScanner(new FakeWmi(Answer)).ScanAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Dell Inc.", result.System.Manufacturer);
        Assert.Equal("Latitude 7440", result.System.Model);
        Assert.Equal("0C08", result.System.SystemSku);
        Assert.Equal("ABC1234", result.System.SerialNumber);
        Assert.Equal("0XYZ12", result.System.BaseBoardProduct);
        Assert.Equal("wmi", result.ScannerId);
    }

    [Fact]
    public async Task Failing_entity_query_falls_back_to_select_star()
    {
        var wmi = new FakeWmi(wql => wql == WmiDeviceScanner.EntityQuery ? throw new ManagementException("Invalid query") : Answer(wql));
        var result = await new WmiDeviceScanner(wmi).ScanAsync(TestContext.Current.CancellationToken);

        Assert.Contains(WmiDeviceScanner.EntityFallbackQuery, wmi.Queries);
        Assert.Equal(2, result.Devices.Count);
    }

    [Fact]
    public async Task Failing_secondary_queries_do_not_break_the_scan()
    {
        var wmi = new FakeWmi(wql => wql.Contains("Win32_PnPEntity", StringComparison.Ordinal) ? Answer(wql) : throw new ManagementException("Access denied"));
        var result = await new WmiDeviceScanner(wmi).ScanAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Devices.Count);
        Assert.Null(result.System.Manufacturer);
    }

    [Theory]
    [InlineData("20230612000000.******+000", 2023, 6, 12)]
    [InlineData("20060621", 2006, 6, 21)]
    public void Cim_dates(string raw, int y, int m, int d) =>
        Assert.Equal(new DateOnly(y, m, d), WmiMapper.CimDate(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    public void Bad_cim_dates_are_null(string? raw) => Assert.Null(WmiMapper.CimDate(raw));

    [Fact]
    [Trait("Category", "RealHardware")]
    public async Task Real_wmi_scan_returns_devices()
    {
        // Runs against this machine's WMI (no admin needed). Skipped on non-Windows by the TFM.
        var result = await new WmiDeviceScanner().ScanAsync(TestContext.Current.CancellationToken);
        Assert.NotEmpty(result.Devices);
        Assert.All(result.Devices, d => Assert.False(string.IsNullOrWhiteSpace(d.InstanceId)));
    }
}
