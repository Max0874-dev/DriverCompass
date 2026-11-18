using DriverCompass.Core.Abstractions;
using DriverCompass.Core.Evaluation;
using DriverCompass.Core.Models;

namespace DriverCompass.Core.Tests;

public class StatusEvaluatorTests
{
    private readonly DeviceStatusEvaluator _evaluator = new(new FakeNames());
    private readonly IVendorCatalog _catalog = TestCatalog.Create();

    private static DeviceInfo Device(string hwid, int code = 0, DriverInfo? driver = null, string? cls = null, string? name = null, string? instance = null) => new()
    {
        InstanceId = instance ?? hwid + @"\1",
        Name = name,
        DeviceClass = cls,
        HardwareIds = [hwid],
        ConfigManagerErrorCode = code,
        Driver = driver,
    };

    [Fact]
    public void Code_28_is_missing_driver()
    {
        var a = _evaluator.Evaluate(Device(@"PCI\VEN_10EC&DEV_8125", code: 28, name: "Ethernet-Controller"), _catalog);
        Assert.Equal(DeviceStatus.MissingDriver, a.Status);
        Assert.Equal(StatusReason.NoDriverInstalled, a.Reason);
        Assert.True(a.NeedsAttention);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(31)]
    [InlineData(43)]
    public void Other_error_codes_are_errors(int code)
    {
        var a = _evaluator.Evaluate(Device(@"USB\VID_8087&PID_0029", code: code), _catalog);
        Assert.Equal(DeviceStatus.Error, a.Status);
        Assert.Equal(StatusReason.DeviceError, a.Reason);
    }

    [Fact]
    public void Code_22_is_disabled_not_a_problem()
    {
        var a = _evaluator.Evaluate(Device(@"USB\VID_04F2&PID_B6D9", code: 22), _catalog);
        Assert.Equal(DeviceStatus.Disabled, a.Status);
        Assert.False(a.NeedsAttention);
    }

    [Fact]
    public void Code_45_not_present_is_hidden()
    {
        var a = _evaluator.Evaluate(Device(@"USB\VID_046D&PID_C52B", code: 45), _catalog);
        Assert.Equal(StatusReason.NotPresent, a.Reason);
        Assert.True(a.IsSystemDevice);
    }

    [Fact]
    public void Basic_display_adapter_is_generic()
    {
        var driver = new DriverInfo { ProviderName = "Microsoft", InfName = "display.inf", Version = "10.0.1" };
        var a = _evaluator.Evaluate(Device(@"PCI\VEN_10DE&DEV_2489", driver: driver, cls: "Display", name: "Microsoft Basic Display Adapter"), _catalog);
        Assert.Equal(DeviceStatus.GenericDriver, a.Status);
        Assert.Equal(GenericDriverCategory.Display, a.GenericCategory);
        Assert.Equal("NVIDIA", a.VendorName);
    }

    [Fact]
    public void Generic_bluetooth_radio_on_usb_is_generic()
    {
        var driver = new DriverInfo { ProviderName = "Microsoft", InfName = "bth.inf" };
        var a = _evaluator.Evaluate(Device(@"USB\VID_8087&PID_0029", driver: driver, cls: "Bluetooth", instance: @"USB\VID_8087&PID_0029\5&1"), _catalog);
        Assert.Equal(DeviceStatus.GenericDriver, a.Status);
    }

    [Fact]
    public void Paired_bluetooth_device_with_bth_inf_is_ok()
    {
        // Regression: paired devices (BTHENUM\DEV_…) also use bth.inf but are not radios.
        var driver = new DriverInfo { ProviderName = "Microsoft", InfName = "bth.inf" };
        var a = _evaluator.Evaluate(Device(@"BTHENUM\DEV_C0288D3F06E8", driver: driver, cls: "Bluetooth", instance: @"BTHENUM\DEV_C0288D3F06E8\8&1"), _catalog);
        Assert.Equal(DeviceStatus.Ok, a.Status);
    }

    [Fact]
    public void Generic_rule_requires_microsoft_provider()
    {
        // A vendor INF that happens to be called display.inf is not generic.
        var driver = new DriverInfo { ProviderName = "NVIDIA", InfName = "display.inf" };
        var a = _evaluator.Evaluate(Device(@"PCI\VEN_10DE&DEV_2489", driver: driver), _catalog);
        Assert.Equal(DeviceStatus.Ok, a.Status);
        Assert.Equal(StatusReason.VendorDriver, a.Reason);
    }

