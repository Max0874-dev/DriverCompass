namespace DriverCompass.Core.Models;

/// <summary>Localized free text from the vendor catalog (keys: two-letter language codes).</summary>
public sealed record LocalizedText(IReadOnlyDictionary<string, string> Values)
{
    public string? Get(string languageCode) =>
        Values.TryGetValue(languageCode, out var v) ? v :
        Values.TryGetValue("en", out var en) ? en :
        Values.Values.FirstOrDefault();
}

/// <summary>Chip manufacturer (NVIDIA, AMD, Intel, Realtek …).</summary>
public sealed record ChipVendor
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public IReadOnlyList<string> PciIds { get; init; } = [];
    public IReadOnlyList<string> UsbIds { get; init; } = [];

    /// <summary>Single USB devices "VID:PID" that belong to this vendor although the VID is someone else's (checked first).</summary>
    public IReadOnlyList<string> UsbProductIds { get; init; } = [];
    public IReadOnlyList<string> AcpiIds { get; init; } = [];
    public IReadOnlyList<string> PnpIds { get; init; } = [];
    public IReadOnlyList<string> AllowedDomains { get; init; } = [];
    public IReadOnlyList<DriverPage> DriverPages { get; init; } = [];
    public LocalizedText? Note { get; init; }
}

public sealed record DriverPage
{
    public required Uri Url { get; init; }

    /// <summary>Setup classes this page is meant for; empty = all.</summary>
    public IReadOnlyList<string> DeviceClasses { get; init; } = [];
}

/// <summary>PC / mainboard manufacturer (Dell, HP, Lenovo, ASUS …).</summary>
public sealed record OemVendor
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    /// <summary>Regular expressions matched against Win32_ComputerSystem.Manufacturer / Win32_BaseBoard.Manufacturer.</summary>
    public IReadOnlyList<string> MatchPatterns { get; init; } = [];

    public IReadOnlyList<string> AllowedDomains { get; init; } = [];

    /// <summary>Ordered most specific first.</summary>
    public IReadOnlyList<SupportUrlTemplate> SupportUrls { get; init; } = [];
}

/// <summary>URL template with placeholders <c>{serial}</c>, <c>{model}</c>, <c>{sku}</c>, <c>{board}</c>, <c>{family}</c>.</summary>
public sealed record SupportUrlTemplate(string Template, IReadOnlyList<string> Requires);

/// <summary>Rule that marks a Microsoft inbox driver as "generic, vendor driver recommended".</summary>
public sealed record GenericDriverRule
{
    public required string Id { get; init; }
    public string? InfName { get; init; }
    public string? DeviceNamePattern { get; init; }
    public string? DeviceClass { get; init; }

    /// <summary>Restricts the rule to these buses (instance ID prefix, e.g. USB, PCI); empty = all.</summary>
    public IReadOnlyList<string> Enumerators { get; init; } = [];

    /// <summary>Regex for the driver provider, default <c>^Microsoft</c>.</summary>
    public string ProviderPattern { get; init; } = "^Microsoft";

    public GenericDriverCategory Category { get; init; }
}

/// <summary>How the OEM was found.</summary>
public enum OemMatchSource
{
    ComputerSystem,
    BaseBoard,
}

public sealed record OemMatch(OemVendor Vendor, OemMatchSource Source);
