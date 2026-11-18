using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace DriverCompass.Installer.Packages;

/// <param name="InfPath">Extracted INF file.</param>
/// <param name="Sha256">SHA-256 of the INF (Windows copies it byte for byte to oemNN.inf).</param>
/// <param name="CatalogSigner">Signer of the driver catalog (.cat) the INF references.</param>
public sealed record InfCandidate(string InfPath, string Sha256, string CatalogSigner);

/// <summary>
/// Picks the INF files of an extracted package that are meant for a given device: the INF must list one of the
/// device's hardware IDs, target this CPU architecture, reference a catalog via <c>CatalogFile</c>, and that
/// catalog must carry a valid, trusted signature. pnputil re-checks every file against the catalog on install.
/// </summary>
public static partial class InfMatcher
{
    private const int MaxInfFiles = 2_000;
    private const long MaxInfBytes = 8L * 1024 * 1024;

    public static IReadOnlyList<InfCandidate> FindMatching(
        string root,
        IReadOnlySet<string> deviceIds,
        Func<string, string?> trustedSigner,
        Architecture architecture = Architecture.X64)
    {
        var result = new List<InfCandidate>();
        foreach (var inf in Directory.EnumerateFiles(root, "*.inf", SearchOption.AllDirectories).Take(MaxInfFiles))
        {
            var info = new FileInfo(inf);
            if (info.Length > MaxInfBytes || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                continue;
            }

            var text = StripComments(File.ReadAllText(inf));
            if (!MentionsAny(text, deviceIds) || !TargetsArchitecture(text, architecture))
            {
                continue;
            }

            var catalogName = CatalogFileRegex().Match(text) is { Success: true } m ? m.Groups["name"].Value.Trim().Trim('"') : null;
            if (catalogName is null || catalogName.IndexOfAny(['\\', '/', ':']) >= 0)
            {
                continue;
            }

            var catalog = Path.Combine(Path.GetDirectoryName(inf)!, catalogName);
            if (!File.Exists(catalog) || trustedSigner(catalog) is not { } signer)
            {
                continue;
            }

            using var stream = File.OpenRead(inf);
            result.Add(new InfCandidate(inf, Convert.ToHexString(SHA256.HashData(stream)), signer));
        }

        return result;
    }

    internal static string StripComments(string text) =>
        string.Join('\n', text.Split('\n').Select(line =>
        {
            // ';' starts a comment unless it is inside a quoted string.
            var inQuotes = false;
            for (var i = 0; i < line.Length; i++)
            {
                if (line[i] == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (line[i] == ';' && !inQuotes)
                {
                    return line[..i];
                }
            }

            return line;
        }));

    /// <summary>Exact, case-insensitive hardware-ID tokens (e.g. "PCI\VEN_8086&amp;DEV_A0E8") – no prefix matching.</summary>
    internal static bool MentionsAny(string text, IReadOnlySet<string> deviceIds) =>
        HardwareIdTokenRegex().Matches(text).Any(m => deviceIds.Contains(m.Value));

    /// <summary>
    /// [Manufacturer] decorations: an INF that only has decorations for other architectures (e.g. NTx86 on x64)
    /// is skipped; an INF without any decoration applies everywhere.
    /// </summary>
    internal static bool TargetsArchitecture(string text, Architecture architecture)
    {
        var own = architecture switch
        {
            Architecture.Arm64 => "NTarm64",
            Architecture.X86 => "NTx86",
            _ => "NTamd64",
        };

        var decorations = DecorationRegex().Matches(text).Select(m => m.Value).ToList();
        return decorations.Count == 0 || decorations.Any(d => d.Equals(own, StringComparison.OrdinalIgnoreCase));
    }

    [GeneratedRegex(@"^\s*CatalogFile(\.[A-Za-z0-9]+)?\s*=\s*(?<name>[^\r\n]+?)\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CatalogFileRegex();

    [GeneratedRegex(@"[A-Za-z0-9_]+\\[A-Za-z0-9_&\-\.]+", RegexOptions.CultureInvariant)]
    private static partial Regex HardwareIdTokenRegex();

    [GeneratedRegex(@"\bNT(amd64|x86|arm64|ia64|arm)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DecorationRegex();
}
