using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using DriverCompass.Core.Abstractions;
using DriverCompass.Core.Installation;
using DriverCompass.Core.Models;
using Microsoft.Extensions.Logging;

namespace DriverCompass.Installer.Client;

/// <summary>Starts a process elevated (UAC). Abstracted for tests.</summary>
public interface IProcessLauncher
{
    /// <summary>Returns the exit code; throws <see cref="OperationCanceledException"/> if the user declines the UAC prompt.</summary>
    Task<int> RunElevatedAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}

public sealed class UacProcessLauncher : IProcessLauncher
{
    private const int ErrorCancelled = 1223;

    public async Task<int> RunElevatedAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = true,
            Verb = "runas",
            // ShellExecute takes one argument string; every argument is quoted.
            Arguments = string.Join(' ', arguments.Select(a => "\"" + a.Replace("\"", string.Empty, StringComparison.Ordinal) + "\"")),
        };

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            throw new OperationCanceledException("UAC prompt declined", ex);
        }

        if (process is null)
        {
            throw new InvalidOperationException("Elevated process did not start");
        }

        using (process)
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode;
        }
    }
}

/// <summary>
/// Unelevated side of stage 3: writes the request (IDs only), starts <c>DriverCompass.exe --elevated</c>
/// via UAC, waits, and reads the result from the admin-only folder.
/// </summary>
public sealed class ElevatedInstallerClient(IProcessLauncher launcher, ILogger<ElevatedInstallerClient> logger, string? executablePath = null, string? protectedDirectory = null)
    : IElevatedInstallerClient
{
    private readonly string _exe = executablePath ?? Environment.ProcessPath ?? throw new InvalidOperationException("Unknown executable path");
    private readonly string _protected = protectedDirectory ?? InstallProtocol.ProtectedDirectory;

    public async Task<InstallResult> RunAsync(InstallRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (InstallProtocol.Validate(request) is { } error)
        {
            return new InstallResult { RequestId = request.RequestId, Outcome = InstallOutcome.Failed, Message = error };
        }

        Directory.CreateDirectory(InstallProtocol.RequestDirectory);
        var requestPath = Path.Combine(InstallProtocol.RequestDirectory, request.RequestId.ToString("N") + ".json");
        await File.WriteAllTextAsync(requestPath, InstallProtocol.Serialize(request), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);

        try
        {
            logger.LogInformation("Starting elevated {Action} request {Request}", request.Action, request.RequestId);
            var exit = await launcher.RunElevatedAsync(_exe, [InstallProtocol.ElevatedSwitch, InstallProtocol.RequestSwitch, requestPath], cancellationToken).ConfigureAwait(false);

            var result = InstallProtocol.ReadResult(InstallProtocol.ResultPath(_protected, request.RequestId), request.RequestId);
            if (result is null)
            {
                logger.LogWarning("Elevated process exited with {Exit} without a result", exit);
                return new InstallResult { RequestId = request.RequestId, Outcome = InstallOutcome.Failed, Message = $"NoResult:{exit}" };
            }

            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("UAC prompt declined");
            return new InstallResult { RequestId = request.RequestId, Outcome = InstallOutcome.Cancelled };
        }
        finally
        {
            try
            {
                File.Delete(requestPath);
            }
            catch (IOException)
            {
                // Harmless leftover in the user's own folder.
            }
        }
    }
}

