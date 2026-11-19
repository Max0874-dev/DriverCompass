using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace DriverCompass.Data.Tests;

/// <summary>
/// Every released app rejects vendor lists with properties it does not know (on purpose: a manipulated list must
/// not smuggle in anything). If the schema gained a field without a new <c>schemaVersion</c>, older apps would call
/// a perfectly good list "failed validation". With a new <c>schemaVersion</c> they say "please update the app".
/// See docs/adding-vendors.md → "Changing the schema".
/// </summary>
public sealed class SchemaVersionGuardTests
{
    /// <summary>
    /// Fingerprint of data/vendors.schema.json per schemaVersion (descriptions and comments ignored).
    /// Add a line here only together with raising <see cref="VendorCatalogValidator.SupportedSchemaVersion"/>.
    /// </summary>
    private static readonly Dictionary<int, string> KnownSchemas = new()
    {
        [1] = "0637b61f6927f6ac5b33d618a917fec2c263e3be6b72db22f589fb8e8019272b", // 0.1.0 – 0.3.0
    };

    private static string RepoFile(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RepoData", name));

    [Fact]
    public void Schema_changes_come_with_a_new_schema_version()
    {
        var version = VendorCatalogValidator.SupportedSchemaVersion;
        var actual = Fingerprint(EmbeddedData.SchemaJson);

        Assert.True(KnownSchemas.TryGetValue(version, out var expected),
            $"No fingerprint recorded for schemaVersion {version}. Add [{version}] = \"{actual}\" to KnownSchemas.");
        Assert.True(expected == actual,
            $"data/vendors.schema.json changed, but schemaVersion is still {version}. Older DriverCompass versions would reject " +
            $"the list as invalid. Raise VendorCatalogValidator.SupportedSchemaVersion and \"schemaVersion\" in data/vendors.json " +
            $"to {version + 1} and add [{version + 1}] = \"{actual}\" to KnownSchemas (see docs/adding-vendors.md).");
    }

    [Fact]
    public void Repository_list_uses_the_current_schema_version()
    {
        var list = JsonNode.Parse(RepoFile("vendors.json"))!;
        Assert.Equal(VendorCatalogValidator.SupportedSchemaVersion, (int)list["schemaVersion"]!);
    }

    [Fact]
    public void Released_schema_versions_are_never_rewritten()
    {
        // The highest recorded version is the current one; all others belong to released apps and stay as they are.
        Assert.Equal(VendorCatalogValidator.SupportedSchemaVersion, KnownSchemas.Keys.Max());
        Assert.Equal(Enumerable.Range(1, KnownSchemas.Count), KnownSchemas.Keys.Order());
    }

    [Fact]
    public void Fingerprint_ignores_descriptions_and_formatting()
    {
        const string a = """{ "type": "object", "description": "one", "properties": { "x": { "type": "string", "$comment": "c" } } }""";
        const string b = """
            {
              "type": "object",
              "description": "two",
              "properties": { "x": { "type": "string" } }
            }
            """;
        const string c = """{ "type": "object", "properties": { "x": { "type": "integer" } } }""";

        Assert.Equal(Fingerprint(a), Fingerprint(b));
        Assert.NotEqual(Fingerprint(a), Fingerprint(c));
    }

    private static string Fingerprint(string schemaJson)
    {
        var node = JsonNode.Parse(schemaJson)!;
        Strip(node);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(node.ToJsonString())));
    }

    private static void Strip(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in new[] { "description", "$comment", "title", "examples" })
                {
                    obj.Remove(key);
                }

                foreach (var (_, child) in obj)
                {
                    Strip(child);
                }

                break;
            case JsonArray array:
                foreach (var child in array)
                {
                    Strip(child);
                }

                break;
        }
    }
}
