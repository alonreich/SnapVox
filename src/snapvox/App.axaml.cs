using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using snapvox.native.foundation;
using snapvox.foundation.core;
using snapvox.foundation.IniFile;
using snapvox.foundation.Interfaces;
using snapvox.foundation.interfaces.Ocr;
using snapvox.helpers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace snapvox
{
    public class App : Application
    {
        private static TrayIcon _trayIcon;
        private static WindowIcon _blueIcon;
        private static WindowIcon _redIcon;
        private static IClassicDesktopStyleApplicationLifetime _desktop;
        private static CancellationTokenSource _mainAppCts = new CancellationTokenSource();
        public static bool IsNoTrayMode { get; set; }

        public override void Initialize() { AvaloniaXamlLoader.Load(this); }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                _desktop = desktop;
                desktop.MainWindow = null;
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                Task.Run(() => InitializeApplicationAsync(desktop, _mainAppCts.Token));
            }
            base.OnFrameworkInitializationCompleted();
        }

        private async Task InitializeApplicationAsync(IClassicDesktopStyleApplicationLifetime desktop, CancellationToken cancellationToken)
        {
            var ocrProviders = new List<IOcrProvider>();
            try
            {
                LogHelper.InitializeLog4Net();
                var log = LogHelper.GetLogger(typeof(App));
                log.Info("--- Application Bootstrap Starting ---");
                log.Info($"Executable Path: {Process.GetCurrentProcess().MainModule?.FileName}");

                string[] args = desktop.Args ?? Array.Empty<string>();
                log.Info($"Command line arguments: {string.Join(" ", args)}");

                if (DeploymentLifecycle.IsLifecycleCommand(args))
                {
                    log.Info("Running deployment lifecycle command...");
                    int exitCode = await DeploymentLifecycle.RunLifecycleCommandAsync(args);
                    log.Info($"Deployment command finished with exit code: {exitCode}");
                    Dispatcher.UIThread.Post(() => desktop.Shutdown(exitCode));
                    return;
                }
                InitializePersistentConfiguration();
                var options = snapvoxCommandLine.Parse(args);
                UiClipboard.RegisterGetter(() => desktop.MainWindow?.Clipboard ?? (desktop.Windows.FirstOrDefault()?.Clipboard));
                using (var instanceMutex = ResourceMutex.Create("snapvox_MainForm", "snapvox instance", true))
                {
                    if (!instanceMutex.IsLocked)
                    {
                        log.Warn("Another instance of SnapVox is already running.");
                        if (options.Files.Length > 0) { IsNoTrayMode = true; log.Info("Proceeding in No-Tray mode for file processing."); }
                        else { log.Info("Shutting down duplicate instance."); Dispatcher.UIThread.Post(() => desktop.Shutdown()); return; }
                    }
                    SimpleServiceProvider.Current.AddService<IOcrResultHandler>(new OcrResultHandler());
#if USE_TESSERACT
                    log.Info("Using Tesseract OCR Provider with Windows OCR fallback.");
                    ocrProviders.Add(new native.TesseractOcrProvider());
                    ocrProviders.Add(new native.Win10OcrProvider());
#else
                    log.Info("Using Windows 10 OCR Provider.");
                    ocrProviders.Add(new native.Win10OcrProvider());
#endif
                    SimpleServiceProvider.Current.AddService<IOcrProvider>(ocrProviders);
                    _ = Task.Run(OcrInstallationHelper.InstallHebrewOcr);
                    RetentionHelper.Start();
                    if (!IsNoTrayMode) 
                    { 
                        log.Info("Initializing Tray Icon and Hotkeys...");
                        await InitializeTrayIconAsync(); 
                        HotkeyManager.Start(); 
                        log.Info("Tray Icon and Hotkeys initialized successfully.");
                    }
                    foreach (var file in options.Files)
                    {
                        if (!File.Exists(file)) { log.Warn($"File not found: {file}"); continue; }
                        log.Info($"Opening file for editing: {file}");
                        using SixLabors.ImageSharp.Image loaded = SixLabors.ImageSharp.Image.Load(file);
                        SixLabors.ImageSharp.Image owned = loaded.Clone();
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            SixLabors.ImageSharp.Image imageForEditor = owned;
                            snapvox.editor.forms.ImageEditorWindow editor = null;
                            try
                            {
                                editor = new snapvox.editor.forms.ImageEditorWindow();
                                var screen = editor.Screens.Primary;
                                var rect = RECT.FromXYWH(screen.Bounds.X + (screen.Bounds.Width - imageForEditor.Width) / 2, screen.Bounds.Y + (screen.Bounds.Height - imageForEditor.Height) / 2, imageForEditor.Width, imageForEditor.Height);
                                editor.SetImage(imageForEditor, rect);
                                owned = null;
                                imageForEditor = null;
                                editor.Show();
                                
                                if (IsNoTrayMode)
                                {
                                    editor.Closed += (s, ev) =>
                                    {
                                        if (desktop.Windows.Count == 0)
                                        {
                                            log.Info("All windows closed in No-Tray mode. Shutting down.");
                                            desktop.Shutdown();
                                        }
                                    };
                                }

                                log.Info($"Editor window shown for file: {file}");
                            }
                            catch
                            {
                                imageForEditor?.Dispose();
                                editor?.Close();
                                throw;
                            }
                        });
                        owned?.Dispose();
                    }
                    if (IsNoTrayMode && desktop.Windows.Count == 0) { log.Info("No files processed and No-Tray mode active. Shutting down."); Dispatcher.UIThread.Post(() => desktop.Shutdown()); return; }
                    log.Info("Application initialization complete. Entering wait loop.");
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
            }
            catch (TaskCanceledException) { }
            catch (Exception ex) 
            { 
                LogHelper.GetLogger(typeof(App)).Fatal("Critical application failure during initialization.", ex);
                Dispatcher.UIThread.Post(() => { new forms.DeploymentProgressWindow("Critical Error: " + ex.Message).Show(); desktop.Shutdown(); }); 
            }
            finally
            {
                RetentionHelper.Stop();
                foreach (var ocrProvider in ocrProviders)
                {
                    if (ocrProvider is IAsyncDisposable asyncDisposable)
                    {
                        await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                    }
                    else if (ocrProvider is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
            }
        }

        private void InitializePersistentConfiguration()
        {
            try
            {
                string configurationFolder = StartupTaskHelper.ConfigurationFolder;
                if (!StartupTaskHelper.IsRunningFromInstallPath()) { configurationFolder = Path.Combine(DeploymentFootprint.TempAppFolder, "Config"); }
                Directory.CreateDirectory(configurationFolder);
                IniConfigurationDeployer.EnsureDefaultsFile(configurationFolder);
                IniConfig.IniDirectory = configurationFolder;
                IniConfig.Init("snapvox", IniConfigurationDeployer.ConfigBaseName);
                var core = IniConfig.GetIniSection<CoreConfiguration>();
                if (string.IsNullOrWhiteSpace(core.Language)) core.Language = "en-US";
            }
            catch { }
        }

        private async Task InitializeTrayIconAsync()
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var icons = TrayIcon.GetIcons(this);
                if (icons != null && icons.Count > 0)
                {
                    _trayIcon = icons[0];
                    try
                    {
                        using var blueAssetLoader = AssetLoader.Open(new Uri("avares://SnapVox/SnapVox.ico"));
                        _blueIcon = new WindowIcon(blueAssetLoader);
                        _trayIcon.Icon = _blueIcon;

                        using var assetLoader = AssetLoader.Open(new Uri("avares://SnapVox/SnapVox.ico"));
                        using var avaloniaBitmap = new Avalonia.Media.Imaging.Bitmap(assetLoader);
                        
                        using var bridgeMs = new MemoryStream();
                        avaloniaBitmap.Save(bridgeMs);
                        bridgeMs.Position = 0;

                        using (var image = SixLabors.ImageSharp.Image.Load<Bgra32>(bridgeMs))
                        {
                            image.Mutate(x => x.ProcessPixelRowsAsVector4(row => 
                            { 
                                for (int i = 0; i < row.Length; i++) 
                                { 
                                    float r = row[i].X;
                                    float g = row[i].Y;
                                    float b = row[i].Z;
                                    
                                    row[i].X = Math.Max(r, Math.Max(g, b));
                                    row[i].Y = g * 0.2f;
                                    row[i].Z = b * 0.2f;
                                } 
                            }));
                            var ms = new MemoryStream(); 
                            image.Save(ms, new PngEncoder()); 
                            ms.Seek(0, SeekOrigin.Begin); 
                            _redIcon = new WindowIcon(ms); 
                        }
                    }
                    catch (Exception ex)
                    {
                        LogHelper.GetLogger(typeof(App)).Error("Failed to create red tray icon", ex);
                    }
                }
            });
        }
        private static bool _forceRedState = false;

        public static void ForceRedTrayIcon(bool force)
        {
            _forceRedState = force;
            if (!force) SetTrayIconState(false);
            else SetTrayIconState(true);
        }

        public static void SetTrayIconState(bool active)
        {
            if (_trayIcon == null) return;
            if (!active && _forceRedState) return;
            Dispatcher.UIThread.Post(() => { _trayIcon.Icon = active && _redIcon != null ? _redIcon : _blueIcon; });
        }

        public static async Task FlickerTrayIcon()
        {
            if (_trayIcon == null || _redIcon == null) return;
            SetTrayIconState(true);
            await Task.Delay(150);
            SetTrayIconState(false);
        }

        public void OnTrayIconClicked(object sender, EventArgs e) => OnCaptureRegionClick(sender, e);
        public void OnCaptureRegionClick(object sender, EventArgs e) => CaptureHelper.CaptureRegion(false);
        public void OnCaptureWindowClick(object sender, EventArgs e) => CaptureHelper.CaptureActiveWindow(false);
        private void OnCaptureFullscreenClick(object sender, EventArgs e) => CaptureHelper.CaptureFullscreen(false, ScreenCaptureMode.FullScreen);
        private void OnOpenFromClipboardClick(object sender, EventArgs e) => CaptureHelper.CaptureClipboard();
        public void OnShowHistoryClick(object sender, EventArgs e)
        {
            try 
            { 
                string tempPath = Path.Combine(Path.GetTempPath(), "SnapVox");
                Directory.CreateDirectory(tempPath);
                Process.Start(new ProcessStartInfo { FileName = tempPath, UseShellExecute = true }); 
            } 
            catch { }
        }
        private void OnSettingsClick(object sender, EventArgs e)
        {
            try
            {
                var settingsWin = new snapvox.Forms.SettingsWindow();
                settingsWin.Show();
            }
            catch (Exception ex)
            {
                LogHelper.GetLogger(typeof(App)).Error("Failed to open settings window", ex);
            }
        }
        public void OnViewLogsClick(object sender, EventArgs e)
        {
            try 
            { 
                string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "snapvox");
                Directory.CreateDirectory(logPath);
                Process.Start(new ProcessStartInfo { FileName = logPath, UseShellExecute = true }); 
            } 
            catch { }
        }
        public void OnExitClick(object sender, EventArgs e) { RetentionHelper.Stop(); _mainAppCts.Cancel(); HotkeyManager.Stop(); _desktop?.Shutdown(); }
    }
}
