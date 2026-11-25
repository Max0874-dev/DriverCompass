using System.Runtime.InteropServices;
using DriverCompass.Core.Abstractions;
using DriverCompass.Core.Models;

namespace DriverCompass.Core.Reporting;

/// <summary>What a button next to a guide step does. The UI decides how.</summary>
public enum GuideAction
{
    None = 0,

    /// <summary>Open the device's first driver source (maker or chip vendor page).</summary>
    OpenPrimaryLink,

    /// <summary>Open the page of the newer package found by the version check.</summary>
    OpenVersionSource,

    /// <summary>Open Settings → Windows Update → Optional updates.</summary>
    OpenWindowsUpdate,

    /// <summary>Open the Microsoft Update Catalog search for <see cref="GuideStep.Value"/> (a hardware ID).</summary>
    OpenCatalogSearch,

    /// <summary>Copy <see cref="GuideStep.Value"/> to the clipboard.</summary>
    Copy,

    /// <summary>Let DriverCompass install the package (GitHub edition only).</summary>
    Install,
}

/// <param name="Text">Localized step text.</param>
/// <param name="Action">Optional button for this step.</param>
/// <param name="Value">Text to copy or hardware ID to search, depending on <paramref name="Action"/>.</param>
public sealed record GuideStep(string Text, GuideAction Action = GuideAction.None, string? Value = null);

/// <param name="Intro">Optional sentence above the steps.</param>
/// <param name="Shortcut">Optional quicker way above the steps (install directly in DriverCompass).</param>
/// <param name="Steps">Numbered steps.</param>
/// <param name="Hints">Alternatives and help below the steps.</param>
public sealed record DriverGuideContent(string? Intro, GuideStep? Shortcut, IReadOnlyList<GuideStep> Steps, IReadOnlyList<GuideStep> Hints);

/// <summary>
/// Step-by-step help to get and install the right driver for one device, chosen from what DriverCompass
/// knows: a newer driver at Windows Update or at the PC maker, the maker's or chip vendor's page, or only
/// the hardware ID. DriverCompass never downloads anything here – the steps open official pages.
/// </summary>
public static class DriverGuide
{
    /// <summary>Makers with an official update program of their own (vendor-list ids).</summary>
    private static readonly string[] MakersWithTools = [VersionSourceIds.Hp, VersionSourceIds.Dell, VersionSourceIds.Lenovo];

    /// <summary>Guide for this device, or <c>null</c> when it needs nothing (working driver, no newer version).</summary>
    /// <param name="assessment">The device with its links and version check result.</param>
    /// <param name="system">The PC (model, Windows version).</param>
    /// <param name="canInstallHere">DriverCompass can install this package itself (GitHub edition, installable result).</param>
    /// <param name="t">Localized texts.</param>
    public static DriverGuideContent? Build(DeviceAssessment assessment, SystemInfo system, bool canInstallHere, ITextProvider t)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(t);

        var check = assessment.VersionCheck;
        if (check is { State: VersionCheckState.NewerAvailable })
        {
            var shortcut = canInstallHere && check.IsInstallable ? new GuideStep(t.Get("Guide_InstallHere"), GuideAction.Install) : null;
            return check.ProviderId == VersionSourceIds.WindowsUpdate
                ? WindowsUpdate(check, shortcut, t)
                : MakerPackage(assessment, check, shortcut, t);
        }

        if (!assessment.NeedsAttention)
        {
            return null;
        }

        var intro = assessment.Status == DeviceStatus.GenericDriver ? t.Get("Guide_Intro_Generic") : null;
        if (assessment.Links.Count > 0 && assessment.Links[0].Kind != DriverLinkKind.MicrosoftCatalog)
        {
            return VendorPage(assessment, assessment.Links[0], system, intro, t);
        }

