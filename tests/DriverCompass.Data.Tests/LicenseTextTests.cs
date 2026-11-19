using System.Text.Json.Nodes;

namespace DriverCompass.Data.Tests;

/// <summary>
/// The release ZIP ships third-party binaries, and several of their licenses (MIT, Apache-2.0, the Windows App SDK
/// terms) require the license text to travel with them. licenses/packages.json maps every shipped NuGet package to a
/// folder with its texts; this test fails as soon as a new package would ship without one.
/// </summary>
public sealed class LicenseTextTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DriverCompass.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Repository root (DriverCompass.slnx) not found.");
    }

    private static JsonObject Manifest() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(RepoRoot, "licenses", "packages.json")))!.AsObject();

    /// <summary>Packages that put files into the app's output (lib, runtimes or native assets), per the restore result.</summary>
    private static SortedSet<string> ShippedPackages()
    {
        var assetsPath = Path.Combine(RepoRoot, "src", "DriverCompass.App", "obj", "project.assets.json");
        Assert.SkipUnless(File.Exists(assetsPath), "Restore the app project first (project.assets.json is missing).");

        var assets = JsonNode.Parse(File.ReadAllText(assetsPath))!;
        var shipped = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (target, libraries) in assets["targets"]!.AsObject())
        {
            if (!target.Contains("/win-", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var (nameAndVersion, library) in libraries!.AsObject())
            {
                if ((string?)library!["type"] != "package")
                {
                    continue;
                }

                var hasFiles = new[] { "runtime", "native", "runtimeTargets" }
                    .Select(kind => library[kind]?.AsObject())
                    .Any(files => files is not null && files.Any(f => !f.Key.EndsWith("_._", StringComparison.Ordinal)));
                if (hasFiles)
                {
                    shipped.Add(nameAndVersion.Split('/')[0]);
                }
            }
        }

        return shipped;
    }

    [Fact]
    public void Every_shipped_package_has_its_license_texts()
    {
        var manifest = Manifest();
        var packages = manifest["packages"]!.AsObject();
        var buildOnly = manifest["buildOnly"]!.AsArray().Select(n => (string)n!).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var shipped = ShippedPackages();
        Assert.NotEmpty(shipped);

        var missing = shipped.Where(p => !packages.ContainsKey(p) && !buildOnly.Contains(p)).ToList();
        Assert.True(missing.Count == 0,
            "These packages ship files but have no license texts: " + string.Join(", ", missing) +
            ". Copy their license (and notice) files into licenses/<folder>/, add them to licenses/packages.json and THIRD-PARTY-NOTICES.md.");

        var shippedButBuildOnly = shipped.Where(buildOnly.Contains).ToList();
        Assert.True(shippedButBuildOnly.Count == 0, "Listed as build-only but shipped: " + string.Join(", ", shippedButBuildOnly));
    }

    [Fact]
    public void Every_license_folder_exists_and_is_not_empty()
    {
        var manifest = Manifest();
        var folders = manifest["packages"]!.AsObject().Select(p => (string)p.Value!)
            .Concat(manifest["runtimes"]!.AsObject().Select(p => (string)p.Value!))
            .Distinct(StringComparer.Ordinal);

        foreach (var folder in folders)
        {
            var path = Path.Combine(RepoRoot, "licenses", folder);
            Assert.True(Directory.Exists(path), $"licenses/{folder} is missing.");
            Assert.True(Directory.EnumerateFiles(path).Any(f => new FileInfo(f).Length > 0), $"licenses/{folder} has no license text.");
        }
    }

    [Fact]
    public void Every_license_folder_is_mentioned_in_the_third_party_notices()
    {
        var notices = File.ReadAllText(Path.Combine(RepoRoot, "THIRD-PARTY-NOTICES.md"));
        foreach (var folder in Directory.EnumerateDirectories(Path.Combine(RepoRoot, "licenses")).Select(Path.GetFileName))
        {
            Assert.Contains($"licenses/{folder}", notices, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Release_workflow_copies_the_license_folder()
    {
        var workflow = File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "release.yml"));
        Assert.Contains("licenses", workflow, StringComparison.Ordinal);
    }
}
