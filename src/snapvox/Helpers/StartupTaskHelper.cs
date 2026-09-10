using snapvox.native;
using snapvox.native.foundation;
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
    private const string ScheduledTaskName = "snapvox";
    private const string ConfigureAdminStartupArgument = "--configure-admin-startup";
    private const string RemoveAdminStartupArgument = "--remove-admin-startup";

    public static readonly string InstallFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "snapvox");
    public static readonly string ConfigurationFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "snapvox");
    public static readonly string InstallPath = Path.Combine(InstallFolder, "snapvox.exe");
    public static readonly string UninstallExePath = Path.Combine(InstallFolder, "Uninstall.exe");

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
        string definitionPath = Path.Combine(Path.GetTempPath(), "SnapVox", "Lifecycle", "Startup_" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            string sid = identity.User?.Value ?? throw new InvalidOperationException("Cannot identify the startup user.");
            Directory.CreateDirectory(Path.GetDirectoryName(definitionPath));
            await File.WriteAllTextAsync(definitionPath, StartupTaskDefinition.Create(executablePath, sid)).ConfigureAwait(false);
            int exitCode = await RunHiddenProcessAsync("schtasks.exe",
                $"/Create /TN \"{ScheduledTaskName}\" /XML \"{definitionPath}\" /F", 15000).ConfigureAwait(false);
            if (exitCode != 0)
            {
                Log?.Error("Scheduled task registration failed with exit code " + exitCode);
                return false;
            }
            Log?.Info("Elevated startup registered with battery restrictions disabled for " + sid);
            return true;
        }
        catch (Exception ex)
        {
            LogSuppressedException("CreateElevatedStartupTask", ex);
            return false;
        }
        finally
        {
            try { if (File.Exists(definitionPath)) File.Delete(definitionPath); }
            catch (Exception ex) { LogSuppressedException("DeleteStartupDefinition", ex); }
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

    internal static bool IsInstalledExecutable(string executablePath, string installFolder = null)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return false;
        try
        {
            string root = Path.GetFullPath(installFolder ?? InstallFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(executablePath);
            return path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
        {
            return false;
        }
    }

    internal static int[] FindRunningInstalledProcesses()
    {
        var ids = new System.Collections.Generic.List<int>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId) continue;
                try
                {
                    if (IsInstalledExecutable(process.MainModule?.FileName)) ids.Add(process.Id);
                }
                catch (Exception ex) when (IsExpectedProcessInspectionException(ex)) { }
            }
        }
        return ids.ToArray();
    }

    internal static void RequireInstalledApplicationsClosed()
    {
        if (FindRunningInstalledProcesses().Length != 0)
            throw new IOException("SnapVox is still running. Save your work, exit SnapVox from its tray menu, and run setup again. No application was forcibly closed.");
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBox(IntPtr hWnd, string lpText, string lpCaption, uint uType);


    private const uint MbOk = 0x00000000;
    private const uint MbOkCancel = 0x00000001;
    private const uint MbAbortRetryIgnore = 0x00000002;
    private const uint MbYesNoCancel = 0x00000003;
    private const uint MbYesNo = 0x00000004;
    private const uint MbRetryCancel = 0x00000005;
    private const uint MbIconError = 0x00000010;
    private const uint MbIconQuestion = 0x00000020;
    private const uint MbIconWarning = 0x00000030;
    private const uint MbIconInformation = 0x00000040;
    private const uint MbSetForeground = 0x00010000;
    private const uint MbTopmost = 0x00040000;



    private const int IdCancel = 2;
    private const int IdAbort = 3;
    private const int IdRetry = 4;
    private const int IdIgnore = 5;
    private const int IdYes = 6;
    private const int IdNo = 7;

    public static DialogResult ShowForegroundMessageBox(string message, string title, MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.Information, IntPtr ownerHWnd = default)
    {
        try
        {
            uint type = MbOk;
            if (buttons == MessageBoxButtons.OKCancel) type = MbOkCancel;
            else if (buttons == MessageBoxButtons.AbortRetryIgnore) type = MbAbortRetryIgnore;
            else if (buttons == MessageBoxButtons.YesNoCancel) type = MbYesNoCancel;
            else if (buttons == MessageBoxButtons.YesNo) type = MbYesNo;
            else if (buttons == MessageBoxButtons.RetryCancel) type = MbRetryCancel;

            if (icon == MessageBoxIcon.Hand || icon == MessageBoxIcon.Stop || icon == MessageBoxIcon.Error) type |= MbIconError;
            else if (icon == MessageBoxIcon.Question) type |= MbIconQuestion;
            else if (icon == MessageBoxIcon.Exclamation || icon == MessageBoxIcon.Warning) type |= MbIconWarning;
            else if (icon == MessageBoxIcon.None) {  }
            else type |= MbIconInformation;

            type |= MbSetForeground | MbTopmost;

            int result = MessageBox(ownerHWnd, message, title, type);

            if (result == IdYes) return DialogResult.Yes;
            if (result == IdNo) return DialogResult.No;
            if (result == IdCancel) return DialogResult.Cancel;
            if (result == IdAbort) return DialogResult.Abort;
            if (result == IdRetry) return DialogResult.Retry;
            if (result == IdIgnore) return DialogResult.Ignore;
            return DialogResult.OK;
        }
        catch
        {



            return buttons == MessageBoxButtons.OK ? DialogResult.OK : DialogResult.Cancel;
        }
    }
}


