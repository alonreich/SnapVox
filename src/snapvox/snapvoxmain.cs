using System;
using System.Linq;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using snapvox.foundation.core;
using snapvox.foundation.IniFile;
using snapvox.helpers;
using log4net;
using Avalonia;

namespace snapvox;

public class snapvoxMain
{
    private static ILog LOG;
    public static string LogFileLocation;

    static snapvoxMain()
    {
        AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
    }

    private static Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
    {
        Assembly ayResult = null;
        string sShortAssemblyName = args.Name.Split(',')[0];
        Assembly[] ayAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (Assembly ayAssembly in ayAssemblies)
        {
            if (sShortAssemblyName != ayAssembly.FullName.Split(',')[0]) continue;
            ayResult = ayAssembly;
            break;
        }
        return ayResult;
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    [STAThread]
    public static async Task Main(string[] args)
    {
        args ??= Array.Empty<string>();
        bool isInstaller = !StartupTaskHelper.IsRunningFromInstallPath() && !DeploymentLifecycle.IsLifecycleCommand(args);
        bool isLifecycle = DeploymentLifecycle.IsLifecycleCommand(args);

        bool hasFiles = args.Any(a => !a.StartsWith("-") && !a.StartsWith("/"));

        using var appMutex = new Mutex(false, "Global\\SnapVox_SingleInstance_Mutex", out bool createdNew);
        if (!createdNew && !isInstaller && !isLifecycle && !hasFiles)
        {
            return;
        }

        InstallHostContext.WriteEarlyTrace("ENTER Main PID=" + Environment.ProcessId + " exe=" + Environment.ProcessPath);
        InstallHostContext.WriteEarlyTrace("Args: " + string.Join(' ', args));

        bool headlessInstaller = DeploymentLifecycle.ShouldRunHeadlessDeployment(args);
        if (!headlessInstaller)
        {
            BootstrapDebug.Clear();
        }

        BootstrapDebug.Log("--- Application Bootstrap Starting ---");
        BootstrapDebug.Log("Args: " + string.Join(' ', args));
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += Task_UnhandledException;

        try
        {
            if (headlessInstaller && !DeploymentLifecycle.IsLifecycleCommand(args))
            {
                BootstrapDebug.Log("Headless deployment path (no Avalonia / no Skia preload).");
                InstallHostContext.WriteEarlyTrace("Headless deployment branch");
                Environment.Exit(await DeploymentLifecycle.RunHeadlessDeploymentCommandAsync(args).ConfigureAwait(false));
                return;
            }

            if (headlessInstaller)
            {
                BootstrapDebug.Log("Installer mode detected. Flowing to Avalonia for UI...");
            }

            if (!StartupTaskHelper.IsRunningFromInstallPath()
                && !DeploymentLifecycle.IsLifecycleCommand(args))
            {
                BootstrapDebug.Log("Stage 1: Relocating installer to temporary directory.");
                string tempInstallerPath = Path.Combine(DeploymentFootprint.DeploymentTempRoot, "Install", Path.GetFileName(RuntimePathHelper.ExecutablePath));
                Directory.CreateDirectory(Path.GetDirectoryName(tempInstallerPath));
                File.Copy(RuntimePathHelper.ExecutablePath, tempInstallerPath, true);
                Process.Start(new ProcessStartInfo
                {
                    FileName = tempInstallerPath,
                    Arguments = "--install --install-worker",
                    UseShellExecute = true,
                    Verb = "runas"
                });
                return;
            }

            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.Name = RuntimePathHelper.ProductName;

            LogFileLocation = LogHelper.InitializeLog4Net();
            LOG = LogHelper.GetLogger("snapvox");

            if (PayloadExtractor.HasEmbeddedPayload())
            {
                BootstrapDebug.Log("Extracting Avalonia/Skia dependencies for desktop host...");
                PayloadExtractor.ExtractCriticalDependencies();
            }

            BootstrapDebug.Log("Launching Avalonia App...");
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            string msg = "CRITICAL BOOTSTRAP ERROR: " + ex.ToString();
            BootstrapDebug.Log($"Main Exception: {ex}");
            InstallHostContext.WriteEarlyTrace("Main exception: " + ex.Message);
            if (LOG != null) LOG.Fatal(msg);
            ExecutionTrace.LogException("Bootstrap.Main", ex, msg);
        }
    }

    internal static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Exception ex = e.ExceptionObject as Exception;
        LOG?.Error("UnhandledException: " + (ex?.Message ?? "Unknown"), ex);
        BootstrapDebug.Log($"UnhandledException: {ex}");
        InstallHostContext.WriteEarlyTrace("UnhandledException: " + ex);
        ExecutionTrace.LogException("AppDomain.UnhandledException", ex, string.Empty);
    }

    internal static void Task_UnhandledException(object sender, UnobservedTaskExceptionEventArgs args)
    {
        Exception ex = args.Exception;
        LOG?.Error("TaskException: " + ex.Message, ex);
        BootstrapDebug.Log($"TaskException: {ex}");
        ExecutionTrace.LogException("Task.UnobservedTaskException", ex, string.Empty);
        args.SetObserved();
    }
}
