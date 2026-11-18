using System.Text.Json.Serialization;

namespace DriverCompass.Data;

// JSON shape of data/vendors.json. Mapped to the Core models after validation.

internal sealed class CatalogDto
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; }
    [JsonPropertyName("dataVersion")] public string DataVersion { get; set; } = string.Empty;
    [JsonPropertyName("chipVendors")] public List<ChipVendorDto> ChipVendors { get; set; } = [];
    [JsonPropertyName("oems")] public List<OemDto> Oems { get; set; } = [];
    [JsonPropertyName("genericDrivers")] public List<GenericDriverDto> GenericDrivers { get; set; } = [];
}

internal sealed class ChipVendorDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("ids")] public IdsDto Ids { get; set; } = new();
    [JsonPropertyName("allowedDomains")] public List<string> AllowedDomains { get; set; } = [];
    [JsonPropertyName("driverPages")] public List<DriverPageDto> DriverPages { get; set; } = [];
    [JsonPropertyName("note")] public Dictionary<string, string>? Note { get; set; }
}

internal sealed class IdsDto
{
    [JsonPropertyName("pci")] public List<string> Pci { get; set; } = [];
    [JsonPropertyName("usb")] public List<string> Usb { get; set; } = [];
    [JsonPropertyName("usbProducts")] public List<string> UsbProducts { get; set; } = [];
    [JsonPropertyName("acpi")] public List<string> Acpi { get; set; } = [];
    [JsonPropertyName("pnp")] public List<string> Pnp { get; set; } = [];
}

internal sealed class DriverPageDto
{
    [JsonPropertyName("url")] public string Url { get; set; } = string.Empty;
    [JsonPropertyName("deviceClasses")] public List<string> DeviceClasses { get; set; } = [];
}

internal sealed class OemDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("match")] public List<string> Match { get; set; } = [];
    [JsonPropertyName("allowedDomains")] public List<string> AllowedDomains { get; set; } = [];
    [JsonPropertyName("supportUrls")] public List<SupportUrlDto> SupportUrls { get; set; } = [];
}

internal sealed class SupportUrlDto
{
    [JsonPropertyName("template")] public string Template { get; set; } = string.Empty;
    [JsonPropertyName("requires")] public List<string> Requires { get; set; } = [];
}

internal sealed class GenericDriverDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("infName")] public string? InfName { get; set; }
    [JsonPropertyName("deviceNamePattern")] public string? DeviceNamePattern { get; set; }
    [JsonPropertyName("deviceClass")] public string? DeviceClass { get; set; }
    [JsonPropertyName("enumerators")] public List<string> Enumerators { get; set; } = [];
    [JsonPropertyName("providerPattern")] public string? ProviderPattern { get; set; }
    [JsonPropertyName("category")] public string Category { get; set; } = "other";
}
