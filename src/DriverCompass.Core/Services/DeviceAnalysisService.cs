using DriverCompass.Core.Abstractions;
using DriverCompass.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DriverCompass.Core.Services;

/// <summary>Scan → evaluate → resolve links → sort (problems first).</summary>
public sealed class DeviceAnalysisService : IDeviceAnalysisService
{
    private readonly IDeviceScanner _scanner;
    private readonly IVendorCatalogProvider _catalogProvider;
    private readonly IDeviceStatusEvaluator _evaluator;
    private readonly ILinkResolver _linkResolver;
    private readonly ILogger<DeviceAnalysisService> _logger;
    private readonly Lock _gate = new();
    private Task<CatalogLoadResult>? _catalogTask;

    public DeviceAnalysisService(
        IDeviceScanner scanner,
        IVendorCatalogProvider catalogProvider,
        IDeviceStatusEvaluator evaluator,
        ILinkResolver linkResolver,
        ILogger<DeviceAnalysisService>? logger = null)
    {
        _scanner = scanner;
        _catalogProvider = catalogProvider;
        _evaluator = evaluator;
        _linkResolver = linkResolver;
        _logger = logger ?? NullLogger<DeviceAnalysisService>.Instance;
    }

    /// <summary>Forces the vendor catalog to be loaded again on the next analysis.</summary>
    public void InvalidateCatalog()
    {
        lock (_gate)
        {
            _catalogTask = null;
        }
    }

    public async Task<AnalysisResult> AnalyzeAsync(AnalysisOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var catalogTask = GetCatalogTask(cancellationToken);
        var scanTask = _scanner.ScanAsync(cancellationToken);
        await Task.WhenAll(catalogTask, scanTask).ConfigureAwait(false);

        var catalog = await catalogTask.ConfigureAwait(false);
        var scan = await scanTask.ConfigureAwait(false);
        var context = new LinkContext(scan.System, catalog.Catalog, options);

        var devices = scan.Devices
            .Select(d => _evaluator.Evaluate(d, catalog.Catalog))
            .Select(a => a with { Links = _linkResolver.Resolve(a, context) })
            .OrderBy(a => a, AssessmentOrder.Instance)
            .ToList();

        _logger.LogInformation(
            "Analysis finished: scanner={Scanner}, devices={Count}, errors={Errors}, missing={Missing}, generic={Generic}, catalog={Source} {Version}",
            _scanner.Id,
            devices.Count,
            devices.Count(d => d.Status == DeviceStatus.Error),
            devices.Count(d => d.Status == DeviceStatus.MissingDriver),
            devices.Count(d => d.Status == DeviceStatus.GenericDriver),
            catalog.Info.Source,
            catalog.Info.DataVersion);

        return new AnalysisResult(scan, devices, catalog.Info, DateTimeOffset.Now);
    }

    private Task<CatalogLoadResult> GetCatalogTask(CancellationToken ct)
    {
        lock (_gate)
        {
            if (_catalogTask is null || _catalogTask.IsFaulted || _catalogTask.IsCanceled)
            {
                _catalogTask = _catalogProvider.LoadAsync(ct);
            }

            return _catalogTask;
        }
    }
}

/// <summary>Status (problems first) → device class → name.</summary>
public sealed class AssessmentOrder : IComparer<DeviceAssessment>
{
    public static AssessmentOrder Instance { get; } = new();

    public int Compare(DeviceAssessment? x, DeviceAssessment? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        var c = x.Status.CompareTo(y.Status);
        if (c != 0)
        {
            return c;
        }

        c = string.Compare(x.Device.DeviceClass, y.Device.DeviceClass, StringComparison.OrdinalIgnoreCase);
        return c != 0 ? c : string.Compare(x.DisplayName, y.DisplayName, StringComparison.CurrentCultureIgnoreCase);
    }
}
