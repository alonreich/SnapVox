using System;
using System.IO;
using snapvox.foundation.core;

namespace snapvox.helpers;

/// <summary>
/// Tracks installer host mode without referencing Avalonia (touching Avalonia loads Skia native DLLs).
/// </summary>
internal static class InstallHostContext
{
    public static bool HeadlessInstallerActive { get; set; }

    public static bool IsStandaloneInstallerHost()
    {
        if (StartupTaskHelper.IsRunningFromInstallPath())
        {
            return false;
        }

        if (PayloadExtractor.HasEmbeddedPayload())
        {
            return true;
        }

        string fileName = Path.GetFileName(RuntimePathHelper.ExecutablePath);
        if (fileName.Equals("Setup.exe", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string directory = (Path.GetDirectoryName(RuntimePathHelper.ExecutablePath) ?? string.Empty)
            .Replace('/', Path.DirectorySeparatorChar);
        if (directory.StartsWith(DeploymentFootprint.DeploymentTempRoot, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    public static void WriteEarlyTrace(string message)
    {
        try
        {
            string path = DeploymentFootprint.TempInstallationLogPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? DeploymentFootprint.TempAppFolder);
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [EARLY] {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}
