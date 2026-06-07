using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace snapvox.helpers;

/// <summary>
/// Authoritative list of registry keys and filesystem paths created by the application.
/// Derived from source audit — Registry.* / Directory.CreateDirectory / File.Write* call sites.
/// Resolves Issue ID: 001, 002, 004, 005, 006, 009, 010.
/// </summary>
internal static class DeploymentFootprint
{
    public const string AppName = "snapvox";
    public const string ScheduledTaskName = "snapvox";
    public const string InstallerMutexName = @"Global\snapvox_Installer";
    public const string ProgId = "snapvox.editor.1";
    public const string OpenWithShellName = "Open with snapvox";
    public const string DisplayName = "snapvox";

    public const string UninstallKeyName = "snapvox";
    public const string UninstallKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + UninstallKeyName;
    public const string AppRegistryKeyPath = @"SOFTWARE\snapvox";

    public static readonly string ProgramDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        AppName);

    public static readonly string InstallFolder = StartupTaskHelper.InstallFolder;
    public static readonly string InstallLogPath = Path.Combine(ProgramDataFolder, "install_log.txt");

    public static readonly string RoamingAppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppName);

    public static readonly string LocalAppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppName);

    public static readonly string TempAppFolder = Path.Combine(Path.GetTempPath(), AppName);
    public static readonly string DeploymentTempRoot = Path.Combine(TempAppFolder, "Lifecycle");

    /// <summary>Early bootstrap / Setup.exe trace before ProgramData logging is available (%TEMP%).</summary>
    public static readonly string TempInstallationLogPath = Path.Combine(TempAppFolder, "Installation.log");

    public static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif", ".ico" };

    public static readonly string[] RunKeyRelativePaths =
    {
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
        @"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Run"
    };

    public static readonly string[] MuiCacheRelativePaths =
    {
        @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache",
        @"Software\Microsoft\Windows\ShellNoRoam\MUICache",
        @"Software\Microsoft\Windows\Shell\MuiCache"
    };

    public static readonly string[] ShortcutFileNames =
    {
        "snapvox.lnk",
        "SnapVox.lnk",
        "Uninstall snapvox.lnk",
        "Uninstall SnapVox.lnk",
        "snapvox.lnk"
    };

    public static IEnumerable<string> GetShortcutSearchFolders()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows\Start Menu\Programs\Startup");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Start Menu\Programs\Startup");
    }

    /// <summary>Canonical ARP entry for creation.</summary>
    public static (RegistryHive Hive, RegistryView View, string SubKeyPath) GetCanonicalUninstallRegistryTarget()
    {
        return (RegistryHive.LocalMachine, RegistryView.Registry64, UninstallKeyPath);
    }

    /// <summary>All uninstall registration locations to purge (Resolves Issue 001, 002).</summary>
    public static IEnumerable<(RegistryHive Hive, RegistryView View, string SubKeyPath)> GetUninstallRegistryPurgeTargets()
    {
        foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                yield return (hive, view, UninstallKeyPath);
                yield return (hive, view, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + AppName);
                yield return (hive, view, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\SnapVox");
                yield return (hive, view, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\SnapVoxsnapvox");
                yield return (hive, view, @"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\" + AppName);
                yield return (hive, view, @"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\" + UninstallKeyName);
                yield return (hive, view, @"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\SnapVox");
                yield return (hive, view, @"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\SnapVoxsnapvox");
                yield return (hive, view, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\snapvox");
                yield return (hive, view, @"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\snapvox");
            }
        }
    }

    public static IEnumerable<(RegistryHive Hive, RegistryView View, string Path)> GetAppRegistryPurgeTargets()
    {
        foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                yield return (hive, view, AppRegistryKeyPath);
                yield return (hive, view, @"SOFTWARE\Wow6432Node\snapvox");
                yield return (hive, view, @"SOFTWARE\snapvox");
                yield return (hive, view, @"SOFTWARE\Wow6432Node\snapvox");
            }
        }
    }

    public static IEnumerable<string> GetUserArtifactPatterns()
    {
        string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        yield return Path.Combine(downloads, "OCR_*.txt");
        yield return Path.Combine(downloads, "Capture_*.jpg");
        yield return Path.Combine(downloads, "Capture_*.png");
        yield return Path.Combine(Path.GetTempPath(), "snapvox*.*");
    }

    public static IEnumerable<string> GetDirectoryPurgeTargets(bool includeInstallFolder)
    {
        var dirs = new List<string>();
        if (includeInstallFolder) dirs.Add(InstallFolder);
        dirs.Add(ProgramDataFolder);
        dirs.Add(RoamingAppDataFolder);
        dirs.Add(LocalAppDataFolder);
        dirs.Add(TempAppFolder);
        dirs.Add(DeploymentTempRoot);

        dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "snapvox"));
        dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "snapvox"));

        
        return dirs.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    public static IEnumerable<string> GetVerificationTargets()
    {
        yield return InstallFolder;
        yield return ProgramDataFolder;
        yield return RoamingAppDataFolder;
        yield return LocalAppDataFolder;
    }
}
