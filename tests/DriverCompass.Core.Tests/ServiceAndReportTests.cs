using System.Text;
using DriverCompass.Core.Abstractions;
using DriverCompass.Core.Evaluation;
using DriverCompass.Core.Links;
using DriverCompass.Core.Models;
using DriverCompass.Core.Parsing;
using DriverCompass.Core.Reporting;
using DriverCompass.Core.Scanning;
using DriverCompass.Core.Services;

namespace DriverCompass.Core.Tests;

internal sealed class FixedCatalogProvider(IVendorCatalog catalog) : IVendorCatalogProvider
{
    public int Calls { get; private set; }

    public Task<CatalogLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(new CatalogLoadResult(catalog, new CatalogInfo(CatalogSource.Embedded, catalog.DataVersion, [])));
    }
}

public class DeviceAnalysisServiceTests
{
    private static DeviceAnalysisService Create(MockProfile profile, FixedCatalogProvider? provider = null) => new(
        new MockDeviceScanner(profile),
        provider ?? new FixedCatalogProvider(TestCatalog.Create()),
        new DeviceStatusEvaluator(),
        LinkResolver.CreateDefault());

    [Fact]
    public async Task Problems_are_sorted_first()
    {
        var result = await Create(MockProfile.WhiteBoxDesktop).AnalyzeAsync(AnalysisOptions.Default, TestContext.Current.CancellationToken);

        var statuses = result.Devices.Select(d => d.Status).ToList();
        Assert.Equal(statuses.OrderBy(s => s), statuses);
        Assert.Equal(DeviceStatus.Error, statuses[0]);
    }

    [Fact]
    public async Task Mock_desktop_contains_the_typical_problem_cases()
    {
        var result = await Create(MockProfile.WhiteBoxDesktop).AnalyzeAsync(AnalysisOptions.Default, TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Devices.Count(d => d.Status == DeviceStatus.MissingDriver));
        Assert.Equal(1, result.Devices.Count(d => d.Status == DeviceStatus.Error));
        Assert.Equal(2, result.Devices.Count(d => d.Status == DeviceStatus.GenericDriver));
        Assert.Equal(1, result.Devices.Count(d => d.Status == DeviceStatus.Disabled));
        Assert.Equal(2, result.Devices.Count(d => d.IsSystemDevice));
        Assert.Equal(6, result.AttentionCount);
    }

