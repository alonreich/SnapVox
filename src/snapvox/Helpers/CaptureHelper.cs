using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Input;
using Avalonia.Threading;
using log4net;
using snapvox.native.foundation;
using snapvox.foundation.core;
using snapvox.foundation.Interfaces;
using snapvox.editor.forms;
using snapvox.native;
using snapvox.foundation.IniFile;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using ImageSharpImage = SixLabors.ImageSharp.Image;

namespace snapvox.helpers
{
    public static class CaptureHelper
    {
        private static readonly ILog Log = LogHelper.GetLogger(typeof(CaptureHelper));
        private static readonly object LastRegionSync = new object();
        private static RECT _lastRegion = RECT.Empty;

        private static CoreConfiguration Config => IniConfig.GetIniSection<CoreConfiguration>();

        private static ImageSharpImage _frozenSnapshot;
        private static RECT _frozenVirtualBounds;

        public static void CaptureRegion(bool fromHotkey)
        {
            if (!forms.CaptureWindow.BeginCaptureSession()) return;
            _ = Task.Run(() => CaptureRegionAsync(fromHotkey));
        }

        public static void ClearFrozenSnapshot()
        {
            lock (LastRegionSync)
            {
                _frozenSnapshot?.Dispose();
                _frozenSnapshot = null;
            }
        }

        public static ImageSharpImage GetFrozenSnapshot(RECT target)
        {
            lock (LastRegionSync)
            {
                if (_frozenSnapshot == null) return null;
                
                var cropRect = ClampCropRectangle(new Rectangle(target.Left - _frozenVirtualBounds.Left, target.Top - _frozenVirtualBounds.Top, target.Width, target.Height), _frozenSnapshot.Width, _frozenSnapshot.Height);
                if (cropRect.Width <= 0 || cropRect.Height <= 0) return null;
                
                return _frozenSnapshot.Clone(x => x.Crop(cropRect));
            }
        }

        private static async Task CaptureRegionAsync(bool fromHotkey)
        {
            bool overlaysShown = false;
            try
            {
                RECT virtualBounds = GetVirtualDesktopBounds();
                ImageSharpImage fullSnapshot = NativeCapture.CaptureRegion(virtualBounds, Config.CaptureMousepointer);
                if (fullSnapshot == null)
                {
                    forms.CaptureWindow.EndCaptureSession();
                    return;
                }

                if (Config.CaptureDelay > 0) await Task.Delay(Config.CaptureDelay).ConfigureAwait(false);

                lock (LastRegionSync)
                {
                    _frozenSnapshot?.Dispose();
                    _frozenSnapshot = fullSnapshot;
                    _frozenVirtualBounds = virtualBounds;
                }

                var screensInfo = await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var lifetime = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
                    var screens = lifetime?.Windows.FirstOrDefault()?.Screens.All;
                    if (screens == null || !screens.Any())
                    {
                        var probe = new Avalonia.Controls.Window();
                        screens = probe.Screens.All;
                    }
                    return screens?.Select(s => new { s.Bounds, s.Scaling }).ToList();
                });

                if (screensInfo == null || !screensInfo.Any()) 
                {
                    forms.CaptureWindow.EndCaptureSession();
                    ClearFrozenSnapshot();
                    return;
                }

                var screenData = new List<(PixelRect Bounds, byte[] PngData)>();
                foreach (var screen in screensInfo)
                {
                    var cropRect = ClampCropRectangle(new Rectangle(screen.Bounds.X - virtualBounds.Left, screen.Bounds.Y - virtualBounds.Top, screen.Bounds.Width, screen.Bounds.Height), fullSnapshot.Width, fullSnapshot.Height);
                    if (cropRect.Width <= 0 || cropRect.Height <= 0) continue;

                    using var cropped = fullSnapshot.Clone(x => x.Crop(cropRect));
                    if (Config.AddFrameBorders) cropped.Mutate(x => { int t = 3; if (cropped.Width > t * 2 && cropped.Height > t * 2) x.Crop(new Rectangle(t, t, cropped.Width - t * 2, cropped.Height - t * 2)).Pad(cropped.Width, cropped.Height, SixLabors.ImageSharp.Color.FromRgb(0, 0, 128)); });
                    using var ms = new MemoryStream();
                    cropped.Save(ms, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
                    screenData.Add((screen.Bounds, ms.ToArray()));
                }

                if (screenData.Count == 0)
                {
                    forms.CaptureWindow.EndCaptureSession();
                    ClearFrozenSnapshot();
                    return;
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    foreach (var data in screenData)
                    {
                        Avalonia.Media.Imaging.Bitmap bitmap = null;
                        forms.CaptureWindow window = null;
                        using (var ms = new MemoryStream(data.PngData)) bitmap = new Avalonia.Media.Imaging.Bitmap(ms);
                        try
                        {
                            window = new forms.CaptureWindow(data.Bounds, bitmap);
                            bitmap = null;
                            window.Show();
                        }
                        catch
                        {
                            bitmap?.Dispose();
                            window?.Close();
                            throw;
                        }
                    }
                    overlaysShown = true;
                });
            }
            catch (Exception ex) 
            { 
                Log.Fatal("CaptureRegion failed.", ex);
                if (!overlaysShown) forms.CaptureWindow.EndCaptureSession();
                ClearFrozenSnapshot();
            }
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
        private const uint GW_HWNDNEXT = 2;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetTopWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        public static void CaptureWindow(bool fromHotkey) => CaptureActiveWindow(fromHotkey);

