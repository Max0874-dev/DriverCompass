using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace DriverCompass.Installer.Windows;

/// <summary>
/// Windows Update Agent COM API (Microsoft.Update.Session). Download and installation need administrator
/// rights – this class is only used inside the elevated process. Windows Update verifies hash and
/// Microsoft signature of the package itself.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ComWindowsUpdateInstaller : IWindowsUpdateInstaller, IDisposable
{
    private const int UpdateTypeDriver = 2;

    private dynamic? _session;
    private dynamic? _found;
    private string? _foundId;

    public OfferedUpdate? Find(string updateId)
    {
        if (!Guid.TryParseExact(updateId, "D", out var id))
        {
            return null;
        }

        var normalized = id.ToString("D");
        _session ??= CreateSession();
        dynamic searcher = _session.CreateUpdateSearcher();
        try
        {
            searcher.Online = true;
            dynamic result = searcher.Search($"UpdateID='{normalized}' and IsInstalled=0");
            int code = result.ResultCode;
            if (code is not (2 or 3))
            {
                return null;
            }

            dynamic updates = result.Updates;
            int count = updates.Count;
            for (var i = 0; i < count; i++)
            {
                dynamic update = updates.Item(i);
                dynamic identity = update.Identity;
                string uid = identity.UpdateID;
                if (!string.Equals(uid, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                _found = update;
                _foundId = uid;
                int type = update.Type;
                DateTime? date = update.DriverVerDate is DateTime d ? d : null;
                return new OfferedUpdate(
                    uid,
                    (int)identity.RevisionNumber,
                    type == UpdateTypeDriver,
                    (bool)update.EulaAccepted,
                    (string?)update.Title,
                    type == UpdateTypeDriver ? (string?)update.DriverHardwareID : null,
                    type == UpdateTypeDriver ? (string?)update.DriverProvider : null,
                    date is { } dt ? DateOnly.FromDateTime(dt) : null);
            }

            return null;
        }
        finally
        {
            Release(searcher);
        }
    }

    public UpdateInstallResult Install(OfferedUpdate update)
    {
        if (_found is null || !string.Equals(_foundId, update.UpdateId, StringComparison.OrdinalIgnoreCase))
        {
            return new UpdateInstallResult(false, false, "NotFound");
        }

        var collectionType = Type.GetTypeFromProgID("Microsoft.Update.UpdateColl", throwOnError: true)!;
        dynamic updates = Activator.CreateInstance(collectionType)!;
        updates.Add(_found);

        dynamic downloader = _session!.CreateUpdateDownloader();
        downloader.Updates = updates;
        dynamic download = downloader.Download();
        int downloadCode = download.ResultCode;
        if (downloadCode is not (2 or 3) || !(bool)_found.IsDownloaded)
        {
            return new UpdateInstallResult(false, false, $"Download:{downloadCode}:0x{(int)download.HResult:X8}");
        }

        dynamic installer = _session.CreateUpdateInstaller();
        installer.Updates = updates;
        dynamic install = installer.Install();
        int installCode = install.ResultCode;
        dynamic single = install.GetUpdateResult(0);
        int hresult = single.HResult;
        bool reboot = install.RebootRequired;

        return new UpdateInstallResult(installCode is 2 && hresult == 0, reboot, $"Install:{installCode}:0x{hresult:X8}");
    }

    public void Dispose()
    {
        Release(_found);
        Release(_session);
        _found = null;
        _session = null;
    }

    private static dynamic CreateSession()
    {
        var type = Type.GetTypeFromProgID("Microsoft.Update.Session", throwOnError: true)!;
        dynamic session = Activator.CreateInstance(type)!;
        session.ClientApplicationID = "DriverCompass";
        return session;
    }

    private static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
        {
            Marshal.FinalReleaseComObject(com);
        }
    }
}
