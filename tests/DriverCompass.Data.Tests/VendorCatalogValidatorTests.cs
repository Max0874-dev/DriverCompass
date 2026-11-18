using System.Text.Json;
using System.Text.Json.Nodes;
using DriverCompass.Core.Models;
using DriverCompass.Core.Parsing;

namespace DriverCompass.Data.Tests;

public class VendorCatalogValidatorTests
{
    private readonly VendorCatalogValidator _validator = new();

    private static string RepoFile(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RepoData", name));

    /// <summary>Minimal valid catalog that tests mutate.</summary>
    private static JsonObject Valid() => JsonNode.Parse("""
        {
          "schemaVersion": 1,
          "dataVersion": "2026.1.1.1",
          "chipVendors": [
            { "id": "acme", "name": "Acme", "ids": { "pci": ["ABCD"] }, "allowedDomains": ["acme.example"],
              "driverPages": [ { "url": "https://www.acme.example/drivers" } ] }
          ],
          "oems": [
            { "id": "pcco", "name": "PC Co", "match": ["^PC Co"], "allowedDomains": ["pcco.example"],
              "supportUrls": [ { "template": "https://support.pcco.example/{serial}", "requires": ["serial"] },
                               { "template": "https://support.pcco.example/", "requires": [] } ] }
          ],
          "genericDrivers": [ { "id": "basic-display", "infName": "display.inf", "category": "display" } ]
        }
        """)!.AsObject();

    private CatalogValidationResultAssert Check(JsonObject json) => new(_validator.Validate(json.ToJsonString()));

    [Fact]
    public void Repository_vendors_json_is_valid()
    {
        var result = _validator.Validate(RepoFile("vendors.json"));
        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
        Assert.NotNull(result.Catalog);
        Assert.True(result.Catalog.ChipVendors.Count >= 20);
    }

    [Theory]
    [InlineData("Dell Inc.", "dell")]
    [InlineData("Alienware", "dell")]
    [InlineData("GIGA-BYTE TECHNOLOGY CO., LTD.", "gigabyte")]
    [InlineData("FUJITSU CLIENT COMPUTING LIMITED", "fujitsu")]
    [InlineData("TOSHIBA", "dynabook")]
    [InlineData("Dynabook Inc.", "dynabook")]
    [InlineData("MEDION", "medion")]
    [InlineData("SAMSUNG ELECTRONICS CO., LTD.", "samsung")]
    [InlineData("Razer", "razer")]
    [InlineData("HUAWEI", "huawei")]
    [InlineData("Framework", "framework")]
    [InlineData("Panasonic Connect Co., Ltd.", "panasonic")]
    [InlineData("Micro-Star International Co., Ltd.", "msi")]
    public void Repository_catalog_recognises_pc_manufacturers(string smbiosManufacturer, string expectedOem)
    {
        var catalog = _validator.Validate(RepoFile("vendors.json")).Catalog!;
        Assert.Equal(expectedOem, catalog.FindOem(new SystemInfo { Manufacturer = smbiosManufacturer })?.Vendor.Id);
    }

    [Fact]
    public void Embedded_catalog_equals_repository_file()
    {
        Assert.Equal(RepoFile("vendors.json").ReplaceLineEndings(), EmbeddedData.VendorsJson.ReplaceLineEndings());
    }

    [Fact]
    public void Example_entries_are_valid_when_inserted()
    {
        var example = JsonNode.Parse(RepoFile("new-vendor.example.json"))!.AsObject();
        var json = Valid();
        json["chipVendors"]!.AsArray().Add(example["chipVendor"]!.DeepClone());
        json["oems"]!.AsArray().Add(example["oem"]!.DeepClone());
        Check(json).Valid();
    }

    [Fact]
    public void Minimal_catalog_is_valid_and_mapped()
    {
        var result = _validator.Validate(Valid().ToJsonString());
        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        var catalog = result.Catalog!;
        Assert.Equal("Acme", catalog.FindChipVendor(HardwareIdParser.Parse(@"PCI\VEN_ABCD&DEV_0001"))?.Name);
        Assert.Equal("PC Co", catalog.FindOem(new SystemInfo { Manufacturer = "PC Co Ltd." })?.Vendor.Name);
    }

    [Fact]
    public void Http_url_is_rejected()
    {
        var json = Valid();
        json["chipVendors"]![0]!["driverPages"]![0]!["url"] = "http://www.acme.example/drivers";
        Check(json).Invalid();
    }

    [Fact]
    public void Url_outside_allowed_domains_is_rejected()
    {
        var json = Valid();
        json["chipVendors"]![0]!["driverPages"]![0]!["url"] = "https://acme.example.attacker.example/drivers";
        Check(json).Invalid("allowedDomains");
    }

    [Fact]
    public void Placeholder_in_host_is_rejected()
    {
        var json = Valid();
        json["oems"]![0]!["supportUrls"]![0]!["template"] = "https://{serial}.pcco.example/";
        Check(json).Invalid();
    }

    [Fact]
    public void Unknown_placeholder_is_rejected()
    {
        var json = Valid();
        json["oems"]![0]!["supportUrls"]![0]!["template"] = "https://support.pcco.example/{password}";
        Check(json).Invalid();
    }

    [Fact]
    public void Requires_must_match_placeholders()
    {
        var json = Valid();
        json["oems"]![0]!["supportUrls"]![0]!["requires"] = new JsonArray("model");
        Check(json).Invalid("requires");
    }

    [Fact]
    public void Invalid_regex_is_rejected()
    {
        var json = Valid();
        json["oems"]![0]!["match"] = new JsonArray("([unclosed");
        Check(json).Invalid("regular expression");
    }

    [Fact]
    public void Duplicate_vendor_id_mapping_is_rejected()
    {
        var json = Valid();
        var copy = json["chipVendors"]![0]!.DeepClone();
        copy["id"] = "acme2";
        json["chipVendors"]!.AsArray().Add(copy);
        Check(json).Invalid("assigned to both");
    }

    [Fact]
    public void Duplicate_ids_are_rejected()
    {
        var json = Valid();
        var copy = json["chipVendors"]![0]!.DeepClone();
        copy["ids"] = JsonNode.Parse("""{ "usb": ["1111"] }""");
        json["chipVendors"]!.AsArray().Add(copy);
        Check(json).Invalid("Duplicate");
    }

    [Fact]
    public void Lowercase_hex_id_is_rejected_by_schema()
    {
        var json = Valid();
        json["chipVendors"]![0]!["ids"] = JsonNode.Parse("""{ "pci": ["abcd"] }""");
        Check(json).Invalid();
    }

    [Fact]
    public void Unknown_property_is_rejected()
    {
        var json = Valid();
        json["chipVendors"]![0]!["downloadExe"] = "https://www.acme.example/x.exe";
        Check(json).Invalid();
    }

    [Fact]
    public void Newer_schema_version_is_rejected_with_clear_message()
    {
        var json = Valid();
        json["schemaVersion"] = 99;
        Check(json).Invalid("update the app");
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("null")]
    public void Garbage_is_rejected(string input)
    {
        Assert.False(_validator.Validate(input).IsValid);
    }

    [Fact]
    public void Oversized_input_is_rejected()
    {
        var huge = "{\"x\":\"" + new string('a', VendorCatalogValidator.MaxBytes) + "\"}";
        new CatalogValidationResultAssert(_validator.Validate(huge)).Invalid("size limit");
    }

    [Fact]
    public void Bad_data_version_is_rejected()
    {
        var json = Valid();
        json["dataVersion"] = "latest";
        Check(json).Invalid();
    }

    [Theory]
    [InlineData("2026.11.16.1", true)]
    [InlineData("2026.1.1.0", true)]
    [InlineData("2026.1.1", false)]
    [InlineData("abc", false)]
    public void Data_version_parsing(string value, bool ok) =>
        Assert.Equal(ok, VendorCatalogValidator.TryParseDataVersion(value, out _));

    private sealed class CatalogValidationResultAssert(Core.Abstractions.CatalogValidationResult result)
    {
        public void Valid() => Assert.True(result.IsValid, string.Join("; ", result.Errors));

        public void Invalid(string? expectedFragment = null)
        {
            Assert.False(result.IsValid);
            Assert.Null(result.Catalog);
            Assert.NotEmpty(result.Errors);
            if (expectedFragment is not null)
            {
                Assert.Contains(result.Errors, e => e.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase));
            }
        }
    }
}

public class EmbeddedPciIdsTests
{
    [Fact]
    public void Embedded_pci_ids_knows_common_vendors()
    {
        var db = EmbeddedData.CreatePciIdsDatabase();
        Assert.Contains("NVIDIA", db.GetVendorName(VendorIdKind.Pci, "10DE"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Intel", db.GetVendorName(VendorIdKind.Pci, "8086"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RTL8125", db.GetDeviceName(VendorIdKind.Pci, "10EC", "8125"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Schema_is_valid_json()
    {
        using var doc = JsonDocument.Parse(EmbeddedData.SchemaJson);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }
}
