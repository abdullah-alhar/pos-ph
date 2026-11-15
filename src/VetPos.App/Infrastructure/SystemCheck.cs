using System.IO;
using VetPos.Core.Infrastructure;

namespace VetPos.App.Infrastructure;

/// <summary>
/// Checks the PC can run the till: Windows 10 (1809) or newer, 64-bit, and enough free disk space
/// for the database and backups. The installer installs the Visual C++ runtime when it is missing.
/// </summary>
public static class SystemCheck
{
    public const long LowDiskBytes = 500L * 1024 * 1024;

    /// <summary>A reason the app cannot run on this PC, or null.</summary>
    public static string? Blocking()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            return "VetPOS needs Windows 10 (version 1809 or newer) or Windows 11. Please update Windows.";
        if (!Environment.Is64BitOperatingSystem)
            return "VetPOS needs 64-bit Windows.";
        return null;
    }

    /// <summary>A warning to show after start (low disk space), or null.</summary>
    public static string? Warning(AppPaths paths)
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(paths.Root)!);
            if (drive.AvailableFreeSpace < LowDiskBytes)
                return $"The disk is almost full ({drive.AvailableFreeSpace / 1024 / 1024} MB free). Backups may fail — free some space.";
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            // Unknown drive (network folder): nothing to warn about.
        }
        return null;
    }
}
