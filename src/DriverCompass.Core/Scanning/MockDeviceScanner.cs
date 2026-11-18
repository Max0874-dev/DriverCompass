using DriverCompass.Core.Abstractions;
using DriverCompass.Core.Models;

namespace DriverCompass.Core.Scanning;

public enum MockProfile
{
    /// <summary>Self-built desktop with an ASUS board (SMBIOS placeholders → mainboard fallback).</summary>
    WhiteBoxDesktop,

    /// <summary>Dell notebook with service tag.</summary>
    DellNotebook,
}

/// <summary>
/// Fake scanner with typical example devices. Used by the unit tests and by the app's demo mode
/// (<c>DriverCompass.App.exe --mock</c>), so the logic can be tried without real problem hardware.
/// </summary>
public sealed class MockDeviceScanner(MockProfile profile = MockProfile.WhiteBoxDesktop) : IDeviceScanner
{
    public string Id => "mock";

    public Task<ScanResult> ScanAsync(CancellationToken cancellationToken = default)
    {
        var system = profile switch
        {
            MockProfile.DellNotebook => new SystemInfo
            {
                Manufacturer = "Dell Inc.",
                Model = "Latitude 7440",
                SystemFamily = "Latitude",
                SystemSku = "0C08",
                SerialNumber = "ABC1234",
                BaseBoardManufacturer = "Dell Inc.",
                BaseBoardProduct = "0XYZ12",
                BiosVersion = "1.12.0",
                OperatingSystem = "Microsoft Windows 11 Pro",
                OperatingSystemVersion = "10.0.26100",
                OperatingSystemDisplayVersion = "24H2",
            },
            _ => new SystemInfo
            {
                Manufacturer = "System manufacturer",
                Model = "System Product Name",
                SystemSku = "SKU",
                SerialNumber = "System Serial Number",
                BaseBoardManufacturer = "ASUSTeK COMPUTER INC.",
                BaseBoardProduct = "ROG STRIX B550-F GAMING",
                BaseBoardVersion = "Rev X.0x",
                BiosVersion = "3611",
                OperatingSystem = "Microsoft Windows 11 Pro",
                OperatingSystemVersion = "10.0.26100",
                OperatingSystemDisplayVersion = "24H2",
            },
        };

        return Task.FromResult(new ScanResult(system, Devices, DateTimeOffset.Now, Id));
    }

