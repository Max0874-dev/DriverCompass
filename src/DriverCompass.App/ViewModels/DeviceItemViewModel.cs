using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using DriverCompass.App.Services;
using DriverCompass.Core.Abstractions;
using DriverCompass.Core.Links;
using DriverCompass.Core.Models;
using DriverCompass.Core.Reporting;
using Microsoft.UI.Xaml.Media;

namespace DriverCompass.App.ViewModels;

/// <summary>One device row/card. All texts are computed once when the scan result arrives.</summary>
public sealed partial class DeviceItemViewModel
{
    private readonly Func<DeviceItemViewModel, Task>? _install;
    private readonly Func<DeviceItemViewModel, Task>? _showGuide;

    /// <param name="install">Stage 3 (GitHub edition only).</param>
    /// <param name="showGuide">Opens the step-by-step guide for this device.</param>
    /// <param name="modelPage">Resolver that turns a maker's general link into this model's page when opened (HP).</param>
    public DeviceItemViewModel(
        DeviceAssessment assessment,
        ITextProvider t,
        string language,
        Func<DeviceItemViewModel, Task>? install = null,
        Func<DeviceItemViewModel, Task>? showGuide = null,
        Func<DriverLink, Func<Task<Uri?>>?>? modelPage = null)
    {
        Assessment = assessment;
        _install = install;
        _showGuide = showGuide;
        var device = assessment.Device;
        var driver = device.Driver;

        Name = assessment.DisplayName;
        WindowsName = device.Name ?? string.Empty;
        Category = AssessmentTexts.Category(t, device.DeviceClass);
        StatusText = AssessmentTexts.Status(t, assessment.Status);
        Reason = AssessmentTexts.Reason(t, assessment);
        Vendor = assessment.VendorName ?? string.Empty;
        Manufacturer = device.Manufacturer ?? string.Empty;
        Provider = driver?.ProviderName ?? string.Empty;
        Version = driver?.Version ?? string.Empty;
        Date = driver?.Date;
        DateText = driver?.Date?.ToString("d", CultureInfo.CurrentCulture) ?? string.Empty;
        Inf = driver?.InfName ?? string.Empty;
        Signer = driver?.Signer ?? string.Empty;
        ErrorCode = device.ConfigManagerErrorCode;
        ErrorCodeText = ErrorCode == 0 ? string.Empty : ErrorCode.ToString(CultureInfo.InvariantCulture);
        DeviceClass = device.DeviceClass ?? string.Empty;
        InstanceId = device.InstanceId;
        HardwareId = device.HardwareIds.FirstOrDefault() ?? string.Empty;
        HardwareIds = device.HardwareIds.Select(h => new HardwareIdItem(h)).ToList();
        CompatibleIds = device.CompatibleIds.Select(h => new HardwareIdItem(h)).ToList();
        Note = assessment.ChipVendor?.Note?.Get(language[..2]) ?? string.Empty;

        var check = assessment.VersionCheck;
        VersionState = check?.State;
        LatestVersionText = AssessmentTexts.LatestVersion(check);
        VersionStatusText = AssessmentTexts.VersionState(t, check);
        VersionPackageText = string.IsNullOrWhiteSpace(check?.PackageName) ? string.Empty : AssessmentTexts.Format(t.Get("Version_Package"), check.PackageName);
        VersionSourceUri = check?.SourceUri;
        IsWindowsUpdateSource = check?.ProviderId == VersionSourceIds.WindowsUpdate;

        Links = assessment.Links.Select(l =>
        {
            // A general maker link that can be resolved to this model's page is labelled as a model link.
            var resolve = modelPage?.Invoke(l);
            var label = resolve is null ? AssessmentTexts.LinkLabel(t, l) : AssessmentTexts.Format(t.Get("Link_OemSupport_Model"), l.SourceName);
            return new LinkViewModel(l, label, resolve);
        }).ToList();
        PrimaryLink = Links.FirstOrDefault();
        SecondaryLinks = Links.Skip(1).ToList();

        SearchIndex = string.Join('\n', new[] { Name, WindowsName, Vendor, Manufacturer, Provider, Inf, DeviceClass, Category, InstanceId }
            .Concat(device.HardwareIds)).ToUpperInvariant();
    }

    public DeviceAssessment Assessment { get; }
    public DeviceStatus Status => Assessment.Status;
    public bool NeedsAttention => Assessment.NeedsAttention;
    public bool IsSystemDevice => Assessment.IsSystemDevice;

    public string Name { get; }
    public string WindowsName { get; }
    public string Category { get; }
    public string StatusText { get; }
    public string Reason { get; }
    public string Vendor { get; }
    public string Manufacturer { get; }
    public string Provider { get; }
    public string Version { get; }
    public DateOnly? Date { get; }
    public string DateText { get; }
    public string Inf { get; }
    public string Signer { get; }
    public int ErrorCode { get; }
    public string ErrorCodeText { get; }
    public string DeviceClass { get; }
    public string InstanceId { get; }
    public string HardwareId { get; }
    public IReadOnlyList<HardwareIdItem> HardwareIds { get; }
    public IReadOnlyList<HardwareIdItem> CompatibleIds { get; }
    public string Note { get; }