        var hardwareId = assessment.Device.HardwareIds.FirstOrDefault(h => !string.IsNullOrWhiteSpace(h));
        return hardwareId is not null
            ? Catalog(hardwareId, system, intro, t)
            : NoSource(intro, t);
    }

    private static DriverGuideContent WindowsUpdate(VersionCheckResult check, GuideStep? shortcut, ITextProvider t)
    {
        var name = string.IsNullOrWhiteSpace(check.PackageName) ? t.Get("Guide_Wu_DriverFallback") : check.PackageName;
        return new DriverGuideContent(
            null,
            shortcut,
            [
                new GuideStep(t.Get("Guide_Wu_Open"), GuideAction.OpenWindowsUpdate),
                new GuideStep(AssessmentTexts.Format(t.Get("Guide_Wu_Select"), name)),
                new GuideStep(t.Get("Guide_Wu_Install")),
                new GuideStep(t.Get("Guide_Restart")),
            ],
            []);
    }

    private static DriverGuideContent MakerPackage(DeviceAssessment assessment, VersionCheckResult check, GuideStep? shortcut, ITextProvider t)
    {
        var maker = AssessmentTexts.VersionSource(t, check.ProviderId);
        var steps = new List<GuideStep>();

        if (check.SourceUri is not null)
        {
            steps.Add(new GuideStep(AssessmentTexts.Format(t.Get("Guide_Oem_OpenPackage"), maker), GuideAction.OpenVersionSource));
        }
        else if (assessment.Links.Count > 0 && assessment.Links[0].Kind == DriverLinkKind.OemSupport)
        {
            steps.Add(new GuideStep(AssessmentTexts.Format(t.Get("Guide_Oem_OpenModel"), maker), GuideAction.OpenPrimaryLink));
        }
        else
        {
            steps.Add(new GuideStep(AssessmentTexts.Format(t.Get("Guide_Oem_FindSupport"), maker)));
        }

        var package = check.PackageName ?? check.PackageId ?? string.Empty;
        steps.Add(string.IsNullOrWhiteSpace(check.LatestVersion)
            ? new GuideStep(AssessmentTexts.Format(t.Get("Guide_Oem_Download"), package), GuideAction.Copy, package)
            : new GuideStep(AssessmentTexts.Format(t.Get("Guide_Oem_DownloadVersion"), package, check.LatestVersion), GuideAction.Copy, package));

        if (check.ProviderId == VersionSourceIds.Hp && !string.IsNullOrWhiteSpace(check.PackageId))
        {
            steps.Add(new GuideStep(AssessmentTexts.Format(t.Get("Guide_Oem_SoftPaq"), check.PackageId), GuideAction.Copy, check.PackageId));
        }

        steps.Add(new GuideStep(AssessmentTexts.Format(t.Get("Guide_Oem_Run"), maker)));
        steps.Add(new GuideStep(t.Get("Guide_Restart")));

        var hints = new List<GuideStep>();
        if (MakersWithTools.Contains(check.ProviderId, StringComparer.Ordinal))
        {
            hints.Add(new GuideStep(t.Get($"Guide_Tool_{check.ProviderId}")));
        }

        return new DriverGuideContent(null, shortcut, steps, hints);
    }

    private static DriverGuideContent VendorPage(DeviceAssessment assessment, DriverLink link, SystemInfo system, string? intro, ITextProvider t)
    {
        var steps = new List<GuideStep>
        {
            new(AssessmentTexts.Format(t.Get("Guide_Page_Open"), link.SourceName), GuideAction.OpenPrimaryLink),
        };

        if (link.Kind == DriverLinkKind.OemSupport)
        {
            if (Clean(system.Model) is { } model)
            {
                steps.Add(new GuideStep(AssessmentTexts.Format(t.Get("Guide_Page_Model"), model), GuideAction.Copy, model));
            }
        }
        else
        {
            var product = assessment.DisplayName;
            steps.Add(new GuideStep(AssessmentTexts.Format(t.Get("Guide_Page_Product"), product), GuideAction.Copy, product));
        }

        steps.Add(new GuideStep(AssessmentTexts.Format(t.Get("Guide_Page_Os"), WindowsName(system))));
        steps.Add(new GuideStep(AssessmentTexts.Format(t.Get("Guide_Page_Category"), AssessmentTexts.Category(t, assessment.Device.DeviceClass))));
        steps.Add(new GuideStep(t.Get("Guide_Page_Run")));
        steps.Add(new GuideStep(t.Get("Guide_Restart")));

        var hints = new List<GuideStep> { new(t.Get("Guide_Hint_Inf")) };
        if (link.Kind == DriverLinkKind.OemSupport && link.OemId is { } oem && MakersWithTools.Contains(oem, StringComparer.Ordinal))
        {
            hints.Add(new GuideStep(t.Get($"Guide_Tool_{oem}")));
        }

        hints.Add(new GuideStep(t.Get("Guide_Hint_WindowsUpdate"), GuideAction.OpenWindowsUpdate));
        return new DriverGuideContent(intro, null, steps, hints);
    }

    private static DriverGuideContent Catalog(string hardwareId, SystemInfo system, string? intro, ITextProvider t) => new(
        intro,
        null,
        [
            new GuideStep(AssessmentTexts.Format(t.Get("Guide_Cat_Copy"), hardwareId), GuideAction.Copy, hardwareId),
            new GuideStep(t.Get("Guide_Cat_Open"), GuideAction.OpenCatalogSearch, hardwareId),
            new GuideStep(AssessmentTexts.Format(t.Get("Guide_Cat_Pick"), WindowsName(system))),
            new GuideStep(t.Get("Guide_Cat_Install")),
            new GuideStep(t.Get("Guide_Restart")),
        ],
        [new GuideStep(t.Get("Guide_Hint_WindowsUpdate"), GuideAction.OpenWindowsUpdate)]);

    private static DriverGuideContent NoSource(string? intro, ITextProvider t) => new(
        intro,
        null,
        [
            new GuideStep(t.Get("Guide_None_WindowsUpdate"), GuideAction.OpenWindowsUpdate),
            new GuideStep(t.Get("Guide_None_Maker")),
        ],
        []);

    /// <summary>"Windows 11 (64-Bit)" / "Windows 10 (ARM64)" – what driver pages ask for.</summary>
    internal static string WindowsName(SystemInfo system)
    {
        var name = system.IsWindows11 ? "Windows 11" : "Windows 10";
        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.Arm64 => "ARM64",
            Architecture.X86 => "32-Bit",
            _ => "64-Bit",
        };
        return $"{name} ({arch})";
    }

    private static string? Clean(string? value) => Evaluation.SystemInfoSanitizer.Clean(value);
}
