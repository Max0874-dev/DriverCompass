using DriverCompass.App.Services;
using DriverCompass.App.ViewModels;
using DriverCompass.Core;
using DriverCompass.Core.Abstractions;
using DriverCompass.Core.Evaluation;
using DriverCompass.Core.Installation;
using DriverCompass.Core.Links;
using DriverCompass.Core.Reporting;
using DriverCompass.Core.Scanning;
using DriverCompass.Core.Models;
using DriverCompass.Core.Services;
using DriverCompass.Core.Versions;
using DriverCompass.Data;
using DriverCompass.Scanner.Windows;
using DriverCompass.VersionProviders;
using DriverCompass.Installer.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Serilog;

namespace DriverCompass.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => Log.Fatal(e.Exception, "Unhandled UI exception");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception");
        TaskScheduler.UnobservedTaskException += (_, e) => Log.Error(e.Exception, "Unobserved task exception");
    }

    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var isMock = Environment.GetCommandLineArgs().Any(a => a.Equals("--mock", StringComparison.OrdinalIgnoreCase));

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(AppInfo.DataDirectory, "logs", "drivercompass-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                formatProvider: System.Globalization.CultureInfo.InvariantCulture)
            .CreateLogger();
        Log.Information("{App} {Version} starting (mock={Mock})", AppInfo.DisplayName, AppInfo.Version, isMock);

        var settings = new JsonSettingsService(JsonSettingsService.DefaultPath);
        Localizer.Current = new Localizer(Localizer.Resolve(settings.Current.Language));

        Services = ConfigureServices(settings, isMock);
        var window = new MainWindow(Services.GetRequiredService<MainViewModel>());
        _window = window;
        window.Activate();
    }

    private static ServiceProvider ConfigureServices(ISettingsService settings, bool isMock)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddSerilog(dispose: true));
        services.AddSingleton(settings);

        // The only network use in stage 1: downloading the public vendor list. Generic user agent, no device data.
        services.AddSingleton(_ =>
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppInfo.TechnicalName}/{AppInfo.Version}");
            return http;
        });

        services.AddSingleton<IVendorCatalogValidator, VendorCatalogValidator>();
        services.AddSingleton<IVendorCatalogProvider, SettingsAwareCatalogProvider>();
        services.AddSingleton<IDeviceNameLookup>(_ => EmbeddedData.CreatePciIdsDatabase());
        services.AddSingleton<IDeviceStatusEvaluator>(sp => new DeviceStatusEvaluator(sp.GetRequiredService<IDeviceNameLookup>()));
        services.AddSingleton<ILinkResolver>(_ => LinkResolver.CreateDefault());

        if (isMock)
        {
            services.AddSingleton<IDeviceScanner>(_ => new MockDeviceScanner());
        }
        else
        {
            services.AddSingleton<IDeviceScanner>(sp => new WmiDeviceScanner(new WmiQueryRunner(), sp.GetRequiredService<ILogger<WmiDeviceScanner>>()));
        }

        services.AddSingleton<DeviceAnalysisService>();
        services.AddSingleton<IDeviceAnalysisService>(sp => sp.GetRequiredService<DeviceAnalysisService>());
        services.AddSingleton<IReportExporter, HtmlReportExporter>();
        services.AddSingleton<IReportExporter, CsvReportExporter>();

        // Stage 2: version sources. Nothing is contacted until the user enables a source and clicks "Check versions".
        if (isMock)
        {
            foreach (var id in VersionSourceIds.All)
            {
                services.AddSingleton<IVersionProvider>(_ => new MockVersionProvider(id));
            }
        }
        else
        {
            services.AddSingleton(_ => new VersionCatalogHttp(VersionProviderFactory.CreateHttpClient()));
            services.AddSingleton<IEnumerable<IVersionProvider>>(sp =>
                VersionProviderFactory.CreateAll(sp.GetRequiredService<VersionCatalogHttp>().Client, sp.GetRequiredService<ILoggerFactory>()));
        }

        services.AddSingleton<IVersionCheckService>(sp =>
            new VersionCheckService(sp.GetRequiredService<IEnumerable<IVersionProvider>>(), sp.GetRequiredService<ILogger<VersionCheckService>>()));

        // Stage 3: installation runs in a separate, UAC-elevated process ("DriverCompass.exe --elevated ...").
        // The demo mode only simulates it – no UAC prompt, no change to Windows.
        if (isMock)
        {
            var mockInstaller = new MockInstallerClient();
            services.AddSingleton<IElevatedInstallerClient>(mockInstaller);
            services.AddSingleton<IInstallLog>(mockInstaller);
        }
        else
        {
            services.AddSingleton<IProcessLauncher, UacProcessLauncher>();
            services.AddSingleton<IElevatedInstallerClient>(sp =>
                new ElevatedInstallerClient(sp.GetRequiredService<IProcessLauncher>(), sp.GetRequiredService<ILogger<ElevatedInstallerClient>>()));
            services.AddSingleton<IInstallLog>(_ => new InstallLogFile());
        }

        services.AddSingleton(sp => new MainViewModel(
            sp.GetRequiredService<IDeviceAnalysisService>(),
            sp.GetRequiredService<IVersionCheckService>(),
            sp.GetRequiredService<IElevatedInstallerClient>(),
            sp.GetRequiredService<IInstallLog>(),
            sp.GetRequiredService<ISettingsService>(),
            sp.GetServices<IReportExporter>(),
            sp.GetRequiredService<ILogger<MainViewModel>>(),
            isMock));

        return services.BuildServiceProvider();
    }

    /// <summary>Separate HttpClient for vendor catalogs (no automatic redirects – each hop is checked against the allow-list).</summary>
    private sealed record VersionCatalogHttp(HttpClient Client);
}
