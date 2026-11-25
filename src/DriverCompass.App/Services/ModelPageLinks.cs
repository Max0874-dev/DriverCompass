using System.Collections.Concurrent;
using DriverCompass.Core.Abstractions;
using DriverCompass.Core.Models;
using Microsoft.Extensions.Logging;

namespace DriverCompass.App.Services;

/// <summary>
/// Turns a PC maker's general support link into a link to this model's page, resolved when the user
/// opens it (HP). Off when the user switched off online lists. A found page is remembered for the session.
/// </summary>
public sealed class ModelPageLinks(IEnumerable<IOemModelPageProvider> providers, ISettingsService settings, ILogger<ModelPageLinks> logger)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly IReadOnlyList<IOemModelPageProvider> _providers = providers.ToList();
    private readonly ConcurrentDictionary<string, Uri> _found = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A resolver for this link, or <c>null</c> when the link stays as it is.</summary>
    public Func<Task<Uri?>>? For(DriverLink link, SystemInfo system)
    {
        if (link.Kind != DriverLinkKind.OemSupport || link.Specificity != LinkSpecificity.Generic ||
            link.OemId is null || !settings.Current.UpdateCatalogOnline)
        {
            return null;
        }

        var provider = _providers.FirstOrDefault(p => string.Equals(p.OemId, link.OemId, StringComparison.OrdinalIgnoreCase) && p.CanResolve(system));
        if (provider is null)
        {
            return null;
        }

        return async () =>
        {
            if (_found.TryGetValue(provider.OemId, out var known))
            {
                return known;
            }

            using var timeout = new CancellationTokenSource(Timeout);
            try
            {
                var uri = await provider.FindModelPageAsync(system, timeout.Token);
                if (uri is not null)
                {
                    _found[provider.OemId] = uri;
                }

                return uri;
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning("Model page lookup for {Oem} timed out", provider.OemId);
                return null;
            }
        };
    }
}