    public static IReadOnlyList<DeviceInfo> Devices { get; } =
    [
        // --- Missing drivers (code 28) ---
        new DeviceInfo
        {
            InstanceId = @"PCI\VEN_10EC&DEV_8125&SUBSYS_86791043&REV_05\01000000684CE00000",
            Name = "Ethernet-Controller",
            DeviceClass = "Net",
            ConfigManagerErrorCode = 28,
            HardwareIds = [@"PCI\VEN_10EC&DEV_8125&SUBSYS_86791043&REV_05", @"PCI\VEN_10EC&DEV_8125&SUBSYS_86791043", @"PCI\VEN_10EC&DEV_8125&CC_020000", @"PCI\VEN_10EC&DEV_8125&CC_0200"],
            CompatibleIds = [@"PCI\VEN_10EC&DEV_8125&REV_05", @"PCI\VEN_10EC&DEV_8125", @"PCI\VEN_10EC&CC_020000", @"PCI\CC_020000", @"PCI\CC_0200"],
        },
        new DeviceInfo
        {
            InstanceId = @"PCI\VEN_1022&DEV_790B&SUBSYS_87C01043&REV_61\3&11583659&0&A0",
            Name = "SM-Bus-Controller",
            DeviceClass = null,
            ConfigManagerErrorCode = 28,
            HardwareIds = [@"PCI\VEN_1022&DEV_790B&SUBSYS_87C01043&REV_61", @"PCI\VEN_1022&DEV_790B&SUBSYS_87C01043"],
            CompatibleIds = [@"PCI\VEN_1022&DEV_790B&REV_61", @"PCI\CC_0C0500"],
        },
        new DeviceInfo
        {
            InstanceId = @"USB\VID_1A86&PID_7523\5&2D3B9E1F&0&3",
            Name = "USB-Serial",
            DeviceClass = null,
            ConfigManagerErrorCode = 28,
            HardwareIds = [@"USB\VID_1A86&PID_7523&REV_0264", @"USB\VID_1A86&PID_7523"],
            CompatibleIds = [@"USB\Class_FF&SubClass_01&Prot_02", @"USB\Class_FF"],
        },

        // --- Device error (code 43) ---
        new DeviceInfo
        {
            InstanceId = @"USB\VID_8087&PID_0029\5&1C8E1F0A&0&10",
            Name = "Intel(R) Wireless Bluetooth(R)",
            Manufacturer = "Intel Corporation",
            DeviceClass = "Bluetooth",
            ConfigManagerErrorCode = 43,
            HardwareIds = [@"USB\VID_8087&PID_0029&REV_0001", @"USB\VID_8087&PID_0029"],
            CompatibleIds = [@"USB\Class_E0&SubClass_01&Prot_01", @"USB\Class_E0"],
            Driver = new DriverInfo { ProviderName = "Intel Corporation", Version = "22.240.0.2", Date = new DateOnly(2023, 6, 12), InfName = "oem31.inf", IsSigned = true },
        },

        // --- Generic Microsoft drivers ---
        new DeviceInfo
        {
            InstanceId = @"PCI\VEN_10DE&DEV_2489&SUBSYS_88221043&REV_A1\4&2E0F4F6A&0&0019",
            Name = "Microsoft Basic Display Adapter",
            Manufacturer = "(Standardgrafikkartentypen)",
            DeviceClass = "Display",
            HardwareIds = [@"PCI\VEN_10DE&DEV_2489&SUBSYS_88221043&REV_A1", @"PCI\VEN_10DE&DEV_2489&SUBSYS_88221043"],
            CompatibleIds = [@"PCI\VEN_10DE&DEV_2489&REV_A1", @"PCI\VEN_10DE&CC_030000", @"PCI\CC_030000"],
            Driver = new DriverInfo { ProviderName = "Microsoft", Version = "10.0.26100.1", Date = new DateOnly(2006, 6, 21), InfName = "display.inf", DeviceName = "Microsoft Basic Display Adapter", IsSigned = true, Signer = "Microsoft Windows" },
        },
        new DeviceInfo
        {
            InstanceId = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_1220&SUBSYS_10438797&REV_1001\5&2B3C4D5E&0&0001",
            Name = "High Definition Audio-Gerät",
            Manufacturer = "Microsoft",
            DeviceClass = "MEDIA",
            HardwareIds = [@"HDAUDIO\FUNC_01&VEN_10EC&DEV_1220&SUBSYS_10438797&REV_1001", @"HDAUDIO\FUNC_01&VEN_10EC&DEV_1220&SUBSYS_10438797"],
            CompatibleIds = [@"HDAUDIO\FUNC_01&VEN_10EC&DEV_1220&REV_1001", @"HDAUDIO\FUNC_01&VEN_10EC", @"HDAUDIO\FUNC_01"],
            Driver = new DriverInfo { ProviderName = "Microsoft", Version = "10.0.26100.1", Date = new DateOnly(2006, 6, 21), InfName = "hdaudio.inf", IsSigned = true, Signer = "Microsoft Windows" },
        },

        // --- Disabled (code 22) ---
        new DeviceInfo
        {
            InstanceId = @"USB\VID_04F2&PID_B6D9&MI_00\6&1A2B3C4D&0&0000",
            Name = "Integrated Webcam",
            Manufacturer = "Microsoft",
            DeviceClass = "Camera",
            ConfigManagerErrorCode = 22,
            HardwareIds = [@"USB\VID_04F2&PID_B6D9&REV_0012&MI_00", @"USB\VID_04F2&PID_B6D9&MI_00"],
            Driver = new DriverInfo { ProviderName = "Microsoft", Version = "10.0.26100.1", Date = new DateOnly(2006, 6, 21), InfName = "usbvideo.inf", IsSigned = true },
        },

        // --- OK ---
        new DeviceInfo
        {
            InstanceId = @"PCI\VEN_8086&DEV_2725&SUBSYS_00248086&REV_1A\4&3A1B2C3D&0&00E4",
            Name = "Intel(R) Wi-Fi 6E AX210 160MHz",
            Manufacturer = "Intel Corporation",
            DeviceClass = "Net",
            HardwareIds = [@"PCI\VEN_8086&DEV_2725&SUBSYS_00248086&REV_1A", @"PCI\VEN_8086&DEV_2725&SUBSYS_00248086"],
            Driver = new DriverInfo { ProviderName = "Intel", Version = "23.60.1.2", Date = new DateOnly(2024, 6, 3), InfName = "oem45.inf", IsSigned = true, Signer = "Microsoft Windows Hardware Compatibility Publisher" },
        },
        new DeviceInfo
        {
            InstanceId = @"PCI\VEN_144D&DEV_A80A&SUBSYS_A801144D&REV_00\4&1F2E3D4C&0&0009",
            Name = "Standard-NVM-Express-Controller",
            Manufacturer = "Standard-NVM-Express-Controller",
            DeviceClass = "SCSIAdapter",
            HardwareIds = [@"PCI\VEN_144D&DEV_A80A&SUBSYS_A801144D&REV_00", @"PCI\VEN_144D&DEV_A80A&SUBSYS_A801144D"],
            Driver = new DriverInfo { ProviderName = "Microsoft", Version = "10.0.26100.1", Date = new DateOnly(2006, 6, 21), InfName = "stornvme.inf", IsSigned = true },
        },
        new DeviceInfo
        {
            InstanceId = @"USB\VID_046D&PID_C52B\6&2A9F1B&0&2",
            Name = "USB-Verbundgerät",
            Manufacturer = "(Standard-USB-Hostcontroller)",
            DeviceClass = "USB",
            HardwareIds = [@"USB\VID_046D&PID_C52B&REV_1211", @"USB\VID_046D&PID_C52B"],
            CompatibleIds = [@"USB\DevClass_00&SubClass_00&Prot_00", @"USB\COMPOSITE"],
            Driver = new DriverInfo { ProviderName = "Microsoft", Version = "10.0.26100.1", Date = new DateOnly(2006, 6, 21), InfName = "usb.inf", IsSigned = true },
        },
        new DeviceInfo
        {
            InstanceId = @"PCI\VEN_1022&DEV_1483&SUBSYS_14531022&REV_00\3&11583659&0&09",
            Name = "PCI Express Root Port",
            Manufacturer = "Advanced Micro Devices, Inc.",
            DeviceClass = "System",
            HardwareIds = [@"PCI\VEN_1022&DEV_1483&SUBSYS_14531022&REV_00", @"PCI\VEN_1022&DEV_1483&SUBSYS_14531022"],
            Driver = new DriverInfo { ProviderName = "Advanced Micro Devices, Inc.", Version = "1.2.0.126", Date = new DateOnly(2024, 3, 1), InfName = "oem12.inf", IsSigned = true },
        },
        new DeviceInfo
        {
            InstanceId = @"ACPI\AMDI0010\3",
            Name = "AMD I2C Controller",
            Manufacturer = "Advanced Micro Devices, Inc",
            DeviceClass = "System",
            HardwareIds = [@"ACPI\VEN_AMDI&DEV_0010", @"ACPI\AMDI0010", @"*AMDI0010"],
            Driver = new DriverInfo { ProviderName = "Advanced Micro Devices, Inc", Version = "1.2.0.124", Date = new DateOnly(2023, 11, 2), InfName = "oem14.inf", IsSigned = true },
        },

        // --- System/software devices (hidden by default) ---
        new DeviceInfo
        {
            InstanceId = @"ROOT\BasicRender\0000",
            Name = "Microsoft Basic Render Driver",
            DeviceClass = "System",
            HardwareIds = [@"ROOT\BasicRender"],
            Driver = new DriverInfo { ProviderName = "Microsoft", Version = "10.0.26100.1", InfName = "basicrender.inf" },
        },
        new DeviceInfo
        {
            InstanceId = @"SWD\MMDEVAPI\{0.0.0.00000000}.{5c1b5f2a-1c2d-4e3f-9a8b-7c6d5e4f3a2b}",
            Name = "Lautsprecher (Realtek(R) Audio)",
            DeviceClass = "AudioEndpoint",
            HardwareIds = [@"MMDEVAPI\AudioEndpoints"],
            Driver = new DriverInfo { ProviderName = "Microsoft", Version = "10.0.26100.1", InfName = "audioendpoint.inf" },
        },
    ];
}
