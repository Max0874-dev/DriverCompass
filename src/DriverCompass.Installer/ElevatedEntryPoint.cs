using System.Runtime.Versioning;
using System.Security.Principal;
using DriverCompass.Core.Installation;
using DriverCompass.Installer.Windows;
using DriverCompass.Scanner.Windows;
using Microsoft.Extensions.Logging;

namespace DriverCompass.Installer;

/// <summary>
/// Entry for <c>DriverCompass.exe --elevated --request &lt;file&gt;</c>. Runs without any UI.
/// Exit codes: 0 = result written (outcome inside), 2 = invalid arguments/request, 5 = not elevated, 3 = internal error.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ElevatedEntryPoint
{
    public const int ExitOk = 0;
    public const int ExitInvalid = 2;
    public const int ExitError = 3;
    public const int ExitNotElevated = 5;

    public static bool IsElevatedInvocation(IReadOnlyList<string> args) =>
        args.Count > 0 && string.Equals(args[0], InstallProtocol.ElevatedSwitch, StringComparison.Ordinal);

    public static int Run(IReadOnlyList<string> args, ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger(typeof(ElevatedEntryPoint));
        if (args.Count != 3 || !IsElevatedInvocation(args) || args[1] != InstallProtocol.RequestSwitch)
        {
            logger.LogError("Invalid elevated invocation");
            return ExitInvalid;
        }

        using (var identity = WindowsIdentity.GetCurrent())
        {
            if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            {
                logger.LogError("Elevated entry started without administrator rights");
                return ExitNotElevated;
            }
        }

        var request = InstallProtocol.ReadRequest(args[2], out var error);
        if (request is null)
        {
            logger.LogError("Rejected install request: {Error}", error);
            return ExitInvalid;
        }

        try
        {
            using var windowsUpdate = new ComWindowsUpdateInstaller();
            var engine = new InstallerEngine(
                new WmiDeviceQuery(new WmiDeviceScanner(new WmiQueryRunner(), loggerFactory.CreateLogger<WmiDeviceScanner>())),
                windowsUpdate,
                new WmiRestorePointCreator(),
                new WindowsDriverStore(),
                new ProtectedInstallStore(),
                logger: loggerFactory.CreateLogger<InstallerEngine>());

            var result = engine.Execute(request);
            logger.LogInformation("Request {Request} ({Action}) finished: {Outcome} {Message}", request.RequestId, request.Action, result.Outcome, result.Message);
            return ExitOk;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Elevated request {Request} failed", request.RequestId);
            return ExitError;
        }
    }
}
