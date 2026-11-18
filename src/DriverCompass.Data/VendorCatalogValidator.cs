using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DriverCompass.Core.Abstractions;
using DriverCompass.Core.Catalog;
using DriverCompass.Core.Links;
using DriverCompass.Core.Models;
using Json.Schema;

namespace DriverCompass.Data;

/// <summary>
/// Validates a vendor catalog before the app uses it:
/// size limit → JSON syntax → supported schemaVersion → JSON Schema → semantic safety rules
/// (HTTPS only, every URL inside the vendor's allowed domains, placeholders only in path/query,
/// valid and bounded regular expressions, no duplicate IDs).
/// A list that fails any check is never used.
/// </summary>
public sealed class VendorCatalogValidator : IVendorCatalogValidator
{
    public const int SupportedSchemaVersion = 1;
    public const int MaxBytes = 2 * 1024 * 1024;
    private const int MaxReportedErrors = 25;

    private static readonly Lazy<JsonSchema> Schema = new(() => JsonSchema.FromText(EmbeddedData.SchemaJson));

    private static readonly JsonSerializerOptions DtoOptions = new() { PropertyNameCaseInsensitive = false };

    public CatalogValidationResult Validate(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return CatalogValidationResult.Fail("Catalog is empty.");
        }

        if (Encoding.UTF8.GetByteCount(json) > MaxBytes)
        {
            return CatalogValidationResult.Fail($"Catalog exceeds the size limit of {MaxBytes} bytes.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        }
        catch (JsonException ex)
        {
            return CatalogValidationResult.Fail($"Invalid JSON: {ex.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("schemaVersion", out var sv) && sv.ValueKind == JsonValueKind.Number &&
                sv.TryGetInt32(out var schemaVersion) && schemaVersion > SupportedSchemaVersion)
            {
                return CatalogValidationResult.Fail(
                    $"Catalog schemaVersion {schemaVersion} is newer than supported ({SupportedSchemaVersion}). Please update the app.");
            }

            var evaluation = Schema.Value.Evaluate(root, new EvaluationOptions { OutputFormat = OutputFormat.List });
            if (!evaluation.IsValid)
            {
                return CatalogValidationResult.Fail(SchemaErrors(evaluation).DefaultIfEmpty("Catalog does not match the JSON schema.").ToArray());
            }

            CatalogDto? dto;
            try
            {
                dto = root.Deserialize<CatalogDto>(DtoOptions);
            }
            catch (JsonException ex)
            {
                return CatalogValidationResult.Fail($"Catalog could not be read: {ex.Message}");
            }

            if (dto is null)
            {
                return CatalogValidationResult.Fail("Catalog is empty.");
            }

            var errors = CheckSemantics(dto);
            return errors.Count > 0
                ? new CatalogValidationResult(false, errors.Take(MaxReportedErrors).ToList(), null)
                : new CatalogValidationResult(true, [], ToCatalog(dto));
        }
    }

    /// <summary>Parses "YYYY.M.D.N" into a comparable version.</summary>
    public static bool TryParseDataVersion(string? value, out Version version)
    {
        version = new Version(0, 0);
        return value is not null && Version.TryParse(value, out version!) && version.Revision >= 0;
    }

    private static IEnumerable<string> SchemaErrors(EvaluationResults results)
    {
        var details = results.Details ?? [];
        return details
            .Where(d => d.Errors is { Count: > 0 })
            .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Value}"))
            .Distinct(StringComparer.Ordinal)
            .Take(MaxReportedErrors);
    }

    private static List<string> CheckSemantics(CatalogDto dto)
    {
        var errors = new List<string>();

        if (!TryParseDataVersion(dto.DataVersion, out _))
        {
            errors.Add($"dataVersion '{dto.DataVersion}' is not in the format YYYY.M.D.N.");
        }

        Duplicates(dto.ChipVendors.Select(v => v.Id), "chipVendors.id", errors);
        Duplicates(dto.Oems.Select(o => o.Id), "oems.id", errors);
        Duplicates(dto.GenericDrivers.Select(g => g.Id), "genericDrivers.id", errors);

        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in dto.ChipVendors)
        {
            foreach (var (kind, id) in v.Ids.Pci.Select(i => ("pci", i))
                         .Concat(v.Ids.Usb.Select(i => ("usb", i)))
                         .Concat(v.Ids.UsbProducts.Select(i => ("usb product", i)))
                         .Concat(v.Ids.Acpi.Select(i => ("acpi", i)))
                         .Concat(v.Ids.Pnp.Select(i => ("pnp", i))))
            {
                var key = $"{kind}:{id}";
                if (owners.TryGetValue(key, out var other))
                {
                    errors.Add($"{kind} ID {id} is assigned to both '{other}' and '{v.Id}'.");
                }
                else
                {
                    owners[key] = v.Id;
                }
            }

            foreach (var page in v.DriverPages)
            {
                if (!Uri.TryCreate(page.Url, UriKind.Absolute, out var uri) || !UrlSafety.IsAllowedHttps(uri, v.AllowedDomains))
                {
                    errors.Add($"chipVendors '{v.Id}': URL '{page.Url}' is not HTTPS or not within allowedDomains.");
                }
            }
        }

