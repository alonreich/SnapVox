using snapvox.native;
using snapvox.native.foundation;
using snapvox.native.graphics;
using snapvox.native.ui;
using System;
using snapvox.foundation.core.AvaloniaShims;
using log4net;
using Microsoft.Win32;
using System.IO;
using snapvox.foundation.core;

namespace snapvox.helpers
{
    public static class StartupHelper
    {
        private static readonly ILog Log = snapvox.foundation.core.LogHelper.GetLogger(typeof(StartupHelper));
        private const string RunKey6432 = @"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Run";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ApplicationName = "snapvox";

        private static string GetExecutablePath(string arguments = null, string executablePath = null)
        {
            string resolvedPath = string.IsNullOrWhiteSpace(executablePath) ? RuntimePathHelper.ExecutablePath : executablePath;
            string quotedPath = "\"" + resolvedPath + "\"";
            if (string.IsNullOrWhiteSpace(arguments)) return quotedPath;
            return quotedPath + " " + arguments.Trim();
        }

        public static bool CanWriteRunAll()
        {
            try { using (RegistryKey key = Registry.LocalMachine.OpenSubKey(RunKey, true)) { } return true; }
            catch { return false; }
        }

        public static bool CanWriteRunUser()
        {
            try { using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true)) { } return true; }
            catch { return false; }
        }

        public static object GetRunAllValue()
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(RunKey, false))
            {
                object runValue = key?.GetValue(ApplicationName);
                if (runValue != null) return runValue;
            }
            if (IntPtr.Size != 8) return null;
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(RunKey6432, false))
            {
                return key?.GetValue(ApplicationName);
            }
        }

        public static object GetRunUserValue()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
            {
                object runValue = key?.GetValue(ApplicationName);
                if (runValue != null) return runValue;
            }
            if (IntPtr.Size != 8) return null;
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey6432, false))
            {
                return key?.GetValue(ApplicationName);
            }
        }

        public static bool HasRunAll()
        {
            try { return GetRunAllValue() != null; }
            catch (Exception e) { Log.Error("Error retrieving RunAllValue", e); return false; }
        }

        public static bool HasRunUser()
        {
            try { return GetRunUserValue() != null; }
            catch (Exception e) { Log.Error("Error retrieving RunUserValue", e); return false; }
        }

        public static void DeleteRunAll()
        {
            if (!HasRunAll()) return;
            try { using (RegistryKey key = Registry.LocalMachine.OpenSubKey(RunKey, true)) { key?.DeleteValue(ApplicationName, false); } }
            catch (Exception e) { Log.Error("Error in deleteRunAll.", e); }
            if (IntPtr.Size != 8) return;
            try { using (RegistryKey key = Registry.LocalMachine.OpenSubKey(RunKey6432, true)) { key?.DeleteValue(ApplicationName, false); } }
            catch (Exception e) { Log.Error("Error in deleteRunAll x64.", e); }
        }

        public static void DeleteRunUser()
        {
            if (!HasRunUser()) return;
            try { using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true)) { key?.DeleteValue(ApplicationName, false); } }
            catch (Exception e) { Log.Error("Error in deleteRunUser.", e); }
            if (IntPtr.Size != 8) return;
            try { using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey6432, true)) { key?.DeleteValue(ApplicationName, false); } }
            catch (Exception e) { Log.Error("Error in deleteRunUser x64.", e); }
        }

        public static void SetRunUser(string arguments = null, string executablePath = null)
        {
            try { using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey, true)) { key?.SetValue(ApplicationName, GetExecutablePath(arguments, executablePath)); } }
            catch (Exception e) { Log.Error("Error in setRunUser.", e); }
        }

        public static void DeleteStartupFolderShortcut()
        {
            try
            {
                string lnkName = Path.GetFileNameWithoutExtension(RuntimePathHelper.ExecutablePath) + ".lnk";
                string startupPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                string startupShortcut = Path.Combine(startupPath, lnkName);
                if (File.Exists(startupShortcut)) File.Delete(startupShortcut);
                string startupAll = Environment.GetEnvironmentVariable("ALLUSERSPROFILE") + @"\Microsoft\Windows\Start Menu\Programs\Startup";
                string startupAllShortcut = Path.Combine(startupAll, lnkName);
                if (File.Exists(startupAllShortcut)) File.Delete(startupAllShortcut);
            }
            catch (Exception e) { Log.Error("Error deleting startup shortcut.", e); }
        }

        public static bool IsInStartupFolder()
        {
            try
            {
                string lnkName = Path.GetFileNameWithoutExtension(RuntimePathHelper.ExecutablePath) + ".lnk";
                string startupPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                if (Directory.Exists(startupPath) && File.Exists(Path.Combine(startupPath, lnkName))) return true;
                string startupAll = Environment.GetEnvironmentVariable("ALLUSERSPROFILE") + @"\Microsoft\Windows\Start Menu\Programs\Startup";
                if (Directory.Exists(startupAll) && File.Exists(Path.Combine(startupAll, lnkName))) return true;
            }
            catch { }
            return false;
        }
    }
}
