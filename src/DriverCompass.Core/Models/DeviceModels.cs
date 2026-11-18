namespace DriverCompass.Core.Models;

/// <summary>A Plug-and-Play device as reported by the scanner (Win32_PnPEntity + Win32_PnPSignedDriver).</summary>
public sealed record DeviceInfo
{
    /// <summary>PNP device instance ID, e.g. <c>PCI\VEN_10EC&amp;DEV_8125&amp;SUBSYS_...\4&amp;...</c>.</summary>
    public required string InstanceId { get; init; }

    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? Manufacturer { get; init; }

    /// <summary>Setup class name, e.g. <c>Display</c>, <c>Net</c>, <c>MEDIA</c>.</summary>
    public string? DeviceClass { get; init; }

    public string? ClassGuid { get; init; }
    public string? Service { get; init; }

    /// <summary>Most specific first, as delivered by Windows.</summary>
    public IReadOnlyList<string> HardwareIds { get; init; } = [];

    public IReadOnlyList<string> CompatibleIds { get; init; } = [];

    /// <summary>Configuration Manager error code (0 = working, 28 = no driver installed …).</summary>
    public int ConfigManagerErrorCode { get; init; }

    public bool? IsPresent { get; init; }

    /// <summary>Installed driver, <c>null</c> when Windows has no driver for the device.</summary>
    public DriverInfo? Driver { get; init; }
}

public sealed record DriverInfo
{
    public string? ProviderName { get; init; }
    public string? Version { get; init; }
    public DateOnly? Date { get; init; }

    /// <summary>INF file name, e.g. <c>display.inf</c> or <c>oem42.inf</c>.</summary>
    public string? InfName { get; init; }

    public string? Manufacturer { get; init; }
    public string? DeviceName { get; init; }
    public bool? IsSigned { get; init; }
    public string? Signer { get; init; }

    /// <summary>True if any driver information is available at all.</summary>
    public bool HasData =>
        !string.IsNullOrWhiteSpace(ProviderName) || !string.IsNullOrWhiteSpace(Version) || !string.IsNullOrWhiteSpace(InfName);
}

/// <summary>PC identity from Win32_ComputerSystem, Win32_BaseBoard and Win32_BIOS.</summary>
public sealed record SystemInfo
{
    public string? Manufacturer { get; init; }
    public string? Model { get; init; }
    public string? SystemFamily { get; init; }
    public string? SystemSku { get; init; }

    /// <summary>Serial number / service tag. Sensitive: never logged, excluded from exports by default.</summary>
    public string? SerialNumber { get; init; }

    public string? BaseBoardManufacturer { get; init; }
    public string? BaseBoardProduct { get; init; }
    public string? BaseBoardVersion { get; init; }
    public string? BiosVersion { get; init; }
    public string? OperatingSystem { get; init; }
    public string? OperatingSystemVersion { get; init; }

    public static SystemInfo Empty { get; } = new();
}

public sealed record ScanResult(
    SystemInfo System,
    IReadOnlyList<DeviceInfo> Devices,
    DateTimeOffset ScannedAt,
    string ScannerId);