    [Fact]
    public async Task Every_visible_device_with_hardware_ids_has_links()
    {
        var result = await Create(MockProfile.DellNotebook).AnalyzeAsync(AnalysisOptions.Default, TestContext.Current.CancellationToken);

        Assert.All(result.Devices.Where(d => !d.IsSystemDevice), d => Assert.NotEmpty(d.Links));
        var gpu = result.Devices.Single(d => d.Device.DeviceClass == "Display");
        Assert.Equal(DriverLinkKind.OemSupport, gpu.Links[0].Kind);
        Assert.Contains("ABC1234", gpu.Links[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Catalog_is_loaded_only_once()
    {
        var provider = new FixedCatalogProvider(TestCatalog.Create());
        var service = Create(MockProfile.WhiteBoxDesktop, provider);

        await service.AnalyzeAsync(AnalysisOptions.Default, TestContext.Current.CancellationToken);
        await service.AnalyzeAsync(AnalysisOptions.Default, TestContext.Current.CancellationToken);
        Assert.Equal(1, provider.Calls);

        service.InvalidateCatalog();
        await service.AnalyzeAsync(AnalysisOptions.Default, TestContext.Current.CancellationToken);
        Assert.Equal(2, provider.Calls);
    }
}

public class ReportTests
{
    private static async Task<AnalysisResult> Analyze(SystemInfo? system = null)
    {
        var service = new DeviceAnalysisService(
            new MockDeviceScanner(MockProfile.DellNotebook),
            new FixedCatalogProvider(TestCatalog.Create()),
            new DeviceStatusEvaluator(),
            LinkResolver.CreateDefault());
        var result = await service.AnalyzeAsync(AnalysisOptions.Default);
        return system is null ? result : result with { Scan = result.Scan with { System = system } };
    }

    [Fact]
    public async Task Html_report_is_encoded_and_has_no_scripts()
    {
        var result = await Analyze(new SystemInfo { Manufacturer = "<script>alert(1)</script>", Model = "M&M" });
        var html = HtmlReportExporter.Build(result, new ReportOptions(), KeyTextProvider.Instance);

        Assert.DoesNotContain("<script>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("M&amp;M", html, StringComparison.Ordinal);
        Assert.Contains("rel=\"noopener noreferrer\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Serial_number_is_excluded_from_reports_by_default()
    {
        var result = await Analyze();
        var html = HtmlReportExporter.Build(result, new ReportOptions(), KeyTextProvider.Instance);
        Assert.DoesNotContain("Sys_SerialNumber", html, StringComparison.Ordinal);

        var withSerial = HtmlReportExporter.Build(result, new ReportOptions { IncludeSerialNumber = true }, KeyTextProvider.Instance);
        Assert.Contains("Sys_SerialNumber", withSerial, StringComparison.Ordinal);
    }

    [Fact]
    public async Task System_devices_are_excluded_unless_requested()
    {
        var result = await Analyze();
        var csv = CsvReportExporter.Build(result, new ReportOptions { CsvSeparator = ";" }, KeyTextProvider.Instance);
        Assert.DoesNotContain(@"ROOT\BasicRender", csv, StringComparison.Ordinal);

        var all = CsvReportExporter.Build(result, new ReportOptions { CsvSeparator = ";", IncludeSystemDevices = true }, KeyTextProvider.Instance);
        Assert.Contains(@"ROOT\BasicRender", all, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Version_results_appear_in_both_reports()
    {
        var result = await Analyze();
        var run = await new DriverCompass.Core.Versions.VersionCheckService([new DriverCompass.Core.Versions.MockVersionProvider(VersionSourceIds.Dell)])
            .CheckAllAsync(result.Devices, result.Scan.System, new HashSet<string> { VersionSourceIds.Dell }, cancellationToken: TestContext.Current.CancellationToken);
        var checkedResult = result with { Devices = run.Devices };

        var csv = CsvReportExporter.Build(checkedResult, new ReportOptions { CsvSeparator = ";" }, KeyTextProvider.Instance);
        var html = HtmlReportExporter.Build(checkedResult, new ReportOptions(), KeyTextProvider.Instance);

        Assert.Contains("Col_Latest;Col_VersionState;Col_VersionSource", csv, StringComparison.Ordinal);
        Assert.Contains("23.130.0.4;VersionState_NewerAvailable;VersionSource_dell", csv, StringComparison.Ordinal);
        Assert.Contains("VersionState_UpToDate", csv, StringComparison.Ordinal);
        Assert.Contains("VersionState_Unknown", csv, StringComparison.Ordinal);
        Assert.Contains("<td>23.130.0.4</td><td>VersionState_NewerAvailable</td>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_version_check_the_version_columns_stay_empty()
    {
        var csv = CsvReportExporter.Build(await Analyze(), new ReportOptions { CsvSeparator = ";" }, KeyTextProvider.Instance);
        Assert.DoesNotContain("VersionState_", csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Csv_has_header_and_one_row_per_device()
    {
        var result = await Analyze();
        var csv = CsvReportExporter.Build(result, new ReportOptions { CsvSeparator = ";" }, KeyTextProvider.Instance);
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.StartsWith("Col_Status;Col_Reason;Col_Name", lines[0], StringComparison.Ordinal);
        Assert.Equal(result.Devices.Count(d => !d.IsSystemDevice) + 1, lines.Length);
    }

    [Theory]
    [InlineData("plain", ";", "plain")]
    [InlineData("a;b", ";", "\"a;b\"")]
    [InlineData("say \"hi\"", ";", "\"say \"\"hi\"\"\"")]
    [InlineData("=HYPERLINK(\"x\")", ";", "\"'=HYPERLINK(\"\"x\"\")\"")]
    [InlineData("+1", ",", "'+1")]
    [InlineData("-cmd", ",", "'-cmd")]
    [InlineData("@SUM(A1)", ",", "'@SUM(A1)")]
    [InlineData("line1\nline2", ",", "\"line1\nline2\"")]
    [InlineData(null, ",", "")]
    public void Csv_escaping_and_formula_injection(string? value, string sep, string expected) =>
        Assert.Equal(expected, CsvReportExporter.Escape(value, sep));

    [Fact]
    public async Task Csv_export_writes_utf8_bom()
    {
        var result = await Analyze();
        using var ms = new MemoryStream();
        await new CsvReportExporter().ExportAsync(result, new ReportOptions(), KeyTextProvider.Instance, ms, TestContext.Current.CancellationToken);
        var bytes = ms.ToArray();
        Assert.True(bytes.Length > 3);
        Assert.Equal(Encoding.UTF8.Preamble.ToArray(), bytes[..3]);
    }
}

public class AssessmentTextTests
{
    private sealed class DictTexts(Dictionary<string, string> map) : ITextProvider
    {
        public string Get(string key) => map.TryGetValue(key, out var v) ? v : key;
    }

    [Fact]
    public void Unknown_error_code_falls_back_to_generic_text()
    {
        var t = new DictTexts(new() { ["CmError_Unknown"] = "Code {0}", ["CmError_43"] = "Stopped" });
        Assert.Equal("Stopped", AssessmentTexts.ErrorCode(t, 43));
        Assert.Equal("Code 99", AssessmentTexts.ErrorCode(t, 99));
    }

    [Fact]
    public void Unknown_class_falls_back_to_class_name()
    {
        var t = new DictTexts(new() { ["Class_Display"] = "Grafikkarte", ["Class_Unknown"] = "Sonstiges" });
        Assert.Equal("Grafikkarte", AssessmentTexts.Category(t, "Display"));
        Assert.Equal("Firmware", AssessmentTexts.Category(t, "Firmware"));
        Assert.Equal("Sonstiges", AssessmentTexts.Category(t, null));
    }

    [Fact]
    public void Broken_format_string_does_not_throw() =>
        Assert.Equal("Broken {", AssessmentTexts.Format("Broken {", 1));
}

public class SanitizerTests
{
    [Theory]
    [InlineData("To Be Filled By O.E.M.", true)]
    [InlineData("System manufacturer", true)]
    [InlineData("Default string", true)]
    [InlineData("0000000000", true)]
    [InlineData("XXXXXXXX", true)]
    [InlineData("", true)]
    [InlineData(null, true)]
    [InlineData("Dell Inc.", false)]
    [InlineData("ABC1234", false)]
    [InlineData("MS-7D25", false)]
    public void Placeholders(string? value, bool expected) =>
        Assert.Equal(expected, SystemInfoSanitizer.IsPlaceholder(value));
}

public class PciIdsDatabaseTests
{
    private const string Sample =
        "# comment\n" +
        "10de  NVIDIA Corporation\n" +
        "\t2489  GA104 [GeForce RTX 3060 Ti Lite Hash Rate]\n" +
        "\t\t1043 8822  Some ASUS card\n" +
        "10ec  Realtek Semiconductor Co., Ltd.\n" +
        "\t8125  RTL8125 2.5GbE Controller\n" +
        "C 00  Unclassified device\n" +
        "\t00  Non-VGA unclassified device\n";

    private static PciIdsDatabase Db() => new(() => new MemoryStream(Encoding.UTF8.GetBytes(Sample)));

    [Fact]
    public void Vendor_and_device_names_are_found_case_insensitive()
    {
        var db = Db();
        Assert.Equal("NVIDIA Corporation", db.GetVendorName(VendorIdKind.Pci, "10DE"));
        Assert.Equal("RTL8125 2.5GbE Controller", db.GetDeviceName(VendorIdKind.Pci, "10ec", "8125"));
        Assert.Equal(2, db.VendorCount);
    }

    [Fact]
    public void Only_pci_namespace_is_answered()
    {
        Assert.Null(Db().GetVendorName(VendorIdKind.Usb, "10DE"));
    }

    [Fact]
    public void Subsystems_and_classes_are_ignored()
    {
        var db = Db();
        Assert.Null(db.GetDeviceName(VendorIdKind.Pci, "10DE", "1043"));
        Assert.Null(db.GetVendorName(VendorIdKind.Pci, "00"));
    }

    [Fact]
    public void Missing_stream_gives_empty_database()
    {
        var db = new PciIdsDatabase(() => null);
        Assert.Null(db.GetVendorName(VendorIdKind.Pci, "10DE"));
    }
}

public class SettingsServiceTests
{
    [Fact]
    public async Task Settings_roundtrip_and_broken_file_falls_back_to_defaults()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dc-tests-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "settings.json");
        try
        {
            var service = new JsonSettingsService(path);
            Assert.Equal(AppSettings.Default, service.Current);

            Assert.Empty(service.Current.EnabledVersionSources);

            var changed = service.Current with
            {
                StartView = ViewMode.Expert,
                Language = "en-US",
                UseSerialNumberInLinks = false,
                EnabledVersionSources = [VersionSourceIds.WindowsUpdate, VersionSourceIds.Dell],
            };
            await service.SaveAsync(changed, TestContext.Current.CancellationToken);
            Assert.Equal(changed, new JsonSettingsService(path).Current);

            await File.WriteAllTextAsync(path, "{ not json", TestContext.Current.CancellationToken);
            Assert.Equal(AppSettings.Default, new JsonSettingsService(path).Current);

            // A settings file from stage 1 (without the new field) still loads – with every version source off.
            await File.WriteAllTextAsync(path, """{ "language": "de-DE", "startView": "Expert" }""", TestContext.Current.CancellationToken);
            var old = new JsonSettingsService(path).Current;
            Assert.Equal("de-DE", old.Language);
            Assert.Empty(old.EnabledVersionSources);

            // Users of 0.2.0 and older see the privacy notice once; the update hint stays off.
            Assert.False(old.PrivacyNoticeShown);
            Assert.False(old.CheckForAppUpdates);
            Assert.True(old.UpdateCatalogOnline);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Privacy_choice_applies_even_if_the_file_cannot_be_written()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dc-tests-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "settings.json");
        Directory.CreateDirectory(dir);
        try
        {
            var service = new JsonSettingsService(path);
            await using (File.Create(path + ".tmp", 1, FileOptions.None))
            {
                // The temp file is locked, so writing fails – the choice must still be in effect for this session.
                var choice = service.Current with { UpdateCatalogOnline = false, PrivacyNoticeShown = true };
                await Assert.ThrowsAnyAsync<IOException>(() => service.SaveAsync(choice, TestContext.Current.CancellationToken));
                Assert.False(service.Current.UpdateCatalogOnline);
                Assert.True(service.Current.PrivacyNoticeShown);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
