using DriverCompass.Core;
using DriverCompass.Core.Abstractions;

namespace DriverCompass.App.Services;

/// <summary>Demo mode (<c>--mock</c>): pretends the next minor version exists, without any network access.</summary>
public sealed class MockAppUpdateChecker : IAppUpdateChecker
{
    public Task<AppUpdateInfo?> CheckAsync(string currentVersion, CancellationToken cancellationToken = default)
    {
        var parts = currentVersion.Split('-', '+')[0].Split('.');
        var minor = parts.Length > 1 && int.TryParse(parts[1], out var m) ? m + 1 : 1;
        var next = $"{(parts.Length > 0 ? parts[0] : "0")}.{minor}.0";
        var page = new Uri($"{AppInfo.RepositoryUrl.TrimEnd('/')}/releases");
        return Task.FromResult<AppUpdateInfo?>(new AppUpdateInfo(next, page));
    }
}
