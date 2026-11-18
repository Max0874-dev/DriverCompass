using System.Diagnostics;
using DriverCompass.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DriverCompass.VersionProviders.Net;

/// <summary>The single file extracted from a catalog CAB. Deleting the temp folder on dispose.</summary>
public sealed class ExtractedFile(string path, string? tempDirectory) : IDisposable
{
    public string Path { get; } = path;

    public Stream OpenRead() => File.OpenRead(Path);

    public void Dispose()
    {
        if (tempDirectory is null)
        {
            return;
        }

        try
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Temp folder – Windows cleans it up eventually.
        }
    }
}

public interface ICabExtractor
{
    /// <summary>
    /// Verifies the Authenticode signature (must be valid and issued to one of <paramref name="allowedSigners"/>)
    /// and extracts the single catalog file. Throws <see cref="VersionSourceException"/> on failure.
    /// </summary>
    Task<ExtractedFile> ExtractAsync(string cabPath, IReadOnlyCollection<string> allowedSigners, CancellationToken cancellationToken = default);
}

/// <summary>
/// Extracts with Windows' own <c>expand.exe</c> (absolute path from the system directory) into a new empty temp folder.
/// Nothing from the CAB is ever executed; only one file below the size limit is accepted.
/// </summary>
public sealed class ExpandCabExtractor(string? tempRoot = null, ILogger<ExpandCabExtractor>? logger = null) : ICabExtractor
{
    public const long MaxExtractedBytes = 200L * 1024 * 1024;
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    private readonly string _tempRoot = tempRoot ?? Path.Combine(Path.GetTempPath(), "DriverCompass");
    private readonly ILogger _logger = (ILogger?)logger ?? NullLogger.Instance;

    public async Task<ExtractedFile> ExtractAsync(string cabPath, IReadOnlyCollection<string> allowedSigners, CancellationToken cancellationToken = default)
    {
        var signer = AuthenticodeVerifier.VerifyAndGetSigner(cabPath);
        if (signer is null || !allowedSigners.Contains(signer, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Catalog CAB signature rejected: {Path}, signer {Signer}", cabPath, signer ?? "(none/invalid)");
            throw new VersionSourceException(VersionSourceReasons.SignatureInvalid, $"Untrusted signature on {Path.GetFileName(cabPath)}");
        }

        var outDir = Path.Combine(_tempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);

        try
        {
            var startInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "expand.exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add(cabPath);
            startInfo.ArgumentList.Add("-F:*");
            startInfo.ArgumentList.Add(outDir);

            using var process = Process.Start(startInfo) ?? throw new VersionSourceException(VersionSourceReasons.Unexpected, "expand.exe did not start");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);
            var drainOut = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var drainErr = process.StandardError.ReadToEndAsync(timeout.Token);

            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                await Task.WhenAll(drainOut, drainErr).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }

            var files = Directory.GetFiles(outDir, "*", SearchOption.AllDirectories);
            if (process.ExitCode != 0 || files.Length != 1)
            {
                throw new VersionSourceException(VersionSourceReasons.InvalidData,
                    $"Unexpected CAB content ({files.Length} files, exit code {process.ExitCode})");
            }

            if (new FileInfo(files[0]).Length > MaxExtractedBytes)
            {
                throw new VersionSourceException(VersionSourceReasons.InvalidData, "Extracted catalog too large");
            }

            return new ExtractedFile(files[0], outDir);
        }
        catch
        {
            new ExtractedFile(outDir, outDir).Dispose();
            throw;
        }
    }
}
