#if !STORE_EDITION
using DriverCompass.Core;
using DriverCompass.Core.Installation;
using DriverCompass.Installer;
using Microsoft.Extensions.Logging;
using Serilog;
#endif
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace DriverCompass.App;

/// <summary>
/// Custom entry point (DISABLE_XAML_GENERATED_MAIN):
/// <c>DriverCompass.exe --elevated --request &lt;file&gt;</c> runs the stage-3 installer engine in this separate,
/// UAC-elevated process WITHOUT starting any UI. Every other start is the normal, unelevated app.
/// The Microsoft Store edition (STORE_EDITION) has no installer and therefore no elevated entry point.
/// </summary>
public static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
#if !STORE_EDITION
        if (ElevatedEntryPoint.IsElevatedInvocation(args))
        {
            return RunElevated(args);
        }
#endif

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(callbackParams =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
        return 0;
    }

#if !STORE_EDITION
    private static int RunElevated(string[] args)
    {
        try
        {
            // Secure %ProgramData%DriverCompass before anything (including the log) is written there.
            DriverCompass.Installer.Windows.ProtectedInstallStore.Prepare();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            return ElevatedEntryPoint.ExitError;
        }

        // The elevated process logs into the admin-only folder, next to the install log.
        using var logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(InstallProtocol.ProtectedDirectory, "logs", "elevated-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                formatProvider: System.Globalization.CultureInfo.InvariantCulture)
            .CreateLogger();
        using var factory = LoggerFactory.Create(b => b.AddSerilog(logger));
        factory.CreateLogger("DriverCompass").LogInformation("{App} {Version} elevated helper started", AppInfo.DisplayName, AppInfo.Version);
        return ElevatedEntryPoint.Run(args, factory);
    }
#endif
}
