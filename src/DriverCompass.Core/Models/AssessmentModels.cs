namespace DriverCompass.Core.Models;

/// <summary>Traffic-light status. The numeric order is the sort order (problems first).</summary>
public enum DeviceStatus
{
    /// <summary>Device reports an error (Configuration Manager code ≠ 0, except 28/22).</summary>
    Error = 0,

    /// <summary>No driver installed (code 28).</summary>
    MissingDriver = 1,

    /// <summary>Runs on a generic Microsoft driver where a vendor driver is recommended.</summary>
    GenericDriver = 2,

    /// <summary>Device is disabled by the user (code 22). Informational.</summary>
    Disabled = 3,

    /// <summary>Working driver installed.</summary>
    Ok = 4,
}

/// <summary>Why a device got its status. Maps 1:1 to a localized text in the app.</summary>
public enum StatusReason
{
    VendorDriver,
    InboxDriver,
    NoDriverInfo,
    NoDriverInstalled,
    DeviceError,
    Disabled,
    NotPresent,
    GenericDriver,
}

/// <summary>Category of a generic driver rule (drives the explanation text).</summary>
public enum GenericDriverCategory
{
    Other = 0,
    Display,
    Audio,
    Network,
    Bluetooth,
    Storage,
}

public enum DriverLinkKind
{
    /// <summary>Support page of the PC or mainboard manufacturer.</summary>
    OemSupport = 0,

    /// <summary>Official driver page of the chip vendor.</summary>
    ChipVendor = 1,

    /// <summary>Microsoft Update Catalog search for a hardware ID.</summary>
    MicrosoftCatalog = 2,
}

public enum LinkSpecificity
{
    /// <summary>Link contains the serial number / service tag – opens the exact device page.</summary>
    Serial = 0,

    /// <summary>Link contains model, SKU or board name.</summary>
    Model = 1,

    /// <summary>Hardware-ID specific search.</summary>
    HardwareId = 2,

    /// <summary>General download page.</summary>
    Generic = 3,
}

/// <summary>A suggested place to get a driver.</summary>
/// <param name="Kind">Source type – also the primary sort key.</param>
/// <param name="Uri">Absolute HTTPS URI (already validated against the vendor's allowed domains).</param>
/// <param name="Specificity">How exact the link is.</param>
/// <param name="SourceName">Vendor/OEM name for display, e.g. "Dell", "NVIDIA", "Microsoft Update Catalog".</param>
/// <param name="OemId">Vendor-list id of the PC maker (e.g. "hp") for <see cref="DriverLinkKind.OemSupport"/> links.</param>
public sealed record DriverLink(DriverLinkKind Kind, Uri Uri, LinkSpecificity Specificity, string SourceName, string? OemId = null);

/// <summary>Result of evaluating one device.</summary>
public sealed record DeviceAssessment
{
    public required DeviceInfo Device { get; init; }
    public required DeviceStatus Status { get; init; }
    public required StatusReason Reason { get; init; }

    /// <summary>Set for <see cref="StatusReason.GenericDriver"/>.</summary>
    public GenericDriverCategory? GenericCategory { get; init; }

    /// <summary>ROOT\, SWD\, software devices … hidden by default.</summary>
    public bool IsSystemDevice { get; init; }

    /// <summary>Parsed hardware IDs (same order as <see cref="DeviceInfo.HardwareIds"/>).</summary>
    public IReadOnlyList<HardwareId> HardwareIds { get; init; } = [];

    /// <summary>The ID used to determine the chip vendor (first ID with a vendor).</summary>
    public HardwareId? PrimaryId { get; init; }

    public ChipVendor? ChipVendor { get; init; }

    /// <summary>Vendor name: catalog → pci.ids → Windows manufacturer string.</summary>
    public string? VendorName { get; init; }

    /// <summary>Chip name from pci.ids, useful when Windows only shows "Unknown device".</summary>
    public string? DetectedName { get; init; }

    public IReadOnlyList<DriverLink> Links { get; init; } = [];

    /// <summary>Stage 2: result of the version check, <c>null</c> until a version provider ran.</summary>
    public VersionCheckResult? VersionCheck { get; init; }

    public bool NeedsAttention => Status is DeviceStatus.Error or DeviceStatus.MissingDriver or DeviceStatus.GenericDriver;

    /// <summary>Best human-readable name: Windows name unless it is only a placeholder and pci.ids knows better.</summary>
    public string DisplayName
    {
        get
        {
            var name = Device.Name ?? Device.Description;
            if (!string.IsNullOrWhiteSpace(DetectedName) &&
                (Status is DeviceStatus.MissingDriver or DeviceStatus.GenericDriver || string.IsNullOrWhiteSpace(name)))
            {
                return string.IsNullOrWhiteSpace(VendorName) || DetectedName.StartsWith(VendorName, StringComparison.OrdinalIgnoreCase)
                    ? DetectedName
                    : $"{VendorName} {DetectedName}";
            }

            return string.IsNullOrWhiteSpace(name) ? Device.InstanceId : name;
        }
    }
}

/// <summary>Outcome of a full analysis run.</summary>
public sealed record AnalysisResult(
    ScanResult Scan,
    IReadOnlyList<DeviceAssessment> Devices,
    CatalogInfo Catalog,
    DateTimeOffset CompletedAt)
{
    public int AttentionCount => Devices.Count(d => d.NeedsAttention && !d.IsSystemDevice);
}

/// <summary>Where the vendor catalog came from.</summary>
public enum CatalogSource
{
    Embedded = 0,
    Cache = 1,
    Remote = 2,
}

public sealed record CatalogInfo(CatalogSource Source, string DataVersion, IReadOnlyList<string> Warnings);

/// <summary>Options for one analysis run (from the user settings).</summary>
public sealed record AnalysisOptions
{
    /// <summary>Allow the serial number in OEM support links (opened only on click).</summary>
    public bool UseSerialNumberInLinks { get; init; } = true;

    public static AnalysisOptions Default { get; } = new();
}