        public static void CaptureActiveWindow(bool fromHotkey)
        {
            App.ForceRedTrayIcon(true);
            _ = Task.Run(async () =>
            {
                ImageSharpImage fullSnapshot = null;
                ImageSharpImage owned = null;
                bool editorShown = false;
                try
                {
                    RECT virtualBounds = GetVirtualDesktopBounds();
                    fullSnapshot = NativeCapture.CaptureRegion(virtualBounds, Config.CaptureMousepointer);

                    int delay = Config.CaptureDelay > 0 ? Config.CaptureDelay : (fromHotkey ? 0 : 400);
                    if (delay > 0) await Task.Delay(delay).ConfigureAwait(false);

                    IntPtr activeHwnd = Win32WindowHelper.GetForegroundWindow();
                    if (activeHwnd != IntPtr.Zero)
                    {
                        var sb = new System.Text.StringBuilder(256);
                        GetClassName(activeHwnd, sb, sb.Capacity);
                        string className = sb.ToString();
                        if (className == "Shell_TrayWnd" || className == "NotifyIconOverflowWindow" || className == "TrayNotifyWnd" || className == "PopupHost" || className == "WorkerW" || className == "Progman")
                        {
                            IntPtr top = GetTopWindow(IntPtr.Zero);
                            uint currentPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                            int skipped = 0;
                            while (top != IntPtr.Zero && skipped < 100)
                            {
                                if (IsWindowVisible(top))
                                {
                                    var sb2 = new System.Text.StringBuilder(256);
                                    GetClassName(top, sb2, sb2.Capacity);
                                    string cls = sb2.ToString();
                                    GetWindowThreadProcessId(top, out uint pid);
                                    bool isSystem = cls == "Shell_TrayWnd" || cls == "WorkerW" || cls == "Progman" || cls == "NotifyIconOverflowWindow" || cls == "PopupHost" || cls == "EdgeUiInputTopWnd" || cls == "DummyDWMTargetWindow" || cls == "ThumbnailDeviceHelperWnd" || cls.Contains("Flyout") || cls == "Windows.UI.Core.CoreWindow" || cls == "ApplicationFrameWindow";
                                    if (pid != currentPid && !isSystem)
                                    {
                                        if (Win32WindowHelper.GetWindowRectActual(top, out RECT tr) && !tr.IsEmpty && tr.Width > 150 && tr.Height > 150) { activeHwnd = top; break; }
                                    }
                                }
                                top = GetWindow(top, GW_HWNDNEXT);
                                skipped++;
                            }
                        }
                    }

                    if (activeHwnd != IntPtr.Zero && fullSnapshot != null)
                    {
                        if (Win32WindowHelper.GetWindowRect(activeHwnd, out RECT rawRect) && !rawRect.IsEmpty)
                        {
                            if (!fromHotkey && Win32WindowHelper.GetWindowRectActual(activeHwnd, out RECT dwmRect))
                            {
                                if (dwmRect.Width > rawRect.Width && dwmRect.Height > rawRect.Height) rawRect = dwmRect;
                            }

                            var cropRect = ClampCropRectangle(new Rectangle(rawRect.Left - virtualBounds.Left, rawRect.Top - virtualBounds.Top, rawRect.Width, rawRect.Height), fullSnapshot.Width, fullSnapshot.Height);
                            if (cropRect.Width > 0 && cropRect.Height > 0)
                            {
                                owned = fullSnapshot.Clone(x => x.Crop(cropRect));

                                if (Config.KeepBackup)
                                {
                                    try
                                    {
                                        string tempDir = Path.Combine(Path.GetTempPath(), "SnapVox");
                                        Directory.CreateDirectory(tempDir);
                                        string fileName = $"Raw_{DateTime.Now:yyyy-MM-dd_HH-mm-ss_fff}.jpg";
                                        owned.Save(Path.Combine(tempDir, fileName), new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = snapvox.foundation.IniFile.IniConfig.GetIniSection<CoreConfiguration>().OutputFileJpegQuality });
                                    }
                                    catch (Exception ex)
                                    {
                                        Log.Error("[TEMP_SAVE_FAILURE] Failed to save raw capture.", ex);
                                    }
                                }

                                if (Config.AddFrameBorders) owned.Mutate(x => { int t = 3; if (owned.Width > t * 2 && owned.Height > t * 2) x.Crop(new Rectangle(t, t, owned.Width - t * 2, owned.Height - t * 2)).Pad(owned.Width, owned.Height, SixLabors.ImageSharp.Color.FromRgb(0, 0, 128)); });

                                RememberRegion(rawRect);
                                await UiClipboard.SetImageAsync(owned).ConfigureAwait(false);
                                ImageSharpImage imageForEditor = owned;
                                await Dispatcher.UIThread.InvokeAsync(() => ShowEditorForOwnedImage(imageForEditor, rawRect, "region"));
                                owned = null;
                                editorShown = true;
                            }
                        }
                    }
                }
                catch (Exception ex) { Log.Fatal("CaptureActiveWindow failed.", ex); }
                finally
                {
                    owned?.Dispose();
                    fullSnapshot?.Dispose();
                    if (!editorShown) App.ForceRedTrayIcon(false);
                }
            });
        }

        public static void CaptureFullscreen(bool fromHotkey, ScreenCaptureMode mode)
        {
            App.ForceRedTrayIcon(true);
            _ = Task.Run(async () =>
            {
                ImageSharpImage owned = null;
                bool editorShown = false;
                try
                {
                    RECT virtualBounds = GetVirtualDesktopBounds();
                    using var fullSnapshot = NativeCapture.CaptureRegion(virtualBounds, Config.CaptureMousepointer);

                    if (Config.CaptureDelay > 0) await Task.Delay(Config.CaptureDelay).ConfigureAwait(false);

                    if (fullSnapshot != null)
                    {
                        owned = fullSnapshot.Clone(x => { });
                        if (Config.KeepBackup)
                        {
                            try
                            {
                                string tempDir = Path.Combine(Path.GetTempPath(), "SnapVox");
                                Directory.CreateDirectory(tempDir);
                                string fileName = $"Raw_{DateTime.Now:yyyy-MM-dd_HH-mm-ss_fff}.jpg";
                                owned.Save(Path.Combine(tempDir, fileName), new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = snapvox.foundation.IniFile.IniConfig.GetIniSection<CoreConfiguration>().OutputFileJpegQuality });
                            }
                            catch (Exception ex)
                            {
                                Log.Error("[TEMP_SAVE_FAILURE] Failed to save raw capture.", ex);
                            }
                        }

                        if (Config.AddFrameBorders) owned.Mutate(x => { int t = 3; if (owned.Width > t * 2 && owned.Height > t * 2) x.Crop(new Rectangle(t, t, owned.Width - t * 2, owned.Height - t * 2)).Pad(owned.Width, owned.Height, SixLabors.ImageSharp.Color.FromRgb(0, 0, 128)); });

                        await UiClipboard.SetImageAsync(owned).ConfigureAwait(false);
                        ImageSharpImage imageForEditor = owned;
                        await Dispatcher.UIThread.InvokeAsync(() => ShowEditorForOwnedImage(imageForEditor, virtualBounds, "region"));
                        owned = null;
                        editorShown = true;
                    }
                }
                catch (Exception ex) { Log.Fatal("CaptureFullscreen failed.", ex); }
                finally
                {
                    owned?.Dispose();
                    if (!editorShown) App.ForceRedTrayIcon(false);
                }
            });
        }

        public static void CaptureClipboard()
        {
            App.ForceRedTrayIcon(true);
            _ = Dispatcher.UIThread.InvokeAsync(async () =>
            {
                try
                {
                    ImageSharpImage image = await UiClipboard.GetImageAsync();
                    if (image != null)
                    {
                        ShowEditorForOwnedImage(image, RECT.Empty, "clipboard");
                    }
                    else
                    {
                        App.ForceRedTrayIcon(false);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("CaptureClipboard failed", ex);
                    App.ForceRedTrayIcon(false);
                }
            });
        }

        public static void RememberRegion(RECT region)
        {
            if (!region.IsEmpty && region.Width > 0 && region.Height > 0)
            {
                lock (LastRegionSync) _lastRegion = region;
            }
        }

        public static void CaptureLastRegion(bool fromHotkey)
        {
            App.ForceRedTrayIcon(true);
            RECT lastRegion;
            lock (LastRegionSync) lastRegion = _lastRegion;
            if (lastRegion.IsEmpty || lastRegion.Width <= 0 || lastRegion.Height <= 0)
            {
                App.ForceRedTrayIcon(false);
                return;
            }
            OpenEditorForRegionAsync(lastRegion);
        }

        private const int SmXVirtualScreen = 76;
        private const int SmYVirtualScreen = 77;
        private const int SmCXVirtualScreen = 78;
        private const int SmCYVirtualScreen = 79;

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        public static RECT GetVirtualDesktopBounds()
        {
            return RECT.FromXYWH(
                GetSystemMetrics(SmXVirtualScreen),
                GetSystemMetrics(SmYVirtualScreen),
                GetSystemMetrics(SmCXVirtualScreen),
                GetSystemMetrics(SmCYVirtualScreen));
        }

        private static void OpenEditorForRegionAsync(RECT region)
        {
            ImageSharpImage owned = null;
            _ = Task.Run(async () =>
            {
                bool editorShown = false;
                try
                {
                    using (ImageSharpImage captured = NativeCapture.CaptureRegion(region, Config.CaptureMousepointer))
                    {
                        if (captured == null)
                        {
                            App.ForceRedTrayIcon(false);
                            return;
                        }
                        owned = captured.Clone(x => { });

                        if (Config.KeepBackup)
                        {
                            try
                            {
                                string tempDir = Path.Combine(Path.GetTempPath(), "SnapVox");
                                Directory.CreateDirectory(tempDir);
                                string fileName = $"Raw_{DateTime.Now:yyyy-MM-dd_HH-mm-ss_fff}.jpg";
                                owned.Save(Path.Combine(tempDir, fileName), new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = snapvox.foundation.IniFile.IniConfig.GetIniSection<CoreConfiguration>().OutputFileJpegQuality });
                            }
                            catch (Exception ex)
                            {
                                Log.Error("[TEMP_SAVE_FAILURE] Failed to save raw capture.", ex);
                            }
                        }

                        if (Config.AddFrameBorders) owned.Mutate(x => { int t = 2; if (owned.Width > t * 2 && owned.Height > t * 2) x.Crop(new Rectangle(t, t, owned.Width - t * 2, owned.Height - t * 2)).Pad(owned.Width, owned.Height, SixLabors.ImageSharp.Color.FromRgb(0, 0, 128)); });
                    }

                    if (owned == null)
                    {
                        App.ForceRedTrayIcon(false);
                        return;
                    }
                    RememberRegion(region);
                    await UiClipboard.SetImageAsync(owned).ConfigureAwait(false);
                    ImageSharpImage imageForEditor = owned;
                    await Dispatcher.UIThread.InvokeAsync(() => ShowEditorForOwnedImage(imageForEditor, region, "region"));
                    owned = null;
                    editorShown = true;
                }
                catch (Exception ex)
                {
                    Log.Fatal("OpenEditorForRegion failed.", ex);
                }
                finally
                {
                    owned?.Dispose();
                    if (!editorShown) App.ForceRedTrayIcon(false);
                }
            });
        }

        public static void OpenEditorForOwnedImage(ImageSharpImage image, RECT region)
        {
            ShowEditorForOwnedImage(image, region, "scroll");
        }

        private static void ShowEditorForOwnedImage(ImageSharpImage image, RECT region, string context)
        {
            ImageEditorWindow editor = null;
            try
            {
                editor = new ImageEditorWindow();
                editor.SetImage(image, region);
                editor.Show();
                App.ForceRedTrayIcon(false);
            }
            catch (Exception ex)
            {
                App.ForceRedTrayIcon(false);
                image?.Dispose();
                editor?.Close();
                Log.Fatal("ShowEditorForOwnedImage failed.", ex);
            }
        }

        private static Rectangle ClampCropRectangle(Rectangle rectangle, int imageWidth, int imageHeight)
        {
            int x = Math.Max(0, rectangle.X);
            int y = Math.Max(0, rectangle.Y);
            int right = Math.Min(imageWidth, rectangle.Right);
            int bottom = Math.Min(imageHeight, rectangle.Bottom);
            return new Rectangle(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
        }
    }
}
