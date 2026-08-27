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
                _ = InitializeApplicationAsync(desktop, _mainAppCts.Token);
            }

            base.OnFrameworkInitializationCompleted();
        }

        private async Task InitializeApplicationAsync(IClassicDesktopStyleApplicationLifetime desktop, CancellationToken cancellationToken)
        {
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                LogHelper.GetLogger(typeof(App)).Error("Unobserved Task Exception", e.Exception);
                e.SetObserved();
            };

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
                    SimpleServiceProvider.Current.AddService<IScrollCaptureLauncher>(new ScrollCaptureLauncher());
#if USE_TESSERACT
                    log.Info("Using Tesseract OCR Provider with Windows OCR fallback.");
                    var tesseractProvider = new native.TesseractOcrProvider();
                    ocrProviders.Add(tesseractProvider);
                    ocrProviders.Add(new native.MixedLanguageOcrProvider(tesseractProvider));
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
                        // Boot check: if Windows (Snipping Tool / Snip &amp; Sketch) or another program has taken
                        // the Print Screen key, tell the user right away in very simple English.
                        _ = PrintScreenConflictHelper.NotifyOnBootAsync();
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
                ForceRedTrayIcon(false);
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
            byte[] blueBytes = null;
            byte[] redBytes = null;

            try
            {
                using (var blueAssetLoader = AssetLoader.Open(new Uri("avares://SnapVox/SnapVox.ico")))
                {
                    using var ms = new MemoryStream();
                    blueAssetLoader.CopyTo(ms);
                    blueBytes = ms.ToArray();
                }

                // BUGFIX (red tray eye): the .ico asset used to be handed straight to
                // Avalonia.Media.Imaging.Bitmap, which cannot decode ICO containers. That threw,
                // the catch below swallowed it, _redIcon stayed null, and every red-state request
                // silently fell back to the blue icon - the eye never turned red no matter what
                // the state machine did. The ICO is now decoded manually: the largest sub-image is
                // extracted (embedded PNG or 32bpp DIB) and converted to PNG bytes first.
                byte[] pngBytes = TryDecodeIcoToPng(blueBytes);

                if (pngBytes != null)
                {
                    redBytes = await Task.Run(() =>
                    {
                        using var image = SixLabors.ImageSharp.Image.Load<Bgra32>(pngBytes);
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

                        using var ms = new MemoryStream();
                        image.Save(ms, new PngEncoder());
                        return ms.ToArray();
                    }).ConfigureAwait(true);
                }
                else
                {
                    LogHelper.GetLogger(typeof(App)).Error("Tray red-eye icon unavailable: SnapVox.ico could not be decoded to pixels (falling back to blue-only).");
                }
            }
            catch (Exception ex)
            {
                LogHelper.GetLogger(typeof(App)).Error("Failed to prepare tray icons", ex);
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    WindowIcon blueIcon = null;
                    WindowIcon redIcon = null;

                    if (blueBytes != null)
                    {
                        using var ms = new MemoryStream(blueBytes);
                        blueIcon = new WindowIcon(ms);
                    }

                    if (redBytes != null)
                    {
                        using var ms = new MemoryStream(redBytes);
                        redIcon = new WindowIcon(ms);
                    }

                    var icons = TrayIcon.GetIcons(this);
                    if (icons != null && icons.Count > 0)
                    {
                        _trayIcon = icons[0];
                        _blueIcon = blueIcon;
                        _redIcon = redIcon;
                        var initialIcon = _currentIconIsRed && _redIcon != null ? _redIcon : _blueIcon;
                        if (initialIcon != null) _trayIcon.Icon = initialIcon;
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.GetLogger(typeof(App)).Error("Failed to create tray icon UI objects", ex);
                }
            });
        }

        /// <summary>
        /// Decodes an ICO container into PNG bytes for the largest sub-image it contains.
        /// Supports both PNG-compressed entries and classic 32bpp BMP (DIB) entries.
        /// </summary>
        private static byte[] TryDecodeIcoToPng(byte[] icoBytes)
        {
            try
            {
                if (icoBytes == null || icoBytes.Length < 6) return null;
                int count = BitConverter.ToUInt16(icoBytes, 4);

                int bestOffset = -1, bestLength = 0, bestWidth = 0, bestHeight = 0;
                long bestArea = 0;
                for (int i = 0; i < count; i++)
                {
                    int entry = 6 + i * 16;
                    if (entry + 16 > icoBytes.Length) break;
                    int width = icoBytes[entry] == 0 ? 256 : icoBytes[entry];
                    int height = icoBytes[entry + 1] == 0 ? 256 : icoBytes[entry + 1];
                    int length = BitConverter.ToInt32(icoBytes, entry + 8);
                    int offset = BitConverter.ToInt32(icoBytes, entry + 12);
                    long area = (long)width * height;
                    if (area > bestArea && offset >= 0 && length > 0 && offset + length <= icoBytes.Length)
                    {
                        bestArea = area;
                        bestOffset = offset;
                        bestLength = length;
                        bestWidth = width;
                        bestHeight = height;
                    }
                }

                if (bestOffset < 0) return null;

                byte[] subImage = new byte[bestLength];
                Array.Copy(icoBytes, bestOffset, subImage, 0, bestLength);

                // PNG-compressed entry: usable as-is.
                if (subImage.Length > 8 && subImage[0] == 0x89 && subImage[1] == 0x50 && subImage[2] == 0x4E && subImage[3] == 0x47)
                {
                    return subImage;
                }

                return DibEntryToPng(subImage, bestWidth, bestHeight);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Converts a 32bpp BITMAPINFOHEADER entry (bottom-up BGRA rows) from an ICO into PNG
        /// bytes. Modern Windows icons are 32bpp with a real alpha channel, so the AND mask is
        /// ignored.
        /// </summary>
        private static byte[] DibEntryToPng(byte[] dib, int width, int height)
        {
            if (dib == null || dib.Length < 40 || width <= 0 || height <= 0) return null;
            int headerSize = BitConverter.ToInt32(dib, 0);
            if (headerSize < 40) return null;
            short bitsPerPixel = BitConverter.ToInt16(dib, 14);
            if (bitsPerPixel != 32) return null;

            int stride = ((width * 32 + 31) / 32) * 4;
            int pixelBytes = stride * height;
            if (headerSize + pixelBytes > dib.Length) return null;

            using var image = SixLabors.ImageSharp.Image.LoadPixelData<Bgra32>(
                new ReadOnlySpan<byte>(dib, headerSize, pixelBytes), width, height);
            image.Mutate(x => x.Flip(FlipMode.Vertical)); // DIB rows are stored bottom-up.

            using var ms = new MemoryStream();
            image.Save(ms, new PngEncoder());
            return ms.ToArray();
        }

        private static int _redStateRequestCount = 0;
        private static volatile bool _currentIconIsRed = false;
        private static readonly object _iconLock = new object();

        public static void ForceRedTrayIcon(bool force, string reason = null)
        {
            lock (_iconLock)
            {
                bool wasRed = _redStateRequestCount > 0;
                if (force) _redStateRequestCount++;
                else _redStateRequestCount = Math.Max(0, _redStateRequestCount - 1);
                bool isRed = _redStateRequestCount > 0;

                if (wasRed != isRed)
                {
                    // Trace every red/blue transition so any future drift between capture
                    // components is diagnosable from the log instead of guesswork.
                    LogHelper.GetLogger(typeof(App)).Info(
                        $"Tray eye -> {(isRed ? "RED (capture active)" : "BLUE (idle)")} [holds={_redStateRequestCount}]{(string.IsNullOrEmpty(reason) ? "" : " " + reason)}");
                }

                SetTrayIconStateInternal(isRed);
            }
        }

        public static void SetTrayIconState(bool active)
        {
            lock (_iconLock)
            {
                if (_redStateRequestCount > 0)
                {
                    SetTrayIconStateInternal(true);
                    return;
                }
                SetTrayIconStateInternal(active);
            }
        }

        private static void SetTrayIconStateInternal(bool active)
        {
            _currentIconIsRed = active;

            void ApplyIcon()
            {
                if (_trayIcon == null) return;
                bool currentActive;
                lock (_iconLock)
                {
                    currentActive = _currentIconIsRed;
                }

                var targetIcon = currentActive && _redIcon != null ? _redIcon : _blueIcon;
                if (targetIcon == null) return;
                if (ReferenceEquals(_trayIcon.Icon, targetIcon)) return;
                _trayIcon.Icon = targetIcon;
            }

            if (Dispatcher.UIThread.CheckAccess())
            {
                ApplyIcon();
            }
            else
            {
                Dispatcher.UIThread.Post(ApplyIcon);
            }
        }

        public void OnTrayIconClicked(object sender, EventArgs e) => OnCaptureRegionClick(sender, e);
        public void OnCaptureRegionClick(object sender, EventArgs e) => CaptureHelper.CaptureRegion(false);
        public void OnCaptureWindowClick(object sender, EventArgs e) => CaptureHelper.CaptureActiveWindow(false);
        private void OnCaptureFullscreenClick(object sender, EventArgs e) => CaptureHelper.CaptureFullscreen(false, ScreenCaptureMode.FullScreen);
        private void OnScrollCaptureClick(object sender, EventArgs e)
        {
            var launcher = SimpleServiceProvider.Current.GetInstance<IScrollCaptureLauncher>(true);
            _ = launcher?.StartAsync(null);
        }
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