    public VersionCheckState? VersionState { get; }
    public string LatestVersionText { get; }
    public string VersionStatusText { get; }
    public string VersionPackageText { get; }
    public Uri? VersionSourceUri { get; }
    public bool IsWindowsUpdateSource { get; }
    public bool HasVersionCheck => VersionState is not null;
    public bool IsNewerAvailable => VersionState == VersionCheckState.NewerAvailable;

    /// <summary>Where a newer driver can be fetched: Windows Update settings or the official driver page.</summary>
    public bool ShowVersionAction => IsNewerAvailable && (IsWindowsUpdateSource || VersionSourceUri is not null);

    public string VersionActionText => Localizer.T(IsWindowsUpdateSource ? "Version_OpenWindowsUpdate" : "Version_OpenSource");

    public string VersionGlyph => VersionState switch
    {
        VersionCheckState.NewerAvailable => "", // Download
        VersionCheckState.UpToDate => "",       // CheckMark
        _ => "",                                // Unknown
    };

    public Brush VersionBrush => StatusVisuals.VersionBrush(VersionState);

    public IReadOnlyList<LinkViewModel> Links { get; }
    public LinkViewModel? PrimaryLink { get; }
    public IReadOnlyList<LinkViewModel> SecondaryLinks { get; }
    public bool HasLinks => Links.Count > 0;
    public bool HasSecondaryLinks => SecondaryLinks.Count > 0;

    /// <summary>Stage 3: DriverCompass can install this newer driver itself (one click = one driver, with confirmation).</summary>
    public bool CanInstall => _install is not null && Assessment.VersionCheck?.IsInstallable == true;

    /// <summary>The prominent "Get driver" button is shown for problem devices only.</summary>
    public bool ShowGetDriver => ((NeedsAttention && HasLinks) || ShowVersionAction) && !CanInstall;

    /// <summary>"How to" for every device that needs a driver or has a newer one.</summary>
    public bool ShowGuide => _showGuide is not null && (NeedsAttention || IsNewerAvailable);

    public bool ShowNoLinks => NeedsAttention && !HasLinks && !ShowVersionAction;

    /// <summary>"More sources" next to the primary button, "Driver sources" when it stands alone.</summary>
    public string SourcesLabel => Localizer.T(ShowGetDriver || CanInstall ? "Simple_MoreSources" : "Simple_Sources");

    public Brush StatusBrush => StatusVisuals.Brush(Status);
    public string StatusGlyph => StatusVisuals.Glyph(Status);

    /// <summary>Upper-case text used by the expert search.</summary>
    internal string SearchIndex { get; }

    [RelayCommand]
    private Task GetDriverAsync()
    {
        // A newer driver from a version source wins over the generic link list.
        if (ShowVersionAction)
        {
            return OpenVersionSourceAsync();
        }

        return PrimaryLink is null ? Task.CompletedTask : PrimaryLink.OpenLinkAsync();
    }

    [RelayCommand]
    private Task InstallAsync() => CanInstall ? _install!(this) : Task.CompletedTask;

    [RelayCommand]
    private Task ShowGuideAsync() => ShowGuide ? _showGuide!(this) : Task.CompletedTask;

    [RelayCommand]
    private Task OpenVersionSourceAsync() =>
        IsWindowsUpdateSource ? Launcher.OpenWindowsUpdateAsync() : Launcher.OpenAsync(VersionSourceUri);

    [RelayCommand]
    private void CopyInstanceId() => Launcher.CopyText(InstanceId);
}

/// <param name="resolve">Optional: finds this model's page when the link is opened; the link's own URL is the fallback.</param>
public sealed partial class LinkViewModel(DriverLink link, string label, Func<Task<Uri?>>? resolve = null)
{
    public DriverLink Link { get; } = link;
    public Uri Uri => Link.Uri;
    public string Label { get; } = label;
    public string Host => Link.Uri.Host;

    public async Task OpenLinkAsync()
    {
        var uri = Link.Uri;
        if (resolve is not null && await resolve() is { } modelPage)
        {
            uri = modelPage;
        }

        await Launcher.OpenAsync(uri);
    }

    [RelayCommand]
    private Task OpenAsync() => OpenLinkAsync();
}

public sealed partial class HardwareIdItem(string id)
{
    public string Id { get; } = id;

    [RelayCommand]
    private void Copy() => Launcher.CopyText(Id);

    [RelayCommand]
    private Task SearchCatalogAsync() => Launcher.OpenAsync(MicrosoftCatalogLinkSource.BuildSearchUri(Id));
}

/// <summary>Group for the simple view (one per traffic-light status).</summary>
public sealed class DeviceGroupViewModel(DeviceStatus status, string title, IEnumerable<DeviceItemViewModel> items) : List<DeviceItemViewModel>(items)
{
    public DeviceStatus Status { get; } = status;
    public string Title { get; } = title;
    public string Header => $"{Title} ({Count})";
    public Brush StatusBrush => StatusVisuals.Brush(Status);
    public string StatusGlyph => StatusVisuals.Glyph(Status);
}
