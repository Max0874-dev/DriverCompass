using System.Globalization;
using System.Net;
using System.Text;
using DriverCompass.Core.Abstractions;
using DriverCompass.Core.Models;

namespace DriverCompass.Core.Reporting;

internal static class ReportRows
{
    public static IEnumerable<DeviceAssessment> Select(AnalysisResult result, ReportOptions options) =>
        result.Devices.Where(d => options.IncludeSystemDevices || !d.IsSystemDevice);

    public static string Date(DateOnly? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
}

/// <summary>Self-contained HTML report (inline CSS, no scripts, no external resources).</summary>
public sealed class HtmlReportExporter : IReportExporter
{
    public string FormatId => "html";
    public string FileExtension => ".html";

    public async Task ExportAsync(AnalysisResult result, ReportOptions options, ITextProvider texts, Stream target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(target);

        var html = Build(result, options, texts);
        var writer = new StreamWriter(target, new UTF8Encoding(false), leaveOpen: true);
        await using (writer.ConfigureAwait(false))
        {
            await writer.WriteAsync(html.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
    }

    internal static string Build(AnalysisResult result, ReportOptions options, ITextProvider t)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
        var sys = result.Scan.System;
        var title = AssessmentTexts.Format(t.Get("Report_Title"), AppInfo.DisplayName);
        var sb = new StringBuilder(64 * 1024);

        sb.Append("<!DOCTYPE html>\n<html lang=\"").Append(E(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)).Append("\">\n<head>\n<meta charset=\"utf-8\">\n")
          .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n")
          .Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'\">\n")
          .Append("<title>").Append(E(title)).Append("</title>\n<style>\n").Append(Css).Append("</style>\n</head>\n<body>\n");

        sb.Append("<h1>").Append(E(title)).Append("</h1>\n");
        sb.Append("<p class=\"meta\">").Append(E(AssessmentTexts.Format(t.Get("Report_GeneratedAt"), result.CompletedAt.ToString("g", CultureInfo.CurrentCulture))))
          .Append(" · ").Append(E(AppInfo.DisplayName)).Append(' ').Append(E(AppInfo.Version))
          .Append(" · ").Append(E(AssessmentTexts.Format(t.Get("Report_CatalogVersion"), result.Catalog.DataVersion))).Append("</p>\n");

        sb.Append("<h2>").Append(E(t.Get("Report_System"))).Append("</h2>\n<table class=\"kv\">\n");
        void Kv(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                sb.Append("<tr><th>").Append(E(t.Get(key))).Append("</th><td>").Append(E(value)).Append("</td></tr>\n");
            }
        }

        Kv("Sys_Manufacturer", sys.Manufacturer);
        Kv("Sys_Model", sys.Model);
        Kv("Sys_BaseBoard", string.Join(' ', new[] { sys.BaseBoardManufacturer, sys.BaseBoardProduct }.Where(s => !string.IsNullOrWhiteSpace(s))));
        Kv("Sys_Bios", sys.BiosVersion);
        Kv("Sys_OperatingSystem", string.Join(' ', new[] { sys.OperatingSystem, sys.OperatingSystemVersion }.Where(s => !string.IsNullOrWhiteSpace(s))));
        if (options.IncludeSerialNumber)
        {
            Kv("Sys_SerialNumber", sys.SerialNumber);
        }

        sb.Append("</table>\n");

        var rows = ReportRows.Select(result, options).ToList();
        sb.Append("<h2>").Append(E(AssessmentTexts.Format(t.Get("Report_Devices"), rows.Count))).Append("</h2>\n");
        sb.Append("<table class=\"devices\">\n<thead><tr>");
        foreach (var col in new[] { "Col_Status", "Col_Name", "Col_Category", "Col_Vendor", "Col_Provider", "Col_Version", "Col_Date", "Col_Latest", "Col_VersionState", "Col_Inf", "Col_ErrorCode", "Col_HardwareId", "Col_Links" })
        {
            sb.Append("<th>").Append(E(t.Get(col))).Append("</th>");
        }

        sb.Append("</tr></thead>\n<tbody>\n");
        foreach (var d in rows)
        {
            var drv = d.Device.Driver;
            sb.Append("<tr class=\"s-").Append(d.Status.ToString().ToLowerInvariant()).Append("\">")
              .Append("<td><span class=\"dot\"></span>").Append(E(AssessmentTexts.Status(t, d.Status))).Append("<div class=\"reason\">").Append(E(AssessmentTexts.Reason(t, d))).Append("</div></td>")
              .Append("<td>").Append(E(d.DisplayName)).Append("</td>")
              .Append("<td>").Append(E(AssessmentTexts.Category(t, d.Device.DeviceClass))).Append("</td>")
              .Append("<td>").Append(E(d.VendorName)).Append("</td>")
              .Append("<td>").Append(E(drv?.ProviderName)).Append("</td>")
              .Append("<td>").Append(E(drv?.Version)).Append("</td>")
              .Append("<td>").Append(E(ReportRows.Date(drv?.Date))).Append("</td>")
              .Append("<td>").Append(E(AssessmentTexts.LatestVersion(d.VersionCheck))).Append("</td>")
              .Append("<td>").Append(E(AssessmentTexts.VersionState(t, d.VersionCheck))).Append("</td>")
              .Append("<td>").Append(E(drv?.InfName)).Append("</td>")
              .Append("<td>").Append(d.Device.ConfigManagerErrorCode == 0 ? string.Empty : d.Device.ConfigManagerErrorCode.ToString(CultureInfo.InvariantCulture)).Append("</td>")
              .Append("<td class=\"mono\">").Append(E(d.Device.HardwareIds.FirstOrDefault())).Append("</td><td>");
            foreach (var link in d.Links)
            {
                // Links are validated HTTPS URLs; still encoded for the attribute context.
                sb.Append("<a href=\"").Append(E(link.Uri.AbsoluteUri)).Append("\" rel=\"noopener noreferrer\">")
                  .Append(E(AssessmentTexts.LinkLabel(t, link))).Append("</a><br>");
            }

            sb.Append("</td></tr>\n");
        }

        sb.Append("</tbody>\n</table>\n<p class=\"meta\">").Append(E(t.Get("Report_Privacy"))).Append("</p>\n</body>\n</html>\n");
        return sb.ToString();
    }

