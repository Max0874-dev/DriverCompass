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

        // Stage 2: source names, privacy notes, verdicts and reason codes are built from ids at runtime.
        expected.AddRange(VersionSourceIds.All.SelectMany(id => new[] { $"VersionSource_{id}", $"VersionSource_{id}_Privacy" }));
        expected.AddRange(Enum.GetNames<VersionCheckState>().Select(s => $"VersionState_{s}"));
        expected.Add("VersionState_UpToDate_windowsupdate");
        expected.AddRange(typeof(VersionSourceReasons).GetFields().Select(f => $"VersionReason_{f.GetValue(null)}"));
        expected.AddRange(["Col_Latest", "Col_VersionState", "Col_VersionSource", "Version_Package"]);

        // Stage 3: outcome texts, history labels and restore point reasons are built from enum names.
        expected.AddRange(Enum.GetNames<InstallOutcome>().SelectMany(o => new[] { $"Install_Outcome_{o}", $"InstallOutcomeShort_{o}" }));
        expected.AddRange(Enum.GetNames<InstallAction>().Select(a => $"History_Action_{a}"));
        expected.AddRange(["RestoreReason_Disabled", "RestoreReason_FrequencyLimit", "RestoreReason_Failed"]);
        expected.AddRange(["PackageHost_dell", "PackageHost_hp", "Install_Intro_Oem", "Install_Steps_Oem"]);

        var missing = expected.Where(k => !keys.ContainsKey(k)).ToList();
        Assert.True(missing.Count == 0, "Missing keys: " + string.Join(", ", missing));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void No_empty_values(string language) =>
        Assert.Empty(Load(language).Where(kv => string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key));
}
