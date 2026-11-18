using DriverCompass.App.Services;
using DriverCompass.App.ViewModels;
using DriverCompass.Core;
using DriverCompass.Core.Abstractions;
using DriverCompass.Core.Evaluation;
using DriverCompass.Core.Links;
using DriverCompass.Core.Reporting;
using DriverCompass.Core.Scanning;
using DriverCompass.Core.Services;
using DriverCompass.Data;
using DriverCompass.Scanner.Windows;
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
        services.AddSingleton(sp => new MainViewModel(
            sp.GetRequiredService<IDeviceAnalysisService>(),
            sp.GetRequiredService<ISettingsService>(),
            sp.GetServices<IReportExporter>(),
            sp.GetRequiredService<ILogger<MainViewModel>>(),
            isMock));

        return services.BuildServiceProvider();
    }
}
