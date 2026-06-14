using snapvox.native;
using snapvox.native.foundation;
using snapvox.native.graphics;
using snapvox.native.ui;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using snapvox.foundation.core.AvaloniaShims;
using Microsoft.Win32;
using snapvox.foundation.core;
using log4net;
using System.Linq;

namespace snapvox.helpers;

public static class StartupTaskHelper
{
    private static ILog Log => LogHelper.IsInitialized ? snapvox.foundation.core.LogHelper.GetLogger(typeof(StartupTaskHelper)) : null;
    private const string MsiMutexName = @"Global\_MSIExecute";
    private const string ScheduledTaskName = "snapvox";
    private const string ConfigureAdminStartupArgument = "--configure-admin-startup";
    private const string RemoveAdminStartupArgument = "--remove-admin-startup";

    public static readonly string InstallFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "snapvox");
    public static readonly string ConfigurationFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "snapvox");
    public static readonly string InstallPath = Path.Combine(InstallFolder, "snapvox.exe");
    public static readonly string UninstallExePath = Path.Combine(InstallFolder, "Uninstall.exe");

    [DllImport("kernel32.dll")]
    private static extern void ExitProcess(uint uExitCode);

    private static void LogSuppressedException(string operation, Exception ex)
    {
        if (ex == null)
        {
            return;
        }

        Log?.Warn(operation, ex);
        ExecutionTrace.LogException("StartupTaskHelper." + operation, ex, string.Empty);
    }

    private static bool IsExpectedProcessInspectionException(Exception ex)
    {
        return ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is Win32Exception || ex is NotSupportedException;
    }

    public static bool IsElevated()
    {
        using (var identity = WindowsIdentity.GetCurrent())
        {
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public static bool IsMsiBusy()
    {
        try { using (var mutex = Mutex.OpenExisting(MsiMutexName)) return true; }
        catch (WaitHandleCannotBeOpenedException) { }
        catch (Exception ex) { LogSuppressedException("IsMsiBusy.OpenMutex", ex); }

        try
        {
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Updates"))
            {
                if (key?.GetValue("UpdateExeVolatile") != null) return true;
            }
        }
        catch (Exception ex) { LogSuppressedException("IsMsiBusy.Registry", ex); }
        return false;
    }

    public static void RestartElevated(string args = "")
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = RuntimePathHelper.ExecutablePath,
                Arguments = args,
                UseShellExecute = true,
                Verb = "runas"
            });
        }
        catch (Exception ex) { LogSuppressedException("RestartElevated", ex); }
    }

    private static async Task<int> RunHiddenProcessAsync(string fileName, string arguments, int timeoutMilliseconds)
    {
        using (var process = new Process())
        {
            process.StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            if (!process.Start())
            {
                ExecutionTrace.LogEvent("StartupTaskHelper.RunHiddenProcess", "StartFailed", fileName + " " + arguments);
                return -1;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                try { process.Kill(); } catch (Exception ex) { LogSuppressedException("RunHiddenProcess.Kill", ex); }
                ExecutionTrace.LogEvent("StartupTaskHelper.RunHiddenProcess", "Timeout", fileName + " " + arguments);
                try { await process.WaitForExitAsync().ConfigureAwait(false); } catch { }
                return -2;
            }

            string output = await outputTask.ConfigureAwait(false);
            string error = await errorTask.ConfigureAwait(false);
            ExecutionTrace.LogEvent("StartupTaskHelper.RunHiddenProcess", "Exit", string.Format("{0};{1};{2};{3}", fileName, arguments, process.ExitCode, output + error));
            return process.ExitCode;
        }
    }

    private static string GetStartupTaskExecutablePath()
    {
        if (File.Exists(InstallPath))
        {
            return InstallPath;
        }

        return RuntimePathHelper.ExecutablePath;
    }

    public static bool IsAdminStartupCommand(string[] args)
    {
        return args != null && args.Any(arg => arg.Equals(ConfigureAdminStartupArgument, StringComparison.OrdinalIgnoreCase) || arg.Equals(RemoveAdminStartupArgument, StringComparison.OrdinalIgnoreCase));
    }

    public static async Task<int> RunAdminStartupCommandAsync(string[] args)
    {
        if (args == null)
        {
            return 1;
        }

        if (args.Any(arg => arg.Equals(ConfigureAdminStartupArgument, StringComparison.OrdinalIgnoreCase)))
        {
            return await ConfigureElevatedStartupTaskInCurrentProcessAsync(null).ConfigureAwait(false) ? 0 : 1;
        }

        if (args.Any(arg => arg.Equals(RemoveAdminStartupArgument, StringComparison.OrdinalIgnoreCase)))
        {
            return await DeleteElevatedStartupTaskInCurrentProcessAsync().ConfigureAwait(false) ? 0 : 1;
        }

        return 1;
    }

    private static async Task<bool> CreateElevatedStartupTaskAsync(string executablePath)
    {
        try
        {
            Log?.Info("OS: Registering elevated scheduled task for: " + executablePath);
            string userName = WindowsIdentity.GetCurrent().Name;
            string arguments = string.Format("/Create /TN \"{0}\" /TR \"\\\"{1}\\\" --autorun\" /SC ONLOGON /RL HIGHEST /F", ScheduledTaskName, executablePath);
            int exitCode = await RunHiddenProcessAsync("schtasks.exe", arguments, 15000).ConfigureAwait(false);
            if (exitCode != 0)
            {
                string fallbackArguments = string.Format("/Create /TN \"{0}\" /TR \"\\\"{1}\\\" --autorun\" /SC ONLOGON /RL HIGHEST /RU \"{2}\" /F", ScheduledTaskName, executablePath, userName);
                exitCode = await RunHiddenProcessAsync("schtasks.exe", fallbackArguments, 15000).ConfigureAwait(false);
                if (exitCode != 0)
                {
                    Log?.Error("OS: Scheduled task registration failed with exit code " + exitCode);
                    return false;
                }
            }
            Log?.Info("OS: Elevated scheduled task registered successfully for " + userName);
            return true;
        }
        catch (Exception ex)
        {
            LogSuppressedException("CreateElevatedStartupTask", ex);
            return false;
        }
    }

    public static async Task<bool> ConfigureElevatedStartupTaskAsync(string executablePath = null)
    {
        try
        {
            string targetExecutable = string.IsNullOrWhiteSpace(executablePath) ? GetStartupTaskExecutablePath() : executablePath;
            if (!IsElevated())
            {
                bool elevated = await RunElevatedAdminCommandAsync(ConfigureAdminStartupArgument).ConfigureAwait(false);
                return elevated && await HasElevatedStartupTaskAsync().ConfigureAwait(false);
            }

            return await ConfigureElevatedStartupTaskInCurrentProcessAsync(targetExecutable).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogSuppressedException("ConfigureElevatedStartupTask", ex);
            return false;
        }
    }

    private static async Task<bool> ConfigureElevatedStartupTaskInCurrentProcessAsync(string executablePath)
    {
        try
        {
            if (!IsElevated())
            {
                return false;
            }

            string targetExecutable = string.IsNullOrWhiteSpace(executablePath) ? GetStartupTaskExecutablePath() : executablePath;
            if (!await CreateElevatedStartupTaskAsync(targetExecutable).ConfigureAwait(false))
            {
                return false;
            }

            StartupHelper.DeleteRunAll();
            StartupHelper.DeleteRunUser();
            StartupHelper.DeleteStartupFolderShortcut();
            ExecutionTrace.LogEvent("StartupTaskHelper.ScheduledTask", "Configured", targetExecutable);
            return await HasElevatedStartupTaskAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogSuppressedException("ConfigureElevatedStartupTaskInCurrentProcess", ex);
            return false;
        }
    }

    public static async Task<bool> HasElevatedStartupTaskAsync()
    {
        try
        {
            int exitCode = await RunHiddenProcessAsync("schtasks.exe", string.Format("/Query /TN \"{0}\"", ScheduledTaskName), 10000).ConfigureAwait(false);
            return exitCode == 0;
        }
        catch (Exception ex)
        {
            LogSuppressedException("HasElevatedStartupTask", ex);
            return false;
        }
    }

    public static async Task<bool> DeleteElevatedStartupTaskAsync()
    {
        try
        {
            if (!IsElevated())
            {
                bool elevated = await RunElevatedAdminCommandAsync(RemoveAdminStartupArgument).ConfigureAwait(false);
                return elevated && !await HasElevatedStartupTaskAsync().ConfigureAwait(false);
            }

            return await DeleteElevatedStartupTaskInCurrentProcessAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogSuppressedException("DeleteElevatedStartupTask", ex);
            return false;
        }
    }

    private static async Task<bool> DeleteElevatedStartupTaskInCurrentProcessAsync()
    {
        try
        {
            if (!IsElevated())
            {
                return false;
            }

            int exitCode = await RunHiddenProcessAsync("schtasks.exe", string.Format("/Delete /TN \"{0}\" /F", ScheduledTaskName), 10000).ConfigureAwait(false);
            StartupHelper.SetRunUser(null, GetStartupTaskExecutablePath());
            ExecutionTrace.LogEvent("StartupTaskHelper.ScheduledTask", "Delete", exitCode.ToString());
            return exitCode == 0 || !await HasElevatedStartupTaskAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogSuppressedException("DeleteElevatedStartupTaskInCurrentProcess", ex);
            return false;
        }
    }

    private static async Task<bool> RunElevatedAdminCommandAsync(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = RuntimePathHelper.ExecutablePath,
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas"
            });

            if (process == null)
            {
                return false;
            }

            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90)).ConfigureAwait(false);
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            LogSuppressedException("RunElevatedAdminCommand", ex);
            return false;
        }
    }

    public static async Task<bool> TryRunElevatedStartupTaskAsync()
    {
        try
        {
            Log?.Info("OS: Attempting to trigger elevated scheduled task...");
            int exitCode = await RunHiddenProcessAsync("schtasks.exe", string.Format("/Run /TN \"{0}\"", ScheduledTaskName), 10000).ConfigureAwait(false);
            Log?.Info("OS: Scheduled task execution trigger returned: " + exitCode);
            ExecutionTrace.LogEvent("StartupTaskHelper.ScheduledTask", "Run", exitCode.ToString());
            return exitCode == 0;
        }
        catch (Exception ex)
        {
            LogSuppressedException("TryRunElevatedStartupTask", ex);
            return false;
        }
    }

    public static bool IsRunningFromInstallPath()
    {
        try
        {
            string currentExecutable = Path.GetFullPath(RuntimePathHelper.ExecutablePath).TrimEnd(Path.DirectorySeparatorChar);
            string expectedInstallPath = Path.GetFullPath(InstallPath).TrimEnd(Path.DirectorySeparatorChar);
            if (string.Equals(currentExecutable, expectedInstallPath, StringComparison.OrdinalIgnoreCase)) return true;
            string expectedUninstallPath = Path.GetFullPath(UninstallExePath).TrimEnd(Path.DirectorySeparatorChar);
            return string.Equals(currentExecutable, expectedUninstallPath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            string executablePath = RuntimePathHelper.ExecutablePath;
            ExecutionTrace.LogEvent("StartupTaskHelper", "InstallPathFallback", executablePath);
            return string.Equals(executablePath, InstallPath, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(executablePath, UninstallExePath, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static bool ShouldSkipForcedInstallRedirect()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("snapvox_SKIP_INSTALL_GUARD"), "1", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    public static async Task KillAllProcessesAsync(Action<string> updateStatus = null, CancellationToken cancellationToken = default)
    {
        string[] processNames = { 
            "snapvox", 
            "Uninstall", 
            "snapvox_Cleanup", 
            "snapvox_tesseract",
            "snapvox",
            "snapvoxImgEditor"
        };
        var current = Process.GetCurrentProcess();
        string targetDir = InstallFolder.TrimEnd(Path.DirectorySeparatorChar);

        for (int retry = 0; retry < 5; retry++)
        {
            bool foundAny = false;
            
            foreach (string name in processNames)
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    if (process.Id == current.Id) continue;
                    foundAny = true;
                    await TerminateProcessAsync(process, updateStatus, cancellationToken).ConfigureAwait(false);
                }
            }

            if (Directory.Exists(targetDir))
            {
                foreach (var process in Process.GetProcesses())
                {
                    if (process.Id == current.Id) continue;
                    try
                    {
                        string procPath = process.MainModule.FileName;
                        if (procPath.StartsWith(targetDir, StringComparison.OrdinalIgnoreCase))
                        {
                            foundAny = true;
                            await TerminateProcessAsync(process, updateStatus, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    catch { }
                }
            }

            if (!foundAny) break;
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task TerminateProcessAsync(Process process, Action<string> updateStatus, CancellationToken cancellationToken)
    {
        try
        {
            string msg = string.Format("AUTO-CLEANUP: Terminating PID {0} ({1})...", process.Id, process.ProcessName);
            Log?.Info(msg);
            updateStatus?.Invoke(msg);
            process.Kill();
            try
            {
                await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromMilliseconds(3000), cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                using var taskKill = Process.Start(new ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = string.Format("/F /PID {0}", process.Id),
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
                if (taskKill != null)
                {
                    await taskKill.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromMilliseconds(2000), cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
                if (!IsExpectedProcessInspectionException(ex)) Log?.Warn("Auto-cleanup failed for PID " + process.Id + ": " + ex.Message);
        }
    }

    public static async Task<int> PerformInstallAsync()
    {
        return await DeploymentLifecycle.RunInstallAsync().ConfigureAwait(false);
    }

    public static async Task<int> PerformUninstallAsync(string[] args)
    {
        return await DeploymentLifecycle.RunUninstallAsync(args).ConfigureAwait(false);
    }

    private static void ExitCurrentProcess(int exitCode)
    {
        try
        {
            ExitProcess(unchecked((uint)exitCode));
        }
        finally
        {
            Environment.Exit(exitCode);
        }
    }

    public static DialogResult ShowForegroundMessageBox(string message, string title, MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.Information)
    {
        return MessageBox.Show(message, title, buttons, icon);
    }
}


