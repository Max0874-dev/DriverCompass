using DriverCompass.Core.Models;

namespace DriverCompass.Core.Abstractions;

/// <summary>Reads all devices and the PC identity. Implementations: WMI (real) and mock (tests/demo).</summary>
public interface IDeviceScanner
{
    /// <summary>Short identifier for logs/reports, e.g. "wmi" or "mock".</summary>
    string Id { get; }

    Task<ScanResult> ScanAsync(CancellationToken cancellationToken = default);
}

/// <summary>Assigns the traffic-light status to a device.</summary>
public interface IDeviceStatusEvaluator
{
    DeviceAssessment Evaluate(DeviceInfo device, IVendorCatalog catalog);
}

/// <summary>Read-only, validated vendor catalog.</summary>
public interface IVendorCatalog
{
    int SchemaVersion { get; }
    string DataVersion { get; }
    IReadOnlyList<ChipVendor> ChipVendors { get; }
    IReadOnlyList<OemVendor> Oems { get; }

    ChipVendor? FindChipVendor(HardwareId id);

    /// <summary>Matches Win32_ComputerSystem first, falls back to the mainboard for white-box PCs.</summary>
    OemMatch? FindOem(SystemInfo system);

    GenericDriverRule? MatchGenericDriver(DeviceInfo device);
}

/// <summary>Loads the catalog: remote (GitHub) → local cache → embedded fallback.</summary>
public interface IVendorCatalogProvider
{
    Task<CatalogLoadResult> LoadAsync(CancellationToken cancellationToken = default);
}

public sealed record CatalogLoadResult(IVendorCatalog Catalog, CatalogInfo Info);

/// <summary>Validates catalog JSON before it is used (schema + semantic safety rules).</summary>
public interface IVendorCatalogValidator
{
    CatalogValidationResult Validate(string json);
}

public sealed record CatalogValidationResult(bool IsValid, IReadOnlyList<string> Errors, IVendorCatalog? Catalog)
{
    /// <summary>The list uses a newer <c>schemaVersion</c> than this app understands – not broken, the app is just too old.</summary>
    public bool RequiresNewerApp { get; init; }

    public static CatalogValidationResult Fail(params string[] errors) => new(false, errors, null);
}

/// <summary>Optional name database (pci.ids) for chips Windows cannot name.</summary>
public interface IDeviceNameLookup
{
    string? GetVendorName(VendorIdKind kind, string vendorId);

    string? GetDeviceName(VendorIdKind kind, string vendorId, string deviceId);
}

/// <summary>One strategy that produces driver links (OEM, chip vendor, Microsoft Update Catalog).</summary>
public interface ILinkSource
{
    /// <summary>Lower runs first.</summary>
    int Priority { get; }

    IEnumerable<DriverLink> GetLinks(DeviceAssessment assessment, LinkContext context);
}

public sealed record LinkContext(SystemInfo System, IVendorCatalog Catalog, AnalysisOptions Options);

/// <summary>
/// Finds the PC maker's driver page for exactly this model when the vendor list cannot build it from
/// the system values (HP: product number from HP's signed platform list). Asked only when the user opens
/// the maker's link; any failure falls back to the link from the vendor list.
/// </summary>
public interface IOemModelPageProvider
{
    /// <summary>Vendor-list id of the PC maker this provider is for (e.g. "hp").</summary>
    string OemId { get; }

    /// <summary>True if this PC has the values the provider needs (no network access).</summary>
    bool CanResolve(SystemInfo system);

    /// <summary>The model page (HTTPS, maker's own domain) or <c>null</c>. Does not throw for network or data errors.</summary>
    Task<Uri?> FindModelPageAsync(SystemInfo system, CancellationToken cancellationToken = default);
}

/// <summary>Combines all <see cref="ILinkSource"/>s, orders and de-duplicates the links.</summary>
public interface ILinkResolver
{
    IReadOnlyList<DriverLink> Resolve(DeviceAssessment assessment, LinkContext context);
}

/// <summary>Orchestration used by the UI: scan → evaluate → links → sort.</summary>
public interface IDeviceAnalysisService
{
    Task<AnalysisResult> AnalyzeAsync(AnalysisOptions options, CancellationToken cancellationToken = default);
}

/// <summary>Supplies localized texts to UI-agnostic code (reports).</summary>
public interface ITextProvider
{
    string Get(string key);
}

public sealed record ReportOptions
{
    public bool IncludeSystemDevices { get; init; }
    public bool IncludeSerialNumber { get; init; }

    /// <summary>CSV field separator; default follows the culture (";" for German, "," for English).</summary>
    public string? CsvSeparator { get; init; }
}

/// <summary>Writes an analysis result as a report file.</summary>
public interface IReportExporter
{
    string FormatId { get; }
    string FileExtension { get; }

    Task ExportAsync(AnalysisResult result, ReportOptions options, ITextProvider texts, Stream target, CancellationToken cancellationToken = default);
}

/// <summary>Persisted user settings.</summary>
public interface ISettingsService
{
    AppSettings Current { get; }

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
