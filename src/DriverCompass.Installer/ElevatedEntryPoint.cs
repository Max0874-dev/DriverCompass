using System.Runtime.Versioning;
using System.Security.Principal;
using DriverCompass.Core.Installation;
using DriverCompass.Installer.Packages;
using DriverCompass.Installer.Sources;
using DriverCompass.Installer.Windows;
using DriverCompass.Scanner.Windows;
using DriverCompass.VersionProviders;
using DriverCompass.VersionProviders.Dell;
using DriverCompass.VersionProviders.Hp;
using DriverCompass.VersionProviders.Net;
using DriverCompass.VersionProviders.Packages;
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
            var store = new ProtectedInstallStore();
            var root = InstallProtocol.ProtectedDirectory;
            var wmi = new WmiDeviceQuery(new WmiDeviceScanner(new WmiQueryRunner(), loggerFactory.CreateLogger<WmiDeviceScanner>()));
            var driverStore = new WindowsDriverStore();
            using var windowsUpdate = new ComWindowsUpdateInstaller();

            // Catalogs and packages are cached and extracted ONLY inside the admin-only folder – never in the user's profile.
            using var http = VersionProviderFactory.CreateHttpClient();
            var downloader = new CatalogDownloader(http, Path.Combine(root, "cache"), logger: loggerFactory.CreateLogger<CatalogDownloader>());
            var cabs = new ExpandCabExtractor(Path.Combine(root, "work"), loggerFactory.CreateLogger<ExpandCabExtractor>());
            var signatures = new AuthenticodeFileVerifier();
            var payloads = new SevenZipPayloadExtractor();

            IPackageSource Oem(IOemPackageCatalog catalog) => new OemPackageSource(
                catalog, wmi, downloader, signatures, payloads, driverStore, Path.Combine(root, "work"), loggerFactory.CreateLogger<OemPackageSource>());

            var engine = new InstallerEngine(
                wmi,
                [
                    new WindowsUpdateSource(windowsUpdate),
                    Oem(new DellVersionProvider(downloader, cabs)),
                    Oem(new HpVersionProvider(downloader, cabs)),
                ],
                new WmiRestorePointCreator(),
                driverStore,
                store,
                logger: loggerFactory.CreateLogger<InstallerEngine>());

            var result = engine.ExecuteAsync(request).GetAwaiter().GetResult();
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
