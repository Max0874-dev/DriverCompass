using System.Diagnostics;
using System.Runtime.Versioning;
using DriverCompass.VersionProviders.Packages;

namespace DriverCompass.Installer.Packages;

/// <summary>Gets the driver files out of a verified vendor package.</summary>
public interface IPayloadExtractor
{
    /// <summary>True if files were extracted into <paramref name="targetDirectory"/> (which must be empty).</summary>
    bool TryExtract(OemPackage package, string packagePath, string targetDirectory);
}

/// <summary>Chooses the extractor that fits the package (Dell/HP: 7z via tar, Lenovo: its own signed EXE).</summary>
public sealed class PackageExtractor(IPayloadExtractor sevenZip, IPayloadExtractor lenovo) : IPayloadExtractor
{
    public bool TryExtract(OemPackage package, string packagePath, string targetDirectory) => package.Extraction switch
    {
        PackageExtraction.SevenZip => sevenZip.TryExtract(package, packagePath, targetDirectory),
        PackageExtraction.LenovoInno => lenovo.TryExtract(package, packagePath, targetDirectory),
        _ => false,
    };
}

/// <summary>
/// Dell Update Packages and HP SoftPaqs are self-extracting EXEs with a 7z archive appended. This class finds the
/// archive by its signature (37 7A BC AF 27 1C) and lets Windows' own bsdtar (System32\tar.exe, libarchive)
/// extract it. The EXE itself is never started. bsdtar refuses absolute paths and ".." by default; sizes and file
/// counts are capped afterwards.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SevenZipPayloadExtractor : IPayloadExtractor
{
    public const long MaxExtractedBytes = 4L * 1024 * 1024 * 1024;
    public const int MaxFiles = 20_000;
    private const int MaxCandidates = 16;
    private static readonly byte[] Signature = [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C];
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    private readonly string _tar;

    public SevenZipPayloadExtractor(string? tarPath = null) =>
        _tar = tarPath ?? Path.Combine(Environment.SystemDirectory, "tar.exe");

    public bool TryExtract(OemPackage package, string packagePath, string targetDirectory) => TryExtract(packagePath, targetDirectory);

    public bool TryExtract(string packagePath, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        var offsets = FindSignatures(packagePath);

        // The real archive is normally the LAST occurrence; earlier ones are constants inside the extractor stub.
        foreach (var offset in offsets.AsEnumerable().Reverse())
        {
            var payload = Path.Combine(targetDirectory, "..", Path.GetFileName(targetDirectory) + ".payload.7z");
            try
            {
                CopyFrom(packagePath, offset, payload);
                if (RunTar(payload, targetDirectory) && WithinLimits(targetDirectory))
                {
                    return true;
                }
            }
            finally
            {
                File.Delete(payload);
            }

            Clear(targetDirectory);
        }

        return false;
    }

    internal static List<long> FindSignatures(string path)
    {
        var offsets = new List<long>();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        var buffer = new byte[1 << 20];
        long position = 0;
        var carry = 0;
        int read;
        while ((read = stream.Read(buffer, carry, buffer.Length - carry)) > 0)
        {
            var length = carry + read;
            var span = buffer.AsSpan(0, length);
            for (var i = 0; i <= length - Signature.Length; i++)
            {
                if (span[i] == Signature[0] && span.Slice(i, Signature.Length).SequenceEqual(Signature))
                {
                    offsets.Add(position + i);
                    if (offsets.Count >= MaxCandidates)
                    {
                        return offsets;
                    }
                }
            }

            // Keep the last bytes so a signature crossing the buffer border is found too.
            carry = Math.Min(Signature.Length - 1, length);
            Array.Copy(buffer, length - carry, buffer, 0, carry);
            position += length - carry;
        }

        return offsets;
    }

    private static void CopyFrom(string source, long offset, string target)
    {
        using var input = File.OpenRead(source);
        input.Seek(offset, SeekOrigin.Begin);
        using var output = File.Create(target);
        input.CopyTo(output);
    }

    private bool RunTar(string archive, string targetDirectory)
    {
        var startInfo = new ProcessStartInfo(_tar)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-xf");
        startInfo.ArgumentList.Add(archive);
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(targetDirectory);

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return false;
        }

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(Timeout))
        {
            process.Kill(entireProcessTree: true);
            return false;
        }

        Task.WaitAll(stdout, stderr);
        return process.ExitCode == 0 && Directory.EnumerateFileSystemEntries(targetDirectory).Any();
    }

    internal static bool WithinLimits(string directory)
    {
        long total = 0;
        var count = 0;
        foreach (var file in new DirectoryInfo(directory).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            if (file.Attributes.HasFlag(FileAttributes.ReparsePoint) || ++count > MaxFiles || (total += file.Length) > MaxExtractedBytes)
            {
                return false;
            }
        }

        return count > 0;
    }

    private static void Clear(string directory)
    {
        foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
        {
            if (entry is DirectoryInfo d)
            {
                d.Delete(recursive: true);
            }
            else
            {
                entry.Delete();
            }
        }
    }
}
