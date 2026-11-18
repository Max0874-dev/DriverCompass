using System.Runtime.InteropServices;
using DriverCompass.Core.Abstractions;
using DriverCompass.Core.Models;
using DriverCompass.Core.Versions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DriverCompass.VersionProviders.WindowsUpdate;

/// <summary>A driver update Windows Update offers for this PC.</summary>
public sealed record WindowsUpdateDriver(string? Title, string? HardwareId, DateOnly? Date, string? Provider, string? Model)
{
    /// <summary>Windows Update UpdateID (GUID) – what stage 3 hands to the elevated installer.</summary>
    public string? UpdateId { get; init; }

    public int Revision { get; init; }
}

public interface IWindowsUpdateSearcher
{
    /// <summary>Searches Windows Update for applicable, not installed drivers. Throws <see cref="VersionSourceException"/>.</summary>
    Task<IReadOnlyList<WindowsUpdateDriver>> SearchDriversAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Windows Update as version source. Windows Update only offers a driver if it ranks it better than the
/// installed one, so every offered driver counts as "newer available". Devices with a driver and no offer
/// are reported as "Windows Update has nothing newer" – not as "up to date" in general.
/// Uses the same channel Windows itself uses: Microsoft receives the hardware IDs of this PC.
/// </summary>
public sealed class WindowsUpdateVersionProvider(IWindowsUpdateSearcher searcher, ILogger<WindowsUpdateVersionProvider>? logger = null) : IVersionProvider
{
    private readonly ILogger _logger = (ILogger?)logger ?? NullLogger.Instance;

    public VersionSourceInfo Source { get; } = new(VersionSourceIds.WindowsUpdate, TransmitsDeviceData: true);

    public bool IsApplicable(SystemInfo system) => true;

    public async Task<VersionProviderResult> CheckAsync(
        IReadOnlyList<DeviceAssessment> devices,
        SystemInfo system,
        CancellationToken cancellationToken = default)
    {
        var offers = await searcher.SearchDriversAsync(cancellationToken).ConfigureAwait(false);

        var index = new Dictionary<string, WindowsUpdateDriver>(StringComparer.OrdinalIgnoreCase);
        foreach (var offer in offers)
        {
            if (offer.HardwareId is { Length: > 0 } id && (!index.TryGetValue(id, out var existing) || offer.Date > existing.Date))
            {
                index[id] = offer;
            }
        }

        var results = new Dictionary<string, VersionCheckResult>(StringComparer.OrdinalIgnoreCase);
        var matched = new HashSet<WindowsUpdateDriver>();
        foreach (var assessment in devices)
        {
            var offer = VersionMatching.MatchableIds(assessment.Device)
                .Select(id => index.GetValueOrDefault(id))
                .FirstOrDefault(o => o is not null);

            if (offer is not null)
            {
                matched.Add(offer);
                results[assessment.Device.InstanceId] = new VersionCheckResult(
                    Source.Id, VersionCheckState.NewerAvailable, VersionFromTitle(offer.Title), offer.Date, null, offer.Title)
                {
                    PackageId = Guid.TryParseExact(offer.UpdateId, "D", out _) ? offer.UpdateId : null,
                    PackageRevision = offer.Revision,
                };
            }
            else if (assessment.Device.Driver is { HasData: true })
            {
                results[assessment.Device.InstanceId] = new VersionCheckResult(Source.Id, VersionCheckState.UpToDate);
            }
        }

        var unmatched = offers.Count(o => !matched.Contains(o));
        if (unmatched > 0)
        {
            _logger.LogInformation("Windows Update offers {Count} driver(s) that match no listed device", unmatched);
        }

        return new VersionProviderResult(results, VersionSourceOutcome.Ok, offers.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>Windows Update titles usually end with the version: "Intel Corporation - Bluetooth - 23.150.0.3".</summary>
    internal static string? VersionFromTitle(string? title)
    {
        var last = title?.Split(" - ").LastOrDefault()?.Trim();
        return last is not null && last.Contains('.', StringComparison.Ordinal) && DriverVersion.TryParse(last, out _) &&
               last.All(c => char.IsAsciiDigit(c) || c == '.')
            ? last
            : null;
    }
}

/// <summary>
/// Windows Update Agent COM API (<c>Microsoft.Update.Session</c>). Works without admin rights.
/// Runs on its own STA thread; cancellation abandons the (read-only) search.
/// </summary>
public sealed class ComWindowsUpdateSearcher : IWindowsUpdateSearcher
{
    private const int AccessDenied = unchecked((int)0x80070005);

    public Task<IReadOnlyList<WindowsUpdateDriver>> SearchDriversAsync(CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<WindowsUpdateDriver>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                tcs.TrySetResult(Search());
            }
            catch (Exception ex)
            {
                tcs.TrySetException(Translate(ex));
            }
        })
        {
            IsBackground = true,
            Name = "DriverCompass Windows Update search",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        _ = tcs.Task.ContinueWith(_ => registration.Dispose(), TaskScheduler.Default);
        return tcs.Task;
    }

    private static List<WindowsUpdateDriver> Search()
    {
        var type = Type.GetTypeFromProgID("Microsoft.Update.Session", throwOnError: false)
            ?? throw new VersionSourceException(VersionSourceReasons.Unexpected, "Windows Update Agent not available");

        dynamic? session = null;
        dynamic? searcher = null;
        dynamic? result = null;
        try
        {
            session = Activator.CreateInstance(type)!;
            session.ClientApplicationID = "DriverCompass";
            searcher = session.CreateUpdateSearcher();
            searcher.Online = true;
            result = searcher.Search("IsInstalled=0 and IsHidden=0 and Type='Driver'");

            int code = result.ResultCode;
            if (code is not (2 or 3)) // orcSucceeded, orcSucceededWithErrors
            {
                throw new VersionSourceException(VersionSourceReasons.Offline, $"Windows Update search result code {code}");
            }

            var list = new List<WindowsUpdateDriver>();
            dynamic updates = result.Updates;
            int count = updates.Count;
            for (var i = 0; i < count; i++)
            {
                dynamic update = updates.Item(i);
                DateTime? date = update.DriverVerDate is DateTime d ? d : null;
                dynamic identity = update.Identity;
                string? updateId = identity.UpdateID;
                int revision = identity.RevisionNumber;
                Release(identity);
                list.Add(new WindowsUpdateDriver(
                    (string?)update.Title,
                    (string?)update.DriverHardwareID,
                    date is { } dt ? DateOnly.FromDateTime(dt) : null,
                    (string?)update.DriverProvider,
                    (string?)update.DriverModel)
                {
                    UpdateId = updateId,
                    Revision = revision,
                });
            }

            return list;
        }
        finally
        {
            Release(result);
            Release(searcher);
            Release(session);
        }
    }

    private static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
        {
            Marshal.FinalReleaseComObject(com);
        }
    }

    private static Exception Translate(Exception ex) => ex switch
    {
        VersionSourceException => ex,
        COMException { HResult: AccessDenied } or UnauthorizedAccessException =>
            new VersionSourceException(VersionSourceReasons.AccessDenied, "Windows Update search denied", ex),
        COMException com when (com.HResult & 0xFFFF0000) == unchecked((int)0x80240000) || (com.HResult & 0xFFFF0000) == unchecked((int)0x80070000) =>
            new VersionSourceException(VersionSourceReasons.Offline, $"Windows Update error 0x{com.HResult:X8}", ex),
        _ => new VersionSourceException(VersionSourceReasons.Unexpected, "Windows Update search failed: " + ex.Message, ex),
    };
}
