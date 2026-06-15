using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using snapvox.foundation.core.AvaloniaShims;
using snapvox.foundation.core;
using snapvox.foundation.IniFile;
using Microsoft.Win32;
using Avalonia.Threading;

namespace snapvox.helpers;

internal static class DeploymentLifecycle
{
    private const int DeleteRetries = 3;
    private static readonly string SessionTempFolder = Path.Combine(DeploymentFootprint.DeploymentTempRoot, "Staging_" + Process.GetCurrentProcess().Id);
    private static int _pendingRebootDeletes;

    [Flags]
    private enum MoveFileFlags : uint
    {
        DelayUntilReboot = 0x00000004,
        ReplaceExisting = 0x00000001
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileEx(string existingFileName, string newFileName, MoveFileFlags flags);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);

    public static bool ShouldRunHeadlessDeployment(string[] args)
    {
        if (args == null || args.Length == 0) return InstallHostContext.IsStandaloneInstallerHost();
        return args.Any(arg => arg.Equals("--uninstall", StringComparison.OrdinalIgnoreCase) 
                             || arg.Equals("--install", StringComparison.OrdinalIgnoreCase)
                             || arg.Equals("--install-worker", StringComparison.OrdinalIgnoreCase)
                             || arg.Equals("--cleanup-worker", StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsLifecycleCommand(string[] args)
    {
        if (args == null || args.Length == 0) return false;

        return args.Any(arg => arg.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)
                             || arg.Equals("--install", StringComparison.OrdinalIgnoreCase)
                             || arg.Equals("--install-worker", StringComparison.OrdinalIgnoreCase)
                             || arg.Equals("--cleanup-worker", StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsUninstallLauncherCommand(string[] args)
    {
        if (args == null || args.Length == 0) return false;
        return args.Any(arg => arg.Equals("--uninstall", StringComparison.OrdinalIgnoreCase))
            && !args.Any(arg => arg.Equals("--cleanup-worker", StringComparison.OrdinalIgnoreCase));
    }

    public static async Task<int> RunLifecycleCommandAsync(string[] args, CancellationToken ct = default)
    {
        InstallHostContext.HeadlessInstallerActive = false;
        try
        {
            if (IsUninstallLauncherCommand(args)) return await RunUninstallLauncherAsync(args, ct).ConfigureAwait(false);
            if (args != null && args.Any(arg => arg.Equals("--cleanup-worker", StringComparison.OrdinalIgnoreCase)))
                return await RunUninstallAsync(args, ct).ConfigureAwait(false);
            
            return await RunInstallAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            BootstrapDebug.Log("Lifecycle command fatal: " + ex);
            return 1;
        }
    }

    public static async Task<int> RunHeadlessDeploymentCommandAsync(string[] args)
    {
        InstallHostContext.HeadlessInstallerActive = false;
        try
        {
            if (IsUninstallLauncherCommand(args)) return await RunUninstallLauncherAsync(args, CancellationToken.None).ConfigureAwait(false);
            if (args != null && args.Any(arg => arg.Equals("--cleanup-worker", StringComparison.OrdinalIgnoreCase)))
                return await RunUninstallAsync(args, CancellationToken.None).ConfigureAwait(false);
            
            return await RunInstallAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            BootstrapDebug.Log("Headless deployment fatal: " + ex);
            return 1;
        }
    }

    public static async Task<int> RunInstallAsync(CancellationToken ct = default)
    {
        bool isWorker = Environment.GetCommandLineArgs().Any(a => a.Equals("--install-worker", StringComparison.OrdinalIgnoreCase));
        if (!isWorker)
        {
            await RelaunchInstallFromTempAsync(ct).ConfigureAwait(false);
            return 0;
        }

        if (!StartupTaskHelper.IsElevated())
        {
            StartElevated(RuntimePathHelper.ExecutablePath, "--install --install-worker");
            return 0;
        }

        using var mutex = new Mutex(false, DeploymentFootprint.InstallerMutexName);
        if (!AcquireMutex(mutex)) return 2;

        DeploymentLogger logger = null;
        DeploymentProgress progress = null;
        string logPath = DeploymentFootprint.InstallLogPath;

        try
        {
            logger = await DeploymentLogger.CreateAsync(logPath, "INSTALL/UPGRADE", ct).ConfigureAwait(false);
            progress = CreateDeploymentProgress("SnapVox Setup", logPath);

            string conflict = DetectConflictingSoftware();
            if (conflict != null)
            {
                await logger.LogAsync("CRITICAL", "CONFLICT", $"Detected {conflict}", ct).ConfigureAwait(false);
                StartupTaskHelper.ShowForegroundMessageBox(
                    $"Installation had detected a current installed software of {conflict} installed on you system!\r\n\r\nPlease first remove/uninstall the app of {conflict} then re-run the installer again.",
                    "Installation Conflict", 
                    MessageBoxButtons.OK, 
                    MessageBoxIcon.Error);
                throw new Exception($"Conflicting software detected: {conflict}");
            }

            await PerformFullHostCleanupAsync(progress, logger, "Pre-Install Cleanup", 5, 60, requireZeroFootprint: false, purgeUserArtifacts: false, ct).ConfigureAwait(false);

            await ReportAsync(progress, logger, 65, "DEPLOY", "PAYLOAD", "Extracting assets...", ct).ConfigureAwait(false);
            await InstallFreshAsync(progress, logger, ct).ConfigureAwait(false);

            await ReportAsync(progress, logger, 100, "SUCCESS", "COMPLETE", "Deployment finalized.", ct).ConfigureAwait(false);
            await LaunchInstalledApplicationAsync();
            return 0;
        }
        catch (Exception ex)
        {
            if (logger != null) await logger.LogAsync("CRITICAL", "ERROR", ex.Message, ct, ex).ConfigureAwait(false);
            return ex.HResult == 0 ? 1 : ex.HResult;
        }
        finally
        {
            mutex.ReleaseMutex();
            progress?.Dispose();
            if (logger != null) await logger.DisposeAsync().ConfigureAwait(false);
            QueueSelfCleanup(DeploymentFootprint.DeploymentTempRoot);
        }
    }

    public static async Task<int> RunUninstallAsync(string[] args, CancellationToken ct = default)
    {
        if (!args.Any(a => a.Equals("--cleanup-worker", StringComparison.OrdinalIgnoreCase)))
            return await RunUninstallLauncherAsync(args, ct).ConfigureAwait(false);

        if (!StartupTaskHelper.IsElevated())
        {
            TryStartElevated(RuntimePathHelper.ExecutablePath, "--uninstall --cleanup-worker");
            return 0;
        }

        int parentPid = ParseParentPid(args);
        if (parentPid > 0)
        {
            BootstrapDebug.Log($"Worker: Waiting for parent PID {parentPid} to exit...");
            await WaitForParentExitAsync(parentPid, ct).ConfigureAwait(false);
        }

        string logPath = Path.Combine(SessionTempFolder, "snapvox_Uninstall.log");
        DeploymentLogger logger = null;
        DeploymentProgress progress = null;

        try
        {
            logger = await DeploymentLogger.CreateAsync(logPath, "UNINSTALL", ct).ConfigureAwait(false);
            progress = CreateDeploymentProgress("SnapVox Uninstaller", logPath);

            await ReportAsync(progress, logger, 5, "UNINSTALL", "INIT", "Starting scorched-earth cleanup...", ct).ConfigureAwait(false);

            await PerformFullHostCleanupAsync(progress, logger, "Uninstall", 10, 90, requireZeroFootprint: true, purgeUserArtifacts: true, ct).ConfigureAwait(false);

            await ReportAsync(progress, logger, 95, "UNINSTALL", "VERIFYING", "Confirming system state...", ct).ConfigureAwait(false);
            bool clean = await VerifyZeroFootprintAsync(logger, verifyData: true, ct).ConfigureAwait(false);
            
            string status = (clean && Volatile.Read(ref _pendingRebootDeletes) == 0) ? "SUCCESS" : "PENDING REBOOT";
            string detail = clean ? "All components removed successfully." : "Cleanup complete. Some items require a reboot for full removal.";
            
            await ReportAsync(progress, logger, 100, "UNINSTALL", status, detail, ct).ConfigureAwait(false);
            
            await Task.Delay(500, ct).ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex)
        {
            BootstrapDebug.Log("Worker FATAL: " + ex);
            await ReportAsync(progress, logger, 100, "FAILURE", "ERROR", ex.Message, ct, ex).ConfigureAwait(false);
            await Task.Delay(1000, ct).ConfigureAwait(false);
            return 1;
        }
        finally
        {
            progress?.Dispose();
            if (logger != null) await logger.DisposeAsync().ConfigureAwait(false);
            
            QueueSelfCleanup(DeploymentFootprint.DeploymentTempRoot);
        }
    }

    private static async Task PerformFullHostCleanupAsync(DeploymentProgress progress, DeploymentLogger logger, string op, int start, int end, bool requireZeroFootprint, bool purgeUserArtifacts, CancellationToken ct)
    {
        Interlocked.Exchange(ref _pendingRebootDeletes, 0);
        await logger.LogAsync("CLEANUP", "START", $"Performing Scorched Earth for: {op}", ct).ConfigureAwait(false);

        await ReportAsync(progress, logger, start + 5, "CLEANUP", "PROCESSES", "Killing all instances...", ct).ConfigureAwait(false);
        await StartupTaskHelper.KillAllProcessesAsync(s => progress?.Update(start + 5, s), ct).ConfigureAwait(false);
        await Task.Delay(500, ct).ConfigureAwait(false);

        await ReportAsync(progress, logger, start + 10, "CLEANUP", "TASKS", "Removing triggers...", ct).ConfigureAwait(false);
        await RunHiddenProcessAsync("schtasks.exe", $"/Delete /TN \"{DeploymentFootprint.ScheduledTaskName}\" /F", 5000, logger, ct).ConfigureAwait(false);

        var targets = DeploymentFootprint.GetDirectoryPurgeTargets(includeInstallFolder: true).ToList();
        for (int i = 0; i < targets.Count; i++)
        {
            int p = start + 15 + (int)((end - start - 40) * (i / (double)targets.Count));
            await ReportAsync(progress, logger, p, "CLEANUP", "FILESYSTEM", $"Purging: {targets[i]}", ct).ConfigureAwait(false);
            await PurgeDirectoryRecursiveAsync(targets[i], logger, ct).ConfigureAwait(false);
        }

        await ReportAsync(progress, logger, end - 20, "CLEANUP", "REGISTRY", "Scrubbing all hives...", ct).ConfigureAwait(false);
        await DeleteRegistryFootprintAsync(logger, ct).ConfigureAwait(false);

        if (!string.Equals(op, "Pre-Install Cleanup", StringComparison.OrdinalIgnoreCase))
        {
            await DeleteFileWithRetryAsync(DeploymentFootprint.TempInstallationLogPath, logger, ct).ConfigureAwait(false);
        }

        await ReportAsync(progress, logger, end - 10, "CLEANUP", "SHELL", "Cleaning links...", ct).ConfigureAwait(false);
        await DeleteKnownShortcutsAsync(logger, ct).ConfigureAwait(false);

        if (purgeUserArtifacts)
        {
            await ReportAsync(progress, logger, end - 5, "CLEANUP", "USER", "Removing temporary artifacts...", ct).ConfigureAwait(false);
            await PurgeUserGeneratedArtifactsAsync(logger, ct).ConfigureAwait(false);
        }

        if (requireZeroFootprint)
        {
            bool clean = await VerifyZeroFootprintAsync(logger, true, ct).ConfigureAwait(false);
            if (!clean) await logger.LogAsync("CLEANUP", "WARNING", "Residual items detected - some may require reboot.", ct).ConfigureAwait(false);
        }
    }

    private static async Task PurgeDirectoryRecursiveAsync(string dir, DeploymentLogger logger, CancellationToken ct)
    {
        if (!Directory.Exists(dir)) return;

        try
        {
            try 
            { 
                Directory.Delete(dir, true); 
                await logger.LogAsync("FILESYSTEM", "DELETE_DIR_FAST", dir, ct).ConfigureAwait(false);
                return; 
            } 
            catch { }

            var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
            var tasks = new System.Collections.Generic.List<Task>();
            using var throttler = new SemaphoreSlim(20);

            foreach (string file in files)
            {
                await throttler.WaitAsync(ct).ConfigureAwait(false);
                tasks.Add(Task.Run(async () =>
                {
                    try { await DeleteFileWithRetryAsync(file, logger, ct).ConfigureAwait(false); }
                    finally { throttler.Release(); }
                }, ct));
            }
            await Task.WhenAll(tasks).ConfigureAwait(false);

            var subDirs = Directory.GetDirectories(dir, "*", SearchOption.AllDirectories)
                                   .OrderByDescending(d => d.Length);
            foreach (string sub in subDirs)
            {
                try 
                { 
                    if (Directory.Exists(sub))
                    {
                        Directory.Delete(sub, true); 
                        await logger.LogAsync("FILESYSTEM", "DELETE_DIR", sub, ct).ConfigureAwait(false); 
                    }
                } 
                catch { }
            }

            try 
            { 
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true); 
                    await logger.LogAsync("FILESYSTEM", "DELETE_ROOT", dir, ct).ConfigureAwait(false); 
                }
            } 
            catch { }
        }
        catch (Exception ex)
        {
            await logger.LogAsync("FILESYSTEM", "ERROR", $"Failed to purge {dir}: {ex.Message}", ct).ConfigureAwait(false);
        }
    }

    private static async Task DeleteFileWithRetryAsync(string path, DeploymentLogger logger, CancellationToken ct)
    {
        if (!File.Exists(path)) return;

        for (int i = 0; i < DeleteRetries; i++)
        {
            try
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
                await logger.LogAsync("FILESYSTEM", "DELETE", path, ct).ConfigureAwait(false);
                return;
            }
            catch 
            { 
                if (i == DeleteRetries - 1)
                {
                    if (MoveFileEx(path, null, MoveFileFlags.DelayUntilReboot))
                    {
                        Interlocked.Increment(ref _pendingRebootDeletes);
                        await logger.LogAsync("FILESYSTEM", "REBOOT_DELETE", path, ct).ConfigureAwait(false);
                    }
                }
                await Task.Delay(250, ct).ConfigureAwait(false); 
            }
        }
    }

    private static async Task DeleteRegistryFootprintAsync(DeploymentLogger logger, CancellationToken ct)
    {
        foreach (var target in DeploymentFootprint.GetUninstallRegistryPurgeTargets())
            await DeleteSubKeyTreeAsync(target.Hive, target.View, target.SubKeyPath, logger, ct).ConfigureAwait(false);

        foreach (var target in DeploymentFootprint.GetAppRegistryPurgeTargets())
            await DeleteSubKeyTreeAsync(target.Hive, target.View, target.Path, logger, ct).ConfigureAwait(false);

        foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                foreach (var mui in DeploymentFootprint.MuiCacheRelativePaths)
                {
                    try
                    {
                        using var key = baseKey.OpenSubKey(mui, true);
                        if (key == null) continue;
                        foreach (var name in key.GetValueNames())
                        {
                            if (name.Contains(DeploymentFootprint.AppName, StringComparison.OrdinalIgnoreCase) ||
                                name.Contains("snapvox", StringComparison.OrdinalIgnoreCase))
                            {
                                try { key.DeleteValue(name, false); await logger.LogAsync("REGISTRY", "MUI_PURGE", $"{hive}\\{mui}\\{name}", ct).ConfigureAwait(false); } catch { }
                            }
                        }
                    }
                    catch { }
                }
            }
        }

        await DeleteFileAssociationsRegistryAsync(logger, ct).ConfigureAwait(false);
    }

