using DriverCompass.Core.Abstractions;
using DriverCompass.Data;
using Microsoft.Extensions.Logging;

namespace DriverCompass.App.Services;

/// <summary>Creates the catalog provider with the current "update online" setting for every load.</summary>
public sealed class SettingsAwareCatalogProvider(
    HttpClient http,
    IVendorCatalogValidator validator,
    ISettingsService settings,
    ILogger<VendorCatalogProvider> logger) : IVendorCatalogProvider
{
    public Task<CatalogLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        var options = CatalogProviderOptions.CreateDefault(settings.Current.UpdateCatalogOnline);
        return new VendorCatalogProvider(http, validator, options, null, logger).LoadAsync(cancellationToken);
    }
}
