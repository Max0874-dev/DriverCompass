namespace DriverCompass.Core.Abstractions;

/// <summary>A newer DriverCompass release and the page where it can be downloaded.</summary>
/// <param name="Version">Version of the newer release, e.g. "0.2.0".</param>
/// <param name="ReleasePage">HTTPS link to the release page in the project's own GitHub repository.</param>
public sealed record AppUpdateInfo(string Version, Uri ReleasePage);

/// <summary>
/// Optional check for a newer DriverCompass release (off by default).
/// It only reads public release information; nothing is downloaded or installed and no device data is sent.
/// </summary>
public interface IAppUpdateChecker
{
    /// <summary>Returns the newer release, or <c>null</c> if the app is up to date or the check failed.</summary>
    Task<AppUpdateInfo?> CheckAsync(string currentVersion, CancellationToken cancellationToken = default);
}
