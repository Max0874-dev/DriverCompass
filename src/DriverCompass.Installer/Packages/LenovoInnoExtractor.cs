using System.Diagnostics;
using System.Text.RegularExpressions;
using DriverCompass.VersionProviders.Packages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DriverCompass.Installer.Packages;

/// <summary>Starts a process and waits; abstracted so the exact command line can be tested.</summary>
public interface IExtractorProcessRunner
{
    /// <summary>Exit code, or <c>null</c> if the process had to be killed after <paramref name="timeout"/>.</summary>
    int? Run(string fileName, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeout);
}

public sealed class ExtractorProcessRunner : IExtractorProcessRunner
{
    public int? Run(string fileName, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return null;
        }

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeout))
        {
            process.Kill(entireProcessTree: true);
            return null;
        }

        Task.WaitAll(stdout, stderr);
        return process.ExitCode;
    }
}

/// <summary>
/// Lenovo packages are Inno Setup EXEs. The ONLY place where DriverCompass runs vendor code elevated:
/// Lenovo's own, already SHA-256- and Authenticode-verified EXE extracts itself, exactly like Lenovo System Update
/// does. The EXE is copied into the admin-only work folder (nobody can plant DLLs next to it) and started with
/// arguments built here – never with text from the descriptor. Nothing is installed by this step.
/// </summary>
public sealed partial class LenovoInnoExtractor(IExtractorProcessRunner runner, ILogger<LenovoInnoExtractor>? logger = null) : IPayloadExtractor
{
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);
    private readonly ILogger _logger = (ILogger?)logger ?? NullLogger.Instance;

    public bool TryExtract(OemPackage package, string packagePath, string targetDirectory)
    {
        if (package.Extraction != PackageExtraction.LenovoInno ||
            package.InstallerFileName is not { } name || !InstallerNameRegex().IsMatch(name))
        {
            return false;
        }

        var target = Path.GetFullPath(targetDirectory);
        var work = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(target);

        var exe = Path.Combine(work, name);
        File.Copy(packagePath, exe, overwrite: true);
        try
        {
            string[] arguments = ["/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/DIR=" + target, "/EXTRACT=YES"];
            _logger.LogInformation("Running Lenovo's signed extractor {Exe} (extract only)", name);
            var exit = runner.Run(exe, arguments, work, Timeout);
            if (exit != 0)
            {
                _logger.LogWarning("Lenovo extractor ended with {Exit}", exit?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "timeout");
                return false;
            }

            return Directory.EnumerateFileSystemEntries(target).Any() && SevenZipPayloadExtractor.WithinLimits(target);
        }
        finally
        {
            File.Delete(exe);
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9_\-]{1,64}\.exe$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InstallerNameRegex();
}