    [Fact]
    public void Other_microsoft_inbox_drivers_are_ok()
    {
        var driver = new DriverInfo { ProviderName = "Microsoft", InfName = "stornvme.inf" };
        var a = _evaluator.Evaluate(Device(@"PCI\VEN_144D&DEV_A80A", driver: driver), _catalog);
        Assert.Equal(DeviceStatus.Ok, a.Status);
        Assert.Equal(StatusReason.InboxDriver, a.Reason);
    }

    [Fact]
    public void Device_without_driver_info_and_no_error_is_ok()
    {
        var a = _evaluator.Evaluate(Device(@"ACPI\PNP0C0C"), _catalog);
        Assert.Equal(DeviceStatus.Ok, a.Status);
        Assert.Equal(StatusReason.NoDriverInfo, a.Reason);
    }

    [Theory]
    [InlineData(@"ROOT\BasicRender\0000", null, true)]
    [InlineData(@"SWD\MMDEVAPI\{0.0.0.00000000}", null, true)]
    [InlineData(@"STORAGE\VOLUME\{abc}", null, true)]
    [InlineData(@"USB\VID_046D&PID_C52B\1", "SoftwareDevice", true)]
    [InlineData(@"PCI\VEN_8086&DEV_A323\3", "System", false)]
    public void System_devices_are_detected(string instance, string? cls, bool expected)
    {
        var a = _evaluator.Evaluate(Device(@"PCI\VEN_8086&DEV_A323", cls: cls, instance: instance), _catalog);
        Assert.Equal(expected, a.IsSystemDevice);
    }

    [Fact]
    public void Chip_vendor_found_via_compatible_id_when_hardware_ids_have_none()
    {
        var device = new DeviceInfo
        {
            InstanceId = @"FOO\BAR\1",
            HardwareIds = [@"FOO\BAR"],
            CompatibleIds = [@"PCI\VEN_8086&CC_0C05"],
        };

        var a = _evaluator.Evaluate(device, _catalog);
        Assert.Equal("intel", a.ChipVendor?.Id);
    }

    [Fact]
    public void Unknown_device_gets_detected_name_from_pci_ids()
    {
        var a = _evaluator.Evaluate(Device(@"PCI\VEN_10EC&DEV_8125", code: 28, name: "Ethernet-Controller"), _catalog);
        Assert.Equal("RTL8125 2.5GbE Controller", a.DetectedName);
        Assert.Equal("Realtek RTL8125 2.5GbE Controller", a.DisplayName);
    }

    [Fact]
    public void Ok_device_keeps_windows_name()
    {
        var driver = new DriverInfo { ProviderName = "Realtek", Version = "1.0" };
        var a = _evaluator.Evaluate(Device(@"PCI\VEN_10EC&DEV_8125", driver: driver, name: "Realtek Gaming 2.5GbE"), _catalog);
        Assert.Equal("Realtek Gaming 2.5GbE", a.DisplayName);
    }

    [Fact]
    public void HdAudio_codec_does_not_get_a_pci_device_name()
    {
        var driver = new DriverInfo { ProviderName = "Microsoft", InfName = "hdaudio.inf" };
        var a = _evaluator.Evaluate(Device(@"HDAUDIO\FUNC_01&VEN_10EC&DEV_8125", driver: driver), _catalog);
        Assert.Null(a.DetectedName);
        Assert.Equal(DeviceStatus.GenericDriver, a.Status);
    }

    [Fact]
    public void Windows_standard_manufacturer_is_not_used_as_vendor()
    {
        var device = Device(@"USB\VID_9999&PID_0001") with { Manufacturer = "(Standard-USB-Hostcontroller)" };
        var a = _evaluator.Evaluate(device, _catalog);
        Assert.Null(a.VendorName);
    }

    private sealed class FakeNames : IDeviceNameLookup
    {
        public string? GetVendorName(VendorIdKind kind, string vendorId) =>
            kind == VendorIdKind.Pci && vendorId == "10EC" ? "Realtek Semiconductor Co., Ltd." : null;

        public string? GetDeviceName(VendorIdKind kind, string vendorId, string deviceId) =>
            vendorId == "10EC" && deviceId == "8125" ? "RTL8125 2.5GbE Controller" : null;
    }
}
