using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using DriverCompass.Core.Installation;
using DriverCompass.Core.Models;

namespace DriverCompass.Installer.Windows;

/// <summary>
/// %ProgramData%\DriverCompass – created by the elevated process with an explicit ACL:
/// Administrators + SYSTEM full control, Users read only. An existing folder that is not owned by
/// Administrators/SYSTEM (or is a link) is refused, so a normal user cannot prepare it to redirect our writes.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ProtectedInstallStore : IInstallStore
{
    private readonly string _root;

    public ProtectedInstallStore(string? root = null)
    {
        _root = root ?? InstallProtocol.ProtectedDirectory;
        Prepare(_root);
    }

    /// <summary>Creates (or re-secures) the folder and its sub-folders. Call before anything writes there.</summary>
    public static void Prepare(string? root = null)
    {
        root ??= InstallProtocol.ProtectedDirectory;
        EnsureSecure(root);
        EnsureSecure(Path.Combine(root, "results"));
        EnsureSecure(Path.Combine(root, "logs"));
        EnsureSecure(Path.Combine(root, "cache"));
        EnsureSecure(Path.Combine(root, "work"));

        // Leftovers of an interrupted run (extracted packages) are not needed any more.
        foreach (var old in new DirectoryInfo(Path.Combine(root, "work")).EnumerateDirectories()
                     .Where(d => d.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-1) && !d.Attributes.HasFlag(FileAttributes.ReparsePoint)))
        {
            old.Delete(recursive: true);
        }
    }

    public IReadOnlyList<InstallLogEntry> ReadLog() => new InstallLogFile(_root).ReadAll();

    public void AppendLog(InstallLogEntry entry)
    {
        var path = InstallProtocol.LogPath(_root);
        if (File.Exists(path) && new FileInfo(path).Length > InstallProtocol.MaxLogBytes)
        {
            File.Move(path, path + ".old", overwrite: true);
        }

        File.AppendAllText(path, InstallProtocol.Serialize(entry) + "\n", new UTF8Encoding(false));
    }

    public void WriteResult(InstallResult result)
    {
        var path = InstallProtocol.ResultPath(_root, result.RequestId);
        File.WriteAllText(path, InstallProtocol.Serialize(result), new UTF8Encoding(false));

        // Results are only needed until the app has read them.
        foreach (var old in new DirectoryInfo(Path.GetDirectoryName(path)!).EnumerateFiles("*.json")
                     .Where(f => f.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-7)))
        {
            old.Delete();
        }
    }

    private static void EnsureSecure(string directory)
    {
        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);

        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(administrators);
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(administrators, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(users, FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));

        var info = new DirectoryInfo(directory);
        if (!info.Exists)
        {
            info.Create(security);
            return;
        }

        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new UnauthorizedAccessException($"Refusing to use {directory}: it is a link.");
        }

        var owner = info.GetAccessControl().GetOwner(typeof(SecurityIdentifier));
        if (owner != administrators && owner != system)
        {
            throw new UnauthorizedAccessException($"Refusing to use {directory}: not owned by Administrators/SYSTEM.");
        }

        info.SetAccessControl(security);
    }
}