    private static async Task DeleteSubKeyTreeAsync(RegistryHive hive, RegistryView view, string path, DeploymentLogger logger, CancellationToken ct)
    {
        try 
        { 
            using var root = RegistryKey.OpenBaseKey(hive, view); 
            root.DeleteSubKeyTree(path, false); 
            await logger.LogAsync("REGISTRY", "DELETE_KEY", $"{hive}\\{path}", ct).ConfigureAwait(false); 
        } 
        catch { }
    }

    private static string DetectConflictingSoftware()
    {
        string[] targets = { 
            "Greenshot", "Lightshot", "Snagit", "ShareX", "SnippingTool", "ScreenClippingHost", 
            "Lightweight_Greenshot", "Gyazo", "FastStone", "PicPick", "Jing", "Skitch", 
            "Droplr", "CloudApp", "Monosnap", "Screenpresso", "TinyTake", "AshampooSnap", 
            "MovaviScreenRecorder", "Bandicam", "Camtasia", "Fraps", "OBS", "SnagitEditor"
        };
        try
        {
            var processes = System.Diagnostics.Process.GetProcesses();
            foreach (var p in processes)
            {
                foreach (var target in targets)
                {
                    if (p.ProcessName.Contains(target, StringComparison.OrdinalIgnoreCase)) return target;
                }
            }
            string[] keys = { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" };
            foreach (var keyPath in keys)
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyPath);
                if (key == null) continue;
                foreach (var subkeyName in key.GetSubKeyNames())
                {
                    using var subkey = key.OpenSubKey(subkeyName);
                    string name = subkey?.GetValue("DisplayName")?.ToString() ?? "";
                    foreach (var target in targets) { if (name.Contains(target, StringComparison.OrdinalIgnoreCase)) return target; }
                }
            }
        } catch { }
        return null;
    }

    private static async Task InstallFreshAsync(DeploymentProgress progress, DeploymentLogger logger, CancellationToken ct)
    {
        string installFolder = StartupTaskHelper.InstallFolder;
        Directory.CreateDirectory(installFolder);

        if (PayloadExtractor.HasEmbeddedPayload())
        {
            await Task.Run(() => PayloadExtractor.ExtractTo(installFolder), ct).ConfigureAwait(false);
            await CopyFileAggressiveAsync(StartupTaskHelper.InstallPath, StartupTaskHelper.UninstallExePath, logger, ct).ConfigureAwait(false);
        }
        else
        {
            await CopyFileAggressiveAsync(RuntimePathHelper.ExecutablePath, StartupTaskHelper.InstallPath, logger, ct).ConfigureAwait(false);
            await CopyFileAggressiveAsync(RuntimePathHelper.ExecutablePath, StartupTaskHelper.UninstallExePath, logger, ct).ConfigureAwait(false);
        }

        await InitializeInstalledConfigurationAsync(logger, ct).ConfigureAwait(false);
        await WriteUninstallRegistryAsync(logger, ct).ConfigureAwait(false);
        await RegisterFileAssociationsAsync(logger, ct).ConfigureAwait(false);
        await CreateStartMenuShortcutAsync(logger, ct).ConfigureAwait(false);
        try { StartupHelper.SetRunUser(null, StartupTaskHelper.InstallPath); } catch { }
        
        NotifyShellAssociationsChanged();
    }

    public static async Task<int> RunUninstallLauncherAsync(string[] args, CancellationToken ct)
    {
        try
        {
            if (StartupTaskHelper.IsRunningFromInstallPath())
            {
                BootstrapDebug.Log("Launcher: Relaunching from temp (install path detected).");
                return await RelaunchUninstallElevatedAsync(ct).ConfigureAwait(false);
            }

            if (StartupTaskHelper.IsElevated())
            {
                BootstrapDebug.Log("Launcher: Running worker directly (already elevated and in temp).");
                var workerArgs = args.ToList();
                if (!workerArgs.Contains("--cleanup-worker", StringComparer.OrdinalIgnoreCase))
                    workerArgs.Add("--cleanup-worker");
                return await RunUninstallAsync(workerArgs.ToArray(), ct).ConfigureAwait(false);
            }
            BootstrapDebug.Log("Launcher: Relaunching elevated.");
            return await RelaunchUninstallElevatedAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            BootstrapDebug.Log("Launcher FATAL: " + ex);
            MessageBox.Show("Uninstall could not start: " + ex.Message, "Uninstall Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static async Task<int> RelaunchUninstallElevatedAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(SessionTempFolder);
        string src = RuntimePathHelper.ExecutablePath;
        string srcDir = Path.GetDirectoryName(src);
        string temp = Path.Combine(SessionTempFolder, "Uninstall.exe");
        
        BootstrapDebug.Log($"Relaunching: Copying {src} -> {temp}");
        File.Copy(src, temp, true);
        
        if (Directory.Exists(srcDir))
        {
            foreach (string dll in Directory.GetFiles(srcDir, "*.dll"))
            {
                try 
                { 
                    string dest = Path.Combine(SessionTempFolder, Path.GetFileName(dll));
                    File.Copy(dll, dest, true); 
                } 
                catch { }
            }
        }

        int pid = Process.GetCurrentProcess().Id;
        string newArgs = $"--uninstall --cleanup-worker {pid}";

        bool started = false;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = temp,
                Arguments = newArgs,
                UseShellExecute = false,
                CreateNoWindow = false
            };
            Process.Start(psi);
            started = true;
        }
        catch (Exception ex)
        {
            BootstrapDebug.Log("Relaunch fallback to TryStartElevated: " + ex.Message);
            started = TryStartElevated(temp, newArgs);
        }

        if (!started) return 1;

        await Task.CompletedTask;
        return 0;
    }

    private static async Task<bool> VerifyZeroFootprintAsync(DeploymentLogger logger, bool verifyData, CancellationToken ct)
    {
        await Task.Delay(1000, ct).ConfigureAwait(false);
        bool clean = true;

        foreach (string target in DeploymentFootprint.GetVerificationTargets())
        {
            if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
            {
                await logger.LogAsync("VERIFY", "FAIL", $"Residue in: {target}", ct).ConfigureAwait(false);
                clean = false;
            }
        }

        foreach (var reg in DeploymentFootprint.GetUninstallRegistryPurgeTargets())
        {
            using var key = RegistryKey.OpenBaseKey(reg.Hive, reg.View).OpenSubKey(reg.SubKeyPath);
            if (key != null) { await logger.LogAsync("VERIFY", "FAIL", $"Registry: {reg.Hive}\\{reg.SubKeyPath}", ct).ConfigureAwait(false); clean = false; }
        }

        return clean;
    }

    private static async Task WriteUninstallRegistryAsync(DeploymentLogger logger, CancellationToken ct)
    {
        foreach (var purgeTarget in DeploymentFootprint.GetUninstallRegistryPurgeTargets())
        {
            await DeleteSubKeyTreeAsync(purgeTarget.Hive, purgeTarget.View, purgeTarget.SubKeyPath, logger, ct).ConfigureAwait(false);
        }

        var target = DeploymentFootprint.GetCanonicalUninstallRegistryTarget();
        using var baseKey = RegistryKey.OpenBaseKey(target.Hive, target.View);
        using var key = baseKey.CreateSubKey(target.SubKeyPath);
        
        key.SetValue("DisplayName", DeploymentFootprint.DisplayName);
        key.SetValue("UninstallString", $"\"{StartupTaskHelper.UninstallExePath}\" --uninstall");
        key.SetValue("QuietUninstallString", $"\"{StartupTaskHelper.UninstallExePath}\" --uninstall --cleanup-worker");
        key.SetValue("DisplayIcon", StartupTaskHelper.InstallPath);
        key.SetValue("InstallLocation", StartupTaskHelper.InstallFolder);
        key.SetValue("DisplayVersion", RuntimePathHelper.ProductVersion);
        key.SetValue("Publisher", DeploymentFootprint.DisplayName);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
        
        await logger.LogAsync("REGISTRY", "UNINSTALL_REGISTERED", target.SubKeyPath, ct).ConfigureAwait(false);
    }

    private static async Task RegisterFileAssociationsAsync(DeploymentLogger logger, CancellationToken ct)
    {
        string cmd = $"\"{StartupTaskHelper.InstallPath}\" \"%1\"";
        using var classes = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SOFTWARE\Classes", true);
        if (classes == null) return;

        using (var progId = classes.CreateSubKey(DeploymentFootprint.ProgId))
        {
            progId.SetValue("", "snapvox Image");
            progId.CreateSubKey(@"shell\open\command").SetValue("", cmd);
        }

        foreach (string ext in DeploymentFootprint.ImageExtensions)
        {
            using (var openWith = classes.CreateSubKey(ext + @"\OpenWithProgids", true)) openWith.SetValue(DeploymentFootprint.ProgId, Array.Empty<byte>(), RegistryValueKind.None);
            using (var shell = classes.CreateSubKey(ext + @"\shell\" + DeploymentFootprint.OpenWithShellName + @"\command", true)) shell.SetValue("", cmd);
        }
        await logger.LogAsync("REGISTRY", "FILE_ASSOC_CREATED", "ProgId and extensions registered.", ct).ConfigureAwait(false);
    }

    private static Task DeleteFileAssociationsRegistryAsync(DeploymentLogger logger, CancellationToken ct)
    {
        using var classes = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SOFTWARE\Classes", true);
        if (classes == null) return Task.CompletedTask;

        try { classes.DeleteSubKeyTree(DeploymentFootprint.ProgId, false); } catch { }
        foreach (string ext in DeploymentFootprint.ImageExtensions)
        {
            try { using var openWith = classes.OpenSubKey(ext + @"\OpenWithProgids", true); openWith?.DeleteValue(DeploymentFootprint.ProgId, false); } catch { }
            try { classes.DeleteSubKeyTree(ext + @"\shell\" + DeploymentFootprint.OpenWithShellName, false); } catch { }
        }
        return Task.CompletedTask;
    }

    private static async Task DeleteKnownShortcutsAsync(DeploymentLogger logger, CancellationToken ct)
    {
        foreach (string dir in DeploymentFootprint.GetShortcutSearchFolders())
        {
            if (!Directory.Exists(dir)) continue;
            foreach (string name in DeploymentFootprint.ShortcutFileNames)
            {
                string path = Path.Combine(dir, name);
                await DeleteFileWithRetryAsync(path, logger, ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task PurgeUserGeneratedArtifactsAsync(DeploymentLogger logger, CancellationToken ct)
    {
        foreach (string pattern in DeploymentFootprint.GetUserArtifactPatterns())
        {
            string dir = Path.GetDirectoryName(pattern);
            if (!Directory.Exists(dir)) continue;
            try
            {
                foreach (string file in Directory.EnumerateFiles(dir, Path.GetFileName(pattern)))
                    await DeleteFileWithRetryAsync(file, logger, ct).ConfigureAwait(false);
            }
            catch { }
        }
    }

    private static async Task InitializeInstalledConfigurationAsync(DeploymentLogger logger, CancellationToken ct)
    {
        Directory.CreateDirectory(StartupTaskHelper.ConfigurationFolder);
        await Task.Run(() => IniConfigurationDeployer.EnsureUserConfiguration(StartupTaskHelper.ConfigurationFolder), ct).ConfigureAwait(false);
    }

    private static async Task CreateElevatedStartupTaskAsync(string exe, DeploymentLogger logger, CancellationToken ct)
    {
        string user = WindowsIdentity.GetCurrent().Name;
        string args = $"/Create /TN \"{DeploymentFootprint.ScheduledTaskName}\" /TR \"\\\"{exe}\\\" --autorun\" /SC ONLOGON /RL HIGHEST /F";
        int exitCode = await RunHiddenProcessAsync("schtasks.exe", args, 10000, logger, ct).ConfigureAwait(false);
        if (exitCode != 0)
        {
            string fallbackArgs = $"/Create /TN \"{DeploymentFootprint.ScheduledTaskName}\" /TR \"\\\"{exe}\\\" --autorun\" /SC ONLOGON /RL HIGHEST /RU \"{user}\" /F";
            await RunHiddenProcessAsync("schtasks.exe", fallbackArgs, 10000, logger, ct).ConfigureAwait(false);
        }
    }

    private static async Task CreateStartMenuShortcutAsync(DeploymentLogger logger, CancellationToken ct)
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "SnapVox.lnk");
        await ShellLinkWriter.CreateAsync(path, StartupTaskHelper.InstallPath, StartupTaskHelper.InstallFolder, StartupTaskHelper.InstallPath + ",0", "SnapVox", ct).ConfigureAwait(false);
        await logger.LogAsync("SHELL", "SHORTCUT", path, ct).ConfigureAwait(false);
    }

    private static async Task CopyFileAggressiveAsync(string src, string dest, DeploymentLogger logger, CancellationToken ct)
    {
        if (File.Exists(dest)) { File.SetAttributes(dest, FileAttributes.Normal); File.Delete(dest); }
        File.Copy(src, dest, true);
        await logger.LogAsync("FILESYSTEM", "COPY", dest, ct).ConfigureAwait(false);
    }

    private static Task LaunchInstalledApplicationAsync() { try { Process.Start(new ProcessStartInfo { FileName = StartupTaskHelper.InstallPath, UseShellExecute = true }); } catch { } return Task.CompletedTask; }
    private static void StartElevated(string exe, string args) => TryStartElevated(exe, args);
    private static bool TryStartElevated(string exe, string args) { try { Process.Start(new ProcessStartInfo { FileName = exe, Arguments = args, UseShellExecute = true, Verb = "runas" }); return true; } catch { return false; } }
    private static bool AcquireMutex(Mutex m) => m.WaitOne(15000, false);
    private static int ParseParentPid(string[] args) => args.Select(a => int.TryParse(a, out int p) ? p : 0).FirstOrDefault(p => p > 0);
    private static async Task WaitForParentExitAsync(int pid, CancellationToken ct) { try { using var p = Process.GetProcessById(pid); await p.WaitForExitAsync(ct); } catch { } }
    private static void NotifyShellAssociationsChanged() => SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);

    private static void QueueSelfCleanup(string dir)
    {
        Process.Start(new ProcessStartInfo { FileName = "cmd.exe", Arguments = $"/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"{dir}\"", CreateNoWindow = true, UseShellExecute = false });
    }

    private static DeploymentProgress CreateDeploymentProgress(string title, string log) => InstallHostContext.HeadlessInstallerActive ? null : new DeploymentProgress(title, log);

    private static async Task ReportAsync(DeploymentProgress p, DeploymentLogger l, int pct, string phase, string status, string detail, CancellationToken ct, Exception ex = null)
    {
        p?.Update(pct, $"[{phase}] {status}: {detail}");
        if (l != null) await l.LogAsync(phase, status, detail, ct, ex).ConfigureAwait(false);
    }

    private static async Task<int> RunHiddenProcessAsync(string exe, string args, int timeout, DeploymentLogger logger, CancellationToken ct)
    {
        using var p = new Process { StartInfo = new ProcessStartInfo { FileName = exe, Arguments = args, UseShellExecute = false, CreateNoWindow = true } };
        p.Start();
        await p.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromMilliseconds(timeout), ct);
        return p.ExitCode;
    }

    private static async Task RelaunchInstallFromTempAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(SessionTempFolder);
        string temp = Path.Combine(SessionTempFolder, "Setup.exe");
        File.Copy(RuntimePathHelper.ExecutablePath, temp, true);
        Process.Start(new ProcessStartInfo { FileName = temp, Arguments = "--install --install-worker", UseShellExecute = true, Verb = "runas" });
        await Task.CompletedTask;
    }

    private sealed class DeploymentProgress : IDisposable
    {
        private forms.DeploymentProgressWindow _window;
        public DeploymentProgress(string title, string log) 
        { 
            Dispatcher.UIThread.Post(() => { 
                _window = new forms.DeploymentProgressWindow(title, log); 
                _window.Show(); 
            }); 
        }
        public void Update(int pct, string status) 
        { 
            Dispatcher.UIThread.Post(() => {
                _window?.UpdateProgress(pct); 
                _window?.UpdateStatus(status); 
            });
        }
        public void Dispose() 
        { 
            Dispatcher.UIThread.Post(() => {
                try { _window?.Close(); } catch { }
            }); 
        }
    }

    private sealed class DeploymentLogger : IAsyncDisposable
    {
        private readonly StreamWriter _writer;
        private DeploymentLogger(StreamWriter sw) { _writer = sw; }
        public static async Task<DeploymentLogger> CreateAsync(string path, string session, CancellationToken ct)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var sw = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read), Encoding.UTF8) { AutoFlush = true };
                await sw.WriteLineAsync($"\n=== {session} {DateTime.Now:O} PID={Environment.ProcessId} ===").ConfigureAwait(false);
                return new DeploymentLogger(sw);
            }
            catch
            {
                var sw = new StreamWriter(Stream.Null, Encoding.UTF8) { AutoFlush = true };
                return new DeploymentLogger(sw);
            }
        }
        public async Task LogAsync(string phase, string action, string detail, CancellationToken ct, Exception ex = null)
        {
            await _writer.WriteLineAsync($"{DateTime.Now:HH:mm:ss.fff}|{phase}|{action}|{detail}{(ex != null ? "|" + ex : "")}").ConfigureAwait(false);
        }
        public async ValueTask DisposeAsync() { _writer.Dispose(); await ValueTask.CompletedTask; }
    }
}
