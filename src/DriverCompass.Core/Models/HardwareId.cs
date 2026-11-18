namespace DriverCompass.Core.Models;

/// <summary>Bus / enumerator a hardware ID belongs to.</summary>
public enum HardwareBus
{
    Unknown = 0,
    Pci,
    Usb,
    HdAudio,
    Acpi,
    Hid,
    Bluetooth,
    Display,
    Storage,
    Root,
    Software,
    Other,
}

/// <summary>Which ID registry the <see cref="HardwareId.VendorId"/> belongs to.</summary>
public enum VendorIdKind
{
    None = 0,

    /// <summary>PCI-SIG vendor ID (4 hex digits). Also used by HDAUDIO codecs.</summary>
    Pci,

    /// <summary>USB-IF vendor ID (4 hex digits).</summary>
    Usb,

    /// <summary>ACPI / PNP vendor prefix (3 or 4 characters).</summary>
    Acpi,

    /// <summary>EISA / PNP manufacturer ID used by monitors (3 letters).</summary>
    Pnp,
}

/// <summary>A parsed hardware ID. All IDs are upper-case.</summary>
public sealed record HardwareId
{
    public required string Raw { get; init; }

    /// <summary>Part before the first backslash, e.g. <c>PCI</c>, <c>USB</c>, <c>HDAUDIO</c>.</summary>
    public required string Enumerator { get; init; }

    public HardwareBus Bus { get; init; }
    public VendorIdKind VendorKind { get; init; }
    public string? VendorId { get; init; }
    public string? DeviceId { get; init; }
    public string? SubsystemVendorId { get; init; }
    public string? SubsystemDeviceId { get; init; }
    public string? Revision { get; init; }

    /// <summary>Device class code: PCI <c>CC_xxxxxx</c> or USB <c>Class_xx</c>.</summary>
    public string? ClassCode { get; init; }

    /// <summary>USB interface number (<c>MI_xx</c>) or HD-Audio function group (<c>FUNC_xx</c>).</summary>
    public string? Interface { get; init; }

    public bool HasVendor => VendorKind != VendorIdKind.None && !string.IsNullOrEmpty(VendorId);

    public override string ToString() => Raw;
}
