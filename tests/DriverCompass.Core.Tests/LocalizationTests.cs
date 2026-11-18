using System.Xml.Linq;
using DriverCompass.Core.Models;

namespace DriverCompass.Core.Tests;

/// <summary>Keeps the German and English .resw files in sync and covers every key the code builds dynamically.</summary>
public class LocalizationTests
{
    private static Dictionary<string, string> Load(string language)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Strings", language, "Resources.resw");
        return XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(e => (string)e.Attribute("name")!, e => (string?)e.Element("value") ?? string.Empty, StringComparer.Ordinal);
    }

    [Fact]
    public void German_and_English_have_the_same_keys()
    {
        var de = Load("de-DE");
        var en = Load("en-US");

        Assert.Empty(de.Keys.Except(en.Keys));
        Assert.Empty(en.Keys.Except(de.Keys));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void Dynamic_keys_exist(string language)
    {
        var keys = Load(language);
        var expected = new List<string>();
        expected.AddRange(Enum.GetValues<DeviceStatus>().Select(s => $"Status_{s}"));
        expected.AddRange(Enum.GetValues<GenericDriverCategory>().Select(c => $"Reason_Generic_{c}"));
        expected.AddRange(Enum.GetValues<StatusReason>().Where(r => r is not StatusReason.GenericDriver).Select(r => $"Reason_{r}"));
        expected.AddRange(Enum.GetValues<LinkSpecificity>().Select(s => $"Link_OemSupport_{s}"));
        expected.AddRange(Enum.GetValues<CatalogSource>().Select(s => $"CatalogSource_{s}"));
        expected.AddRange(["Link_ChipVendor", "Link_MicrosoftCatalog", "CmError_Unknown", "Class_Unknown",
            "Catalog_RemoteUnavailable", "Catalog_RemoteInvalid", "Catalog_RemoteOlder", "Catalog_CacheInvalid"]);

        var missing = expected.Where(k => !keys.ContainsKey(k)).ToList();
        Assert.True(missing.Count == 0, "Missing keys: " + string.Join(", ", missing));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void No_empty_values(string language) =>
        Assert.Empty(Load(language).Where(kv => string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key));
}
