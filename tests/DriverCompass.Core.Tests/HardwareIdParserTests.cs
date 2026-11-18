using DriverCompass.Core.Models;
using DriverCompass.Core.Parsing;

namespace DriverCompass.Core.Tests;

public class HardwareIdParserTests
{
    [Fact]
    public void Pci_full_id_is_split_into_all_parts()
    {
        var id = HardwareIdParser.Parse(@"PCI\VEN_10DE&DEV_2489&SUBSYS_88221043&REV_A1");

        Assert.Equal(HardwareBus.Pci, id.Bus);
        Assert.Equal("PCI", id.Enumerator);
        Assert.Equal(VendorIdKind.Pci, id.VendorKind);
        Assert.Equal("10DE", id.VendorId);
        Assert.Equal("2489", id.DeviceId);
        Assert.Equal("8822", id.SubsystemDeviceId);
        Assert.Equal("1043", id.SubsystemVendorId);
        Assert.Equal("A1", id.Revision);
    }

    [Fact]
    public void Pci_lowercase_is_normalised()
    {
        var id = HardwareIdParser.Parse(@"pci\ven_10ec&dev_8125");
        Assert.Equal("10EC", id.VendorId);
        Assert.Equal("8125", id.DeviceId);
        Assert.Equal(@"pci\ven_10ec&dev_8125", id.Raw);
    }

    [Fact]
    public void Pci_class_code_only()
    {
        var id = HardwareIdParser.Parse(@"PCI\CC_030000");
        Assert.Equal(HardwareBus.Pci, id.Bus);
        Assert.False(id.HasVendor);
        Assert.Equal("030000", id.ClassCode);
    }

    [Theory]
    [InlineData(@"USB\VID_046D&PID_C52B&REV_1211&MI_00", "046D", "C52B", "1211", "00")]
    [InlineData(@"USB\VID_1A86&PID_7523", "1A86", "7523", null, null)]
    public void Usb_ids(string raw, string vid, string pid, string? rev, string? mi)
    {
        var id = HardwareIdParser.Parse(raw);
        Assert.Equal(HardwareBus.Usb, id.Bus);
        Assert.Equal(VendorIdKind.Usb, id.VendorKind);
        Assert.Equal(vid, id.VendorId);
        Assert.Equal(pid, id.DeviceId);
        Assert.Equal(rev, id.Revision);
        Assert.Equal(mi, id.Interface);
    }

    [Fact]
    public void Usb_class_id_has_no_vendor()
    {
        var id = HardwareIdParser.Parse(@"USB\Class_E0&SubClass_01&Prot_01");
        Assert.False(id.HasVendor);
        Assert.Equal("E0", id.ClassCode);
    }

    [Fact]
    public void HdAudio_codec_uses_pci_vendor_namespace_and_vendor_first_subsys()
    {
        var id = HardwareIdParser.Parse(@"HDAUDIO\FUNC_01&VEN_10EC&DEV_1220&SUBSYS_10438797&REV_1001");
        Assert.Equal(HardwareBus.HdAudio, id.Bus);
        Assert.Equal(VendorIdKind.Pci, id.VendorKind);
        Assert.Equal("10EC", id.VendorId);
        Assert.Equal("1220", id.DeviceId);
        Assert.Equal("1043", id.SubsystemVendorId);
        Assert.Equal("8797", id.SubsystemDeviceId);
        Assert.Equal("01", id.Interface);
    }

    [Fact]
    public void IntelAudio_is_treated_like_hdaudio()
    {
        var id = HardwareIdParser.Parse(@"INTELAUDIO\FUNC_01&VEN_10EC&DEV_0236&SUBSYS_10280A20");
        Assert.Equal(HardwareBus.HdAudio, id.Bus);
        Assert.Equal("10EC", id.VendorId);
    }