        foreach (var oem in dto.Oems)
        {
            foreach (var pattern in oem.Match)
            {
                CheckRegex(pattern, $"oems '{oem.Id}' match", errors);
            }

            foreach (var url in oem.SupportUrls)
            {
                CheckTemplate(oem, url, errors);
            }
        }

        foreach (var rule in dto.GenericDrivers)
        {
            if (rule.DeviceNamePattern is not null)
            {
                CheckRegex(rule.DeviceNamePattern, $"genericDrivers '{rule.Id}' deviceNamePattern", errors);
            }

            if (rule.ProviderPattern is not null)
            {
                CheckRegex(rule.ProviderPattern, $"genericDrivers '{rule.Id}' providerPattern", errors);
            }
        }

        return errors;
    }

    private static void CheckTemplate(OemDto oem, SupportUrlDto url, List<string> errors)
    {
        var where = $"oems '{oem.Id}' template '{url.Template}'";
        var placeholders = UrlSafety.GetPlaceholders(url.Template);

        var unknown = placeholders.Where(p => !UrlSafety.KnownPlaceholders.Contains(p)).ToList();
        if (unknown.Count > 0)
        {
            errors.Add($"{where}: unknown placeholder(s) {string.Join(", ", unknown)}.");
            return;
        }

        if (!placeholders.ToHashSet(StringComparer.Ordinal).SetEquals(url.Requires))
        {
            errors.Add($"{where}: 'requires' must list exactly the placeholders used in the template.");
        }

        // Placeholders must never influence scheme or host.
        var authorityEnd = url.Template.IndexOf('/', "https://".Length);
        var authority = authorityEnd < 0 ? url.Template : url.Template[..authorityEnd];
        if (authority.Contains('{', StringComparison.Ordinal))
        {
            errors.Add($"{where}: placeholders are not allowed in the host name.");
            return;
        }

        var sample = placeholders.ToDictionary(p => p, _ => (string?)"sample", StringComparer.Ordinal);
        if (UrlSafety.Expand(url.Template, sample, oem.AllowedDomains) is null)
        {
            errors.Add($"{where}: does not produce an HTTPS URL within allowedDomains.");
        }
    }

    private static void CheckRegex(string pattern, string where, List<string> errors)
    {
        try
        {
            _ = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, VendorCatalog.RegexTimeout);
        }
        catch (ArgumentException ex)
        {
            errors.Add($"{where}: invalid regular expression ({ex.Message}).");
        }
    }

    private static void Duplicates(IEnumerable<string> ids, string field, List<string> errors)
    {
        foreach (var dup in ids.GroupBy(i => i, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            errors.Add($"Duplicate {field} '{dup.Key}'.");
        }
    }

    private static VendorCatalog ToCatalog(CatalogDto dto) => new(
        dto.SchemaVersion,
        dto.DataVersion,
        dto.ChipVendors.Select(v => new ChipVendor
        {
            Id = v.Id,
            Name = v.Name,
            PciIds = Upper(v.Ids.Pci),
            UsbIds = Upper(v.Ids.Usb),
            UsbProductIds = Upper(v.Ids.UsbProducts),
            AcpiIds = Upper(v.Ids.Acpi),
            PnpIds = Upper(v.Ids.Pnp),
            AllowedDomains = v.AllowedDomains,
            DriverPages = v.DriverPages.Select(p => new DriverPage { Url = new Uri(p.Url), DeviceClasses = p.DeviceClasses }).ToList(),
            Note = v.Note is { Count: > 0 } ? new LocalizedText(v.Note) : null,
        }).ToList(),
        dto.Oems.Select(o => new OemVendor
        {
            Id = o.Id,
            Name = o.Name,
            MatchPatterns = o.Match,
            AllowedDomains = o.AllowedDomains,
            SupportUrls = o.SupportUrls.Select(s => new SupportUrlTemplate(s.Template, s.Requires)).ToList(),
        }).ToList(),
        dto.GenericDrivers.Select(g => new GenericDriverRule
        {
            Id = g.Id,
            InfName = g.InfName,
            DeviceNamePattern = g.DeviceNamePattern,
            DeviceClass = g.DeviceClass,
            Enumerators = g.Enumerators,
            ProviderPattern = g.ProviderPattern ?? "^Microsoft",
            Category = Enum.TryParse<GenericDriverCategory>(g.Category, ignoreCase: true, out var c) ? c : GenericDriverCategory.Other,
        }).ToList());

    private static List<string> Upper(IEnumerable<string> ids) => ids.Select(i => i.ToUpperInvariant()).ToList();
}