    private const string Css = """
        :root { --bg:#ffffff; --fg:#1b1b1f; --muted:#5c5f66; --line:#d9dbe0; --head:#f3f4f6; --red:#c42b1c; --yellow:#b07c00; --green:#0f7b0f; --grey:#8a8d93; }
        @media (prefers-color-scheme: dark) { :root { --bg:#1c1c1f; --fg:#ececef; --muted:#a4a7ae; --line:#3a3b40; --head:#26272b; --red:#ff6b5e; --yellow:#f2c14e; --green:#6ccb5f; --grey:#9a9da3; } }
        body { font-family: "Segoe UI", system-ui, sans-serif; background:var(--bg); color:var(--fg); margin:24px; }
        h1 { font-size:1.6rem; margin:0 0 4px; } h2 { font-size:1.15rem; margin:28px 0 8px; }
        .meta { color:var(--muted); font-size:.85rem; }
        table { border-collapse:collapse; width:100%; font-size:.85rem; }
        th, td { border-bottom:1px solid var(--line); padding:6px 8px; text-align:left; vertical-align:top; }
        thead th { background:var(--head); position:sticky; top:0; }
        table.kv { width:auto; } table.kv th { font-weight:600; padding-right:24px; }
        .mono { font-family: Consolas, monospace; word-break:break-all; }
        .reason { color:var(--muted); font-size:.8rem; }
        .dot { display:inline-block; width:10px; height:10px; border-radius:50%; margin-right:6px; background:var(--grey); }
        .s-error .dot, .s-missingdriver .dot { background:var(--red); }
        .s-genericdriver .dot { background:var(--yellow); }
        .s-ok .dot { background:var(--green); }
        a { color:inherit; }
        """;
}

/// <summary>CSV report (UTF-8 with BOM for Excel, culture-dependent separator, formula-injection safe).</summary>
public sealed class CsvReportExporter : IReportExporter
{
    public string FormatId => "csv";
    public string FileExtension => ".csv";

    public async Task ExportAsync(AnalysisResult result, ReportOptions options, ITextProvider texts, Stream target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(target);

        var csv = Build(result, options, texts);
        var writer = new StreamWriter(target, new UTF8Encoding(true), leaveOpen: true);
        await using (writer.ConfigureAwait(false))
        {
            await writer.WriteAsync(csv.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
    }

    internal static string Build(AnalysisResult result, ReportOptions options, ITextProvider t)
    {
        var sep = string.IsNullOrEmpty(options.CsvSeparator) ? CultureInfo.CurrentCulture.TextInfo.ListSeparator : options.CsvSeparator;
        var sb = new StringBuilder(32 * 1024);

        void Row(IEnumerable<string?> fields) => sb.Append(string.Join(sep, fields.Select(f => Escape(f, sep)))).Append("\r\n");

        Row(new[] { "Col_Status", "Col_Reason", "Col_Name", "Col_Category", "Col_DeviceClass", "Col_Vendor", "Col_Manufacturer", "Col_Provider", "Col_Version", "Col_Date", "Col_Latest", "Col_VersionState", "Col_VersionSource", "Col_Inf", "Col_ErrorCode", "Col_HardwareIds", "Col_CompatibleIds", "Col_InstanceId", "Col_BestLink" }
            .Select(t.Get));

        foreach (var d in ReportRows.Select(result, options))
        {
            var drv = d.Device.Driver;
            Row(
            [
                AssessmentTexts.Status(t, d.Status),
                AssessmentTexts.Reason(t, d),
                d.DisplayName,
                AssessmentTexts.Category(t, d.Device.DeviceClass),
                d.Device.DeviceClass,
                d.VendorName,
                d.Device.Manufacturer,
                drv?.ProviderName,
                drv?.Version,
                ReportRows.Date(drv?.Date),
                AssessmentTexts.LatestVersion(d.VersionCheck),
                AssessmentTexts.VersionState(t, d.VersionCheck),
                AssessmentTexts.VersionSource(t, d.VersionCheck?.ProviderId),
                drv?.InfName,
                d.Device.ConfigManagerErrorCode.ToString(CultureInfo.InvariantCulture),
                string.Join(" | ", d.Device.HardwareIds),
                string.Join(" | ", d.Device.CompatibleIds),
                d.Device.InstanceId,
                d.Links.FirstOrDefault()?.Uri.AbsoluteUri,
            ]);
        }

        return sb.ToString();
    }

    /// <summary>Quotes when needed and neutralises spreadsheet formulas (=, +, -, @, tab, CR at the start).</summary>
    internal static string Escape(string? value, string separator)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        var needsQuotes = value.Contains(separator, StringComparison.Ordinal) || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        return needsQuotes ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
    }
}
