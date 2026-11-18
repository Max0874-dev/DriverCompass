using DriverCompass.Core.Catalog;
using DriverCompass.Core.Models;

namespace DriverCompass.Core.Tests;

/// <summary>Small in-code catalog so the Core tests don't depend on data/vendors.json.</summary>
internal static class TestCatalog
{
    public static VendorCatalog Create() => new(
        1,
        "2026.1.1.1",
        [
            new ChipVendor
            {
                Id = "nvidia", Name = "NVIDIA", PciIds = ["10DE"], AllowedDomains = ["nvidia.com"],
                DriverPages = [new DriverPage { Url = new Uri("https://www.nvidia.com/en-us/drivers/") }],
            },
            new ChipVendor
            {
                Id = "realtek", Name = "Realtek", PciIds = ["10EC"], UsbIds = ["0BDA"], AcpiIds = ["RTL"], AllowedDomains = ["realtek.com"],
                DriverPages =
                [
                    new DriverPage { Url = new Uri("https://www.realtek.com/audio"), DeviceClasses = ["MEDIA"] },
                    new DriverPage { Url = new Uri("https://www.realtek.com/Download/Overview") },
                ],
            },
            new ChipVendor
            {
                Id = "intel", Name = "Intel", PciIds = ["8086"], UsbIds = ["8087"], AcpiIds = ["INT", "INTC"], AllowedDomains = ["intel.com"],
                DriverPages = [new DriverPage { Url = new Uri("https://www.intel.com/content/www/us/en/support/detect.html") }],
            },
            new ChipVendor
            {
                Id = "amd", Name = "AMD", PciIds = ["1002", "1022"], AcpiIds = ["AMDI"], AllowedDomains = ["amd.com"],
                DriverPages = [new DriverPage { Url = new Uri("https://www.amd.com/en/support/download/drivers.html") }],
            },
            new ChipVendor
            {
                Id = "evil", Name = "Evil", UsbIds = ["DEAD"], AllowedDomains = ["evil.example"],
                DriverPages = [new DriverPage { Url = new Uri("https://attacker.example/drivers") }],
            },
        ],
        [
            new OemVendor
            {
                Id = "dell", Name = "Dell", MatchPatterns = ["^Dell"], AllowedDomains = ["dell.com"],
                SupportUrls =
                [
                    new SupportUrlTemplate("https://www.dell.com/support/home/product-support/servicetag/{serial}/drivers", ["serial"]),
                    new SupportUrlTemplate("https://www.dell.com/support/search?q={model}", ["model"]),
                    new SupportUrlTemplate("https://www.dell.com/support/home/drivers", []),
                ],
            },
            new OemVendor
            {
                Id = "asus", Name = "ASUS", MatchPatterns = ["^ASUSTeK", "^ASUS"], AllowedDomains = ["asus.com"],
                SupportUrls =
                [
                    new SupportUrlTemplate("https://www.asus.com/search?q={board}", ["board"]),
                    new SupportUrlTemplate("https://www.asus.com/support/download-center/", []),
                ],
            },
        ],
        [
            new GenericDriverRule { Id = "basic-display", InfName = "display.inf", Category = GenericDriverCategory.Display },
            new GenericDriverRule { Id = "hd-audio", InfName = "hdaudio.inf", Category = GenericDriverCategory.Audio },
            new GenericDriverRule
            {
                Id = "generic-bluetooth", InfName = "bth.inf", DeviceClass = "Bluetooth", Enumerators = ["USB", "PCI"],
                Category = GenericDriverCategory.Bluetooth,
            },
        ]);

    public static SystemInfo Dell(string? serial = "ABC1234") => new()
    {
        Manufacturer = "Dell Inc.",
        Model = "Latitude 7440",
        SerialNumber = serial,
        BaseBoardManufacturer = "Dell Inc.",
        BaseBoardProduct = "0XYZ12",
    };

    public static SystemInfo WhiteBoxAsus() => new()
    {
        Manufacturer = "System manufacturer",
        Model = "System Product Name",
        SerialNumber = "System Serial Number",
        BaseBoardManufacturer = "ASUSTeK COMPUTER INC.",
        BaseBoardProduct = "ROG STRIX B550-F GAMING",
    };
}
