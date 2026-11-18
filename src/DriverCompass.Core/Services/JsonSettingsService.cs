using System.Text.Json;
using System.Text.Json.Serialization;
using DriverCompass.Core.Abstractions;
using DriverCompass.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DriverCompass.Core.Services;

/// <summary>Stores <see cref="AppSettings"/> as JSON. A broken file falls back to defaults.</summary>
public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly ILogger _logger;

    public JsonSettingsService(string path, ILogger<JsonSettingsService>? logger = null)
    {
        _path = path;
        _logger = logger ?? NullLogger<JsonSettingsService>.Instance;
        Current = Load();
    }

    public static string DefaultPath => Path.Combine(AppInfo.DataDirectory, "settings.json");

    public AppSettings Current { get; private set; }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

        var temp = _path + ".tmp";
        await using (var stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        File.Move(temp, _path, overwrite: true);
        Current = settings;
    }

    private AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return AppSettings.Default;
            }

            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? AppSettings.Default;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Settings file unreadable, using defaults");
            return AppSettings.Default;
        }
    }
}