    [Theory]
    [InlineData(@"ACPI\VEN_INT&DEV_33A0", "INT", "33A0")]
    [InlineData(@"ACPI\INT33A0", "INT", "33A0")]
    [InlineData(@"ACPI\INTC1043", "INTC", "1043")]
    [InlineData(@"ACPI\AMDI0010", "AMDI", "0010")]
    [InlineData(@"ACPI\PNP0A08", "PNP", "0A08")]
    [InlineData(@"*PNP0A08", "PNP", "0A08")]
    public void Acpi_ids(string raw, string vendor, string device)
    {
        var id = HardwareIdParser.Parse(raw);
        Assert.Equal(HardwareBus.Acpi, id.Bus);
        Assert.Equal(VendorIdKind.Acpi, id.VendorKind);
        Assert.Equal(vendor, id.VendorId);
        Assert.Equal(device, id.DeviceId);
    }

    [Fact]
    public void Acpi_processor_id_has_no_vendor()
    {
        var id = HardwareIdParser.Parse(@"ACPI\GenuineIntel_-_Intel64_Family_6_Model_158");
        Assert.Equal(HardwareBus.Acpi, id.Bus);
        Assert.False(id.HasVendor);
    }

    [Fact]
    public void Hid_usb_id()
    {
        var id = HardwareIdParser.Parse(@"HID\VID_046D&PID_C52B&MI_00&Col01");
        Assert.Equal(HardwareBus.Hid, id.Bus);
        Assert.Equal("046D", id.VendorId);
        Assert.Equal("C52B", id.DeviceId);
        Assert.Equal("00", id.Interface);
    }

    [Fact]
    public void Bluetooth_hid_with_usb_vendor_source()
    {
        var id = HardwareIdParser.Parse(@"HID\{00001124-0000-1000-8000-00805f9b34fb}_VID&0002046d_PID&b023");
        Assert.Equal(VendorIdKind.Usb, id.VendorKind);
        Assert.Equal("046D", id.VendorId);
        Assert.Equal("B023", id.DeviceId);
    }

    [Fact]
    public void Bluetooth_sig_vendor_source_is_not_mapped()
    {
        var id = HardwareIdParser.Parse(@"BTHENUM\{0000110b-0000-1000-8000-00805f9b34fb}_VID&0001004c_PID&2002");
        Assert.Equal(HardwareBus.Bluetooth, id.Bus);
        Assert.False(id.HasVendor);
    }

    [Fact]
    public void Monitor_pnp_id()
    {
        var id = HardwareIdParser.Parse(@"MONITOR\DELA0B1");
        Assert.Equal(HardwareBus.Display, id.Bus);
        Assert.Equal(VendorIdKind.Pnp, id.VendorKind);
        Assert.Equal("DEL", id.VendorId);
        Assert.Equal("A0B1", id.DeviceId);
    }

    [Theory]
    [InlineData(@"ROOT\BasicRender", HardwareBus.Root)]
    [InlineData(@"HTREE\ROOT\0", HardwareBus.Root)]
    [InlineData(@"SWD\MMDEVAPI\{0.0.0.00000000}", HardwareBus.Software)]
    [InlineData(@"SCSI\DiskSamsung_SSD_980_PRO_1TB", HardwareBus.Storage)]
    [InlineData(@"FOO\BAR", HardwareBus.Other)]
    public void Other_enumerators(string raw, HardwareBus bus)
    {
        var id = HardwareIdParser.Parse(raw);
        Assert.Equal(bus, id.Bus);
        Assert.False(id.HasVendor);
    }

    [Theory]
    [InlineData("GenCdRom")]
    [InlineData(@"PCI\VEN_ZZZZ&DEV_12")]
    [InlineData(@"PCI\")]
    [InlineData(@"USB\VID_&PID_")]
    public void Malformed_ids_do_not_throw_and_have_no_vendor(string raw)
    {
        var id = HardwareIdParser.Parse(raw);
        Assert.False(id.HasVendor);
    }

    [Fact]
    public void Instance_id_suffix_is_ignored()
    {
        var id = HardwareIdParser.Parse(@"PCI\VEN_10EC&DEV_8125&SUBSYS_86791043&REV_05\01000000684CE00000");
        Assert.Equal("10EC", id.VendorId);
        Assert.Equal("05", id.Revision);
    }

    [Fact]
    public void ParseAll_skips_empty_entries_and_keeps_order()
    {
        var ids = HardwareIdParser.ParseAll([@"USB\VID_046D&PID_C52B", "", "  ", @"USB\Class_03"]);
        Assert.Equal(2, ids.Count);
        Assert.Equal("046D", ids[0].VendorId);
    }
}
