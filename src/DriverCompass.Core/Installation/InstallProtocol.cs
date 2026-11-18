using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DriverCompass.Core.Abstractions;
using DriverCompass.Core.Models;

namespace DriverCompass.Core.Installation;

/// <summary>
/// File-based protocol between the unelevated app and the elevated process:
/// request  → %LOCALAPPDATA%\DriverCompass\installer\&lt;id&gt;.json (written by the app, only read by the elevated process)
/// result   → %ProgramData%\DriverCompass\results\&lt;id&gt;.json   (written by the elevated process, read by the app)
/// log      → %ProgramData%\DriverCompass\install-log.jsonl      (append-only, elevated process)
/// The ProgramData folder is writable for administrators only, so nothing there can be planted by a normal user.
/// </summary>
public static class InstallProtocol
{
    public const int MaxRequestBytes = 16 * 1024;
    public const int MaxResultBytes = 64 * 1024;
    public const long MaxLogBytes = 10L * 1024 * 1024;
    public const string ElevatedSwitch = "--elevated";
    public const string RequestSwitch = "--request";

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = false,
    };

    public static string RequestDirectory => Path.Combine(AppInfo.DataDirectory, "installer");

    public static string ProtectedDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), AppInfo.TechnicalName);

    public static string ResultPath(string protectedDirectory, Guid requestId) =>
        Path.Combine(protectedDirectory, "results", requestId.ToString("N") + ".json");

    public static string LogPath(string protectedDirectory) => Path.Combine(protectedDirectory, "install-log.jsonl");

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);

    /// <summary>Reads and validates a request. Returns <c>null</c> and an error text for anything unexpected.</summary>
    public static InstallRequest? ReadRequest(string path, out string? error)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxRequestBytes)
            {
                error = "request file missing or too large";
                return null;
            }

            var request = JsonSerializer.Deserialize<InstallRequest>(File.ReadAllText(path, Encoding.UTF8), Json);
            error = request is null ? "empty request" : Validate(request);
            return error is null ? request : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            error = "unreadable request: " + ex.GetType().Name;
            return null;
        }
    }

    /// <summary>Returns <c>null</c> if the request is well-formed, otherwise the reason.</summary>
    public static string? Validate(InstallRequest request)
    {
        if (request.RequestId == Guid.Empty)
        {
            return "missing request id";
        }

        switch (request.Action)
        {
            case InstallAction.Install:
                if (!IsPlausibleInstanceId(request.DeviceInstanceId))
                {
                    return "invalid device instance id";
                }

                if (request.LogEntryId is not null)
                {
                    return "unexpected log entry id";
                }

                return request.Source switch
                {
                    VersionSourceIds.WindowsUpdate when !Guid.TryParseExact(request.UpdateId, "D", out _) => "invalid update id",
                    VersionSourceIds.WindowsUpdate when request.Revision is < 0 or > 1_000_000 => "invalid revision",
                    VersionSourceIds.WindowsUpdate when request.PackageId is not null => "unexpected package id",
                    VersionSourceIds.WindowsUpdate => null,
                    VersionSourceIds.Dell when request.UpdateId is not null || !IsSafeDellPackagePath(request.PackageId) => "invalid Dell package",
                    VersionSourceIds.Hp when request.UpdateId is not null || !IsHpSoftPaqId(request.PackageId) => "invalid HP package",
                    VersionSourceIds.Lenovo when request.UpdateId is not null || !IsLenovoPackageId(request.PackageId) => "invalid Lenovo package",
                    VersionSourceIds.Dell or VersionSourceIds.Hp or VersionSourceIds.Lenovo => null,
                    _ => "unsupported source",
                };

            case InstallAction.Rollback:
                return request.LogEntryId is { } id && id != Guid.Empty && request.UpdateId is null && request.DeviceInstanceId is null &&
                       request.PackageId is null
                    ? null
                    : "invalid rollback request";

            default:
                return "unknown action";
        }
    }

    /// <summary>Sources whose packages DriverCompass can install.</summary>
    public static IReadOnlySet<string> InstallableSources { get; } =
        new HashSet<string>(StringComparer.Ordinal) { VersionSourceIds.WindowsUpdate, VersionSourceIds.Dell, VersionSourceIds.Hp, VersionSourceIds.Lenovo };

    /// <summary>Dell component path relative to downloads.dell.com, e.g. "FOLDER14023085M/2/Name_A03.EXE".</summary>
    public static bool IsSafeDellPackagePath(string? path) =>
        path is { Length: > 5 and < 300 } &&
        path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
        !path.StartsWith('/') &&
        !path.Contains("..", StringComparison.Ordinal) &&
        path.All(c => char.IsAsciiLetterOrDigit(c) || c is '/' or '-' or '_' or '.');

    /// <summary>Lenovo package descriptor id, e.g. "n32li04w" or "n4ody12w_w10w11".</summary>
    public static bool IsLenovoPackageId(string? id) =>
        id is { Length: >= 4 and <= 40 } && id.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_');

    /// <summary>HP SoftPaq number, e.g. "sp157308".</summary>
    public static bool IsHpSoftPaqId(string? id) =>
        id is { Length: >= 6 and <= 9 } &&
        id.StartsWith("sp", StringComparison.OrdinalIgnoreCase) &&
        id[2..].All(char.IsAsciiDigit);

    /// <summary>PnP instance IDs: printable ASCII, no quotes (they end up in a WQL query), sensible length.</summary>
    public static bool IsPlausibleInstanceId(string? id) =>
        id is { Length: > 3 and <= 400 } &&
        id.All(c => c is >= ' ' and <= '~' && c != '\'' && c != '"') &&
        id.Contains('\\', StringComparison.Ordinal);

    public static InstallResult? ReadResult(string path, Guid expectedRequestId)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxResultBytes)
            {
                return null;
            }

            var result = JsonSerializer.Deserialize<InstallResult>(File.ReadAllText(path, Encoding.UTF8), Json);
            return result?.RequestId == expectedRequestId ? result : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Parses the JSON-lines log; broken lines are skipped, the file size is capped.</summary>
    public static IReadOnlyList<InstallLogEntry> ParseLog(IEnumerable<string> lines)
    {
        var entries = new List<InstallLogEntry>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                if (JsonSerializer.Deserialize<InstallLogEntry>(line, Json) is { } entry)
                {
                    entries.Add(entry);
                }
            }
            catch (JsonException)
            {
                // A damaged line must not hide the rest of the history.
            }
        }

        return entries;
    }

    /// <summary>Installations that can still be rolled back: successful, with new packages, not undone yet.</summary>
    public static bool CanRollBack(InstallLogEntry entry, IReadOnlyList<InstallLogEntry> all) =>
        entry.Action == InstallAction.Install &&
        entry.Outcome is InstallOutcome.Installed or InstallOutcome.RebootRequired &&
        entry.PublishedInfs.Count > 0 &&
        !all.Any(e => e.Action == InstallAction.Rollback && e.RolledBackEntryId == entry.Id &&
                      e.Outcome is InstallOutcome.RolledBack or InstallOutcome.RebootRequired);
}

/// <summary>Reads the protected install log (read-only for the app).</summary>
public sealed class InstallLogFile(string? protectedDirectory = null) : IInstallLog
{
    private readonly string _path = InstallProtocol.LogPath(protectedDirectory ?? InstallProtocol.ProtectedDirectory);

    public IReadOnlyList<InstallLogEntry> ReadAll()
    {
        try
        {
            var info = new FileInfo(_path);
            return info.Exists && info.Length <= InstallProtocol.MaxLogBytes
                ? InstallProtocol.ParseLog(File.ReadLines(_path, Encoding.UTF8))
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
