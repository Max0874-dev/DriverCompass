using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DriverCompass.App.Services;
using DriverCompass.Core;
using DriverCompass.Core.Abstractions;
using DriverCompass.Core.Evaluation;
using DriverCompass.Core.Models;
using DriverCompass.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;

namespace DriverCompass.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private static readonly DeviceStatus?[] StatusFilterValues =
        [null, null, DeviceStatus.Error, DeviceStatus.MissingDriver, DeviceStatus.GenericDriver, DeviceStatus.Disabled, DeviceStatus.Ok];

    private readonly IDeviceAnalysisService _analysis;
    private readonly IVersionCheckService _versions;
    private readonly ISettingsService _settings;
    private readonly IReadOnlyDictionary<string, IReportExporter> _exporters;
    private readonly ILogger<MainViewModel> _logger;
    private List<DeviceItemViewModel> _all = [];
    private AnalysisResult? _result;

    public MainViewModel(
        IDeviceAnalysisService analysis,
        IVersionCheckService versions,
        ISettingsService settings,
        IEnumerable<IReportExporter> exporters,
        ILogger<MainViewModel> logger,
        bool isMockMode)
    {
        _analysis = analysis;
        _versions = versions;
        _settings = settings;
        _exporters = exporters.ToDictionary(e => e.FormatId, StringComparer.OrdinalIgnoreCase);
        _logger = logger;
        IsMockMode = isMockMode;
        IsExpertMode = settings.Current.StartView == ViewMode.Expert;
        ShowSystemDevices = settings.Current.ShowSystemDevices;

        StatusFilterOptions =
        [
            Localizer.T("Filter_AllStatuses"),
            Localizer.T("Filter_Problems"),
            .. StatusFilterValues.Skip(2).Select(s => Core.Reporting.AssessmentTexts.Status(Localizer.Current, s!.Value)),
            Localizer.T("Filter_NewerAvailable"),
        ];
        StatusFilterIndex = 0;
        ClassOptions.Add(Localizer.T("Filter_AllClasses"));
        SelectedClassIndex = 0;
    }

    /// <summary>Set by the window after it is created.</summary>
    public IShellService? Shell { get; set; }

    public bool IsMockMode { get; }

    public string Title => AppInfo.DisplayName;

    public ObservableCollection<DeviceGroupViewModel> SimpleGroups { get; } = [];
    public ObservableCollection<DeviceItemViewModel> ExpertItems { get; } = [];
    public ObservableCollection<string> ClassOptions { get; } = [];
    public IReadOnlyList<string> StatusFilterOptions { get; }

    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial bool IsExpertMode { get; set; }
    [ObservableProperty] public partial bool ShowSystemDevices { get; set; }
    [ObservableProperty] public partial string Summary { get; set; } = string.Empty;
    [ObservableProperty] public partial string SummaryDetail { get; set; } = string.Empty;
    [ObservableProperty] public partial bool AllGood { get; set; }
    [ObservableProperty] public partial string SystemText { get; set; } = string.Empty;
    [ObservableProperty] public partial string CatalogText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsInfoOpen { get; set; }
    [ObservableProperty] public partial string InfoMessage { get; set; } = string.Empty;
    [ObservableProperty] public partial InfoBarSeverity InfoSeverity { get; set; }
    [ObservableProperty] public partial string? InfoActionPath { get; set; }
    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;
    [ObservableProperty] public partial int StatusFilterIndex { get; set; }
    [ObservableProperty] public partial int SelectedClassIndex { get; set; }
    [ObservableProperty] public partial string ExpertCountText { get; set; } = string.Empty;
    [ObservableProperty] public partial DeviceItemViewModel? SelectedItem { get; set; }
    [ObservableProperty] public partial string SortColumn { get; set; } = "Status";
    [ObservableProperty] public partial bool SortAscending { get; set; } = true;
    [ObservableProperty] public partial bool IsCheckingVersions { get; set; }
    [ObservableProperty] public partial string VersionProgressText { get; set; } = string.Empty;
    [ObservableProperty] public partial string VersionText { get; set; } = string.Empty;

    public bool HasInfoAction => !string.IsNullOrEmpty(InfoActionPath);

    public string CatalogVersion => _result?.Catalog.DataVersion ?? "–";

    /// <summary>For the settings page: does a version source cover this PC? <c>null</c> before the first scan.</summary>
    public bool? IsVersionSourceApplicable(string sourceId) =>
        _result is null ? null : _versions.IsApplicable(sourceId, _result.Scan.System);

    /// <summary>
    /// "Check versions": runs only the sources the user switched on in the settings. Nothing is sent before this click.
    /// </summary>
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task CheckVersionsAsync(CancellationToken cancellationToken)
    {
        if (_result is null || IsBusy || IsCheckingVersions)
        {
            return;
        }

        var enabled = _settings.Current.EnabledVersionSources.ToHashSet(StringComparer.Ordinal);
        if (enabled.Count == 0)
        {
            ShowInfo(Localizer.T("Versions_NoSourceHint"), InfoBarSeverity.Informational);
            return;
        }

        IsCheckingVersions = true;
        IsInfoOpen = false;
        var progress = new Progress<VersionCheckProgress>(p =>
            VersionProgressText = Localizer.F("Busy_CheckingVersions", Core.Reporting.AssessmentTexts.VersionSource(Localizer.Current, p.SourceId)));

        try
        {
            var run = await _versions.CheckAllAsync(_result.Devices, _result.Scan.System, enabled, progress, cancellationToken);
            ApplyVersions(run);
        }
        catch (OperationCanceledException)
        {
            ShowInfo(Localizer.T("Versions_Cancelled"), InfoBarSeverity.Informational);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Version check failed");
            ShowInfo(Localizer.F("Versions_Failed", ex.Message), InfoBarSeverity.Error);
        }
        finally
        {
            IsCheckingVersions = false;
            VersionProgressText = string.Empty;
        }
    }

    private void ApplyVersions(VersionCheckRun run)
    {
        if (_result is null)
        {
            return;
        }

        var selectedId = SelectedItem?.InstanceId;
        _result = _result with { Devices = run.Devices };
        BuildItems(_result);
        RebuildSimpleGroups();
        ApplyExpertFilter();
        SelectedItem = selectedId is null ? null : ExpertItems.FirstOrDefault(i => i.InstanceId == selectedId);

        var t = Localizer.Current;
        VersionText = string.Join("  ·  ", run.Sources.Select(s =>
        {
            var name = Core.Reporting.AssessmentTexts.VersionSource(t, s.SourceId);
            return s.Outcome switch
            {
                VersionSourceOutcome.Ok when s.SourceId == VersionSourceIds.WindowsUpdate => Localizer.F("VersionSourceStatus_Ok_windowsupdate", name, s.Detail),
                VersionSourceOutcome.Ok => Localizer.F("VersionSourceStatus_Ok", name, s.Detail),
                VersionSourceOutcome.NotApplicable => Localizer.F("VersionSourceStatus_NotApplicable", name, Localizer.T($"VersionReason_{s.Detail}")),
                _ => Localizer.F("VersionSourceStatus_Failed", name, Localizer.T($"VersionReason_{s.Detail}")),
            };
        }));

        var failed = run.Sources.Any(s => s.Outcome == VersionSourceOutcome.Failed);
        ShowInfo(
            Localizer.F("Versions_Summary", run.NewerCount, run.UpToDateCount, run.UnknownCount) + (failed ? "  ·  " + VersionText : string.Empty),
            failed ? InfoBarSeverity.Warning : run.NewerCount > 0 ? InfoBarSeverity.Informational : InfoBarSeverity.Success);
    }

    partial void OnSearchTextChanged(string value) => ApplyExpertFilter();
    partial void OnStatusFilterIndexChanged(int value) => ApplyExpertFilter();
    partial void OnSelectedClassIndexChanged(int value) => ApplyExpertFilter();
    partial void OnInfoActionPathChanged(string? value) => OnPropertyChanged(nameof(HasInfoAction));

    partial void OnShowSystemDevicesChanged(bool value)
    {
        RebuildSimpleGroups();
        ApplyExpertFilter();
    }

    [RelayCommand]
    private void ShowSimple() => IsExpertMode = false;

    [RelayCommand]
    private void ShowExpert() => IsExpertMode = true;

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsBusy || IsCheckingVersions)
        {
            return;
        }

        IsBusy = true;
        IsInfoOpen = false;
        Summary = Localizer.T("Busy_Scanning");
        SummaryDetail = string.Empty;
        try
        {
            var options = new AnalysisOptions { UseSerialNumberInLinks = _settings.Current.UseSerialNumberInLinks };
            var result = await _analysis.AnalyzeAsync(options);
            Apply(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scan failed");
            Summary = string.Empty;
            ShowInfo(Localizer.F("Error_ScanFailed", ex.Message), InfoBarSeverity.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Sort(string column)
    {
        if (SortColumn == column)
        {
            SortAscending = !SortAscending;
        }
        else
        {
            SortColumn = column;
            SortAscending = true;
        }

        ApplyExpertFilter();
    }

    [RelayCommand]
    private async Task ExportAsync(string format)
    {
        if (_result is null || Shell is null || !_exporters.TryGetValue(format, out var exporter))
        {
            return;
        }

        var label = Localizer.T(format == "csv" ? "Export_FileType_Csv" : "Export_FileType_Html");
        var name = $"{Localizer.T("Export_FileName")}-{DateTime.Now:yyyy-MM-dd}";
        var path = await Shell.PickSaveFileAsync(name, label, exporter.FileExtension);
        if (path is null)
        {
            return;
        }

        try
        {
            var options = new ReportOptions
            {
                IncludeSystemDevices = ShowSystemDevices,
                IncludeSerialNumber = _settings.Current.IncludeSerialNumberInExports,
            };

            await using (var stream = File.Create(path))
            {
                await exporter.ExportAsync(_result, options, Localizer.Current, stream);
            }

            _logger.LogInformation("Exported {Format} report", format);
            ShowInfo(Localizer.F("Export_Saved", path), InfoBarSeverity.Success, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Export failed");
            ShowInfo(Localizer.F("Export_Failed", ex.Message), InfoBarSeverity.Error);
        }
    }

    [RelayCommand]
    private void OpenInfoAction()
    {
        if (InfoActionPath is not null)
        {
            Launcher.OpenLocal(InfoActionPath);
        }
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        if (Shell is null)
        {
            return;
        }

        var before = _settings.Current;
        if (!await Shell.ShowSettingsAsync())
        {
            return;
        }

        var after = _settings.Current;
        ShowSystemDevices = after.ShowSystemDevices;

        if (before.UpdateCatalogOnline != after.UpdateCatalogOnline && _analysis is DeviceAnalysisService service)
        {
            service.InvalidateCatalog();
        }

        if (before.UseSerialNumberInLinks != after.UseSerialNumberInLinks || before.UpdateCatalogOnline != after.UpdateCatalogOnline)
        {
            await ScanAsync();
        }

        if (!string.Equals(Localizer.Resolve(after.Language), Localizer.Current.Language, StringComparison.OrdinalIgnoreCase))
        {
            ShowInfo(Localizer.T("Settings_LanguageRestart"), InfoBarSeverity.Informational);
        }
    }

    private void BuildItems(AnalysisResult result)
    {
        var lang = Localizer.Current.Language;
        _all = result.Devices.Select(d => new DeviceItemViewModel(d, Localizer.Current, lang)).ToList();
    }

    private void Apply(AnalysisResult result)
    {
        // A new scan starts without version results – they belong to the previous device list.
        _result = result;
        VersionText = string.Empty;
        BuildItems(result);

        var sys = result.Scan.System;
        var pc = string.Join(' ', new[] { SystemInfoSanitizer.Clean(sys.Manufacturer), SystemInfoSanitizer.Clean(sys.Model) }.Where(s => s is not null));
        var board = string.Join(' ', new[] { SystemInfoSanitizer.Clean(sys.BaseBoardManufacturer), SystemInfoSanitizer.Clean(sys.BaseBoardProduct) }.Where(s => s is not null));
        var unknown = Localizer.T("Status_Unknown");
        SystemText = $"{Localizer.F("Status_PcLabel", pc.Length > 0 ? pc : unknown)}   ·   {Localizer.F("Status_BoardLabel", board.Length > 0 ? board : unknown)}";
        CatalogText = Localizer.F("Status_CatalogLabel", result.Catalog.DataVersion, Localizer.T($"CatalogSource_{result.Catalog.Source}"));

        var attention = result.AttentionCount;
        AllGood = attention == 0;
        Summary = attention switch
        {
            0 => Localizer.T("Summary_AllGood"),
            1 => Localizer.T("Summary_Attention_One"),
            _ => Localizer.F("Summary_Attention_Many", attention),
        };
        SummaryDetail = Localizer.F("Summary_Counts", result.Devices.Count(d => !d.IsSystemDevice), result.Devices.Count(d => d.IsSystemDevice));

        RebuildClassOptions();
        RebuildSimpleGroups();
        ApplyExpertFilter();

        if (result.Catalog.Warnings.Count > 0)
        {
            ShowInfo(Localizer.T(result.Catalog.Warnings[0]), InfoBarSeverity.Warning);
        }
    }

    private void RebuildSimpleGroups()
    {
        SimpleGroups.Clear();
        foreach (var group in _all
                     .Where(d => ShowSystemDevices || !d.IsSystemDevice)
                     .GroupBy(d => d.Status)
                     .OrderBy(g => g.Key))
        {
            SimpleGroups.Add(new DeviceGroupViewModel(group.Key, Core.Reporting.AssessmentTexts.Status(Localizer.Current, group.Key), group.OrderByDescending(d => d.IsNewerAvailable)));
        }
    }

    private void RebuildClassOptions()
    {
        var selected = SelectedClassIndex > 0 && SelectedClassIndex < ClassOptions.Count ? ClassOptions[SelectedClassIndex] : null;
        var classes = _all.Select(d => d.Category).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase).ToList();

        ClassOptions.Clear();
        ClassOptions.Add(Localizer.T("Filter_AllClasses"));
        foreach (var c in classes)
        {
            ClassOptions.Add(c);
        }

        var index = selected is null ? 0 : ClassOptions.IndexOf(selected);
        SelectedClassIndex = index < 0 ? 0 : index;
    }

    private void ApplyExpertFilter()
    {
        IEnumerable<DeviceItemViewModel> items = _all.Where(d => ShowSystemDevices || !d.IsSystemDevice);

        if (StatusFilterIndex == 1)
        {
            items = items.Where(d => d.NeedsAttention);
        }
        else if (StatusFilterIndex == StatusFilterValues.Length)
        {
            items = items.Where(d => d.IsNewerAvailable);
        }
        else if (StatusFilterIndex > 1 && StatusFilterIndex < StatusFilterValues.Length)
        {
            var status = StatusFilterValues[StatusFilterIndex];
            items = items.Where(d => d.Status == status);
        }

        if (SelectedClassIndex > 0 && SelectedClassIndex < ClassOptions.Count)
        {
            var category = ClassOptions[SelectedClassIndex];
            items = items.Where(d => string.Equals(d.Category, category, StringComparison.CurrentCultureIgnoreCase));
        }

        var terms = (SearchText ?? string.Empty).ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length > 0)
        {
            items = items.Where(d => terms.All(t => d.SearchIndex.Contains(t, StringComparison.Ordinal)));
        }

        var sorted = Sort(items).ToList();
        var selected = SelectedItem;

        ExpertItems.Clear();
        foreach (var item in sorted)
        {
            ExpertItems.Add(item);
        }

        SelectedItem = selected is not null && sorted.Contains(selected) ? selected : null;
        ExpertCountText = Localizer.F("Expert_Count", sorted.Count, _all.Count(d => ShowSystemDevices || !d.IsSystemDevice));
    }

    private IEnumerable<DeviceItemViewModel> Sort(IEnumerable<DeviceItemViewModel> items)
    {
        var cmp = StringComparer.CurrentCultureIgnoreCase;
        IOrderedEnumerable<DeviceItemViewModel> ordered = SortColumn switch
        {
            "Name" => Order(items, d => d.Name, cmp),
            "Category" => Order(items, d => d.Category, cmp),
            "Vendor" => Order(items, d => d.Vendor, cmp),
            "Provider" => Order(items, d => d.Provider, cmp),
            "Version" => Order(items, d => System.Version.TryParse(d.Version, out var v) ? v : new System.Version(0, 0), Comparer<System.Version>.Default),
            "Date" => Order(items, d => d.Date ?? DateOnly.MinValue, Comparer<DateOnly>.Default),
            "Inf" => Order(items, d => d.Inf, cmp),
            "Code" => Order(items, d => d.ErrorCode, Comparer<int>.Default),
            "HardwareId" => Order(items, d => d.HardwareId, cmp),
            "Latest" => Order(items, d => d.LatestVersionText, cmp),
            "VersionState" => Order(items, d => d.VersionState switch
            {
                VersionCheckState.NewerAvailable => 0,
                VersionCheckState.Failed => 1,
                VersionCheckState.Unknown => 2,
                VersionCheckState.UpToDate => 3,
                _ => 4,
            }, Comparer<int>.Default),
            _ => Order(items, d => d.Status, Comparer<DeviceStatus>.Default),
        };

        return ordered.ThenBy(d => d.Name, cmp);
    }

    private IOrderedEnumerable<DeviceItemViewModel> Order<TKey>(IEnumerable<DeviceItemViewModel> items, Func<DeviceItemViewModel, TKey> key, IComparer<TKey> comparer) =>
        SortAscending ? items.OrderBy(key, comparer) : items.OrderByDescending(key, comparer);

    private void ShowInfo(string message, InfoBarSeverity severity, string? actionPath = null)
    {
        InfoMessage = message;
        InfoSeverity = severity;
        InfoActionPath = actionPath;
        IsInfoOpen = true;
    }
}
