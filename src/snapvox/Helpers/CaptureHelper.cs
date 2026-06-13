/*
 * Portions of this file, specifically the native capture logic and 
 * bounds calculations, were adapted from the Greenshot project, 
 * which is licensed under the GNU General Public License (GPL).
 * SnapVox acknowledges and complies with this license.
 */
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
            App.ForceRedTrayIcon(true);
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
            try
            {
                RECT virtualBounds = GetVirtualDesktopBounds();
                // Freeze the snapshot as early as possible
                ImageSharpImage fullSnapshot = NativeCapture.CaptureRegion(virtualBounds, Config.CaptureMousepointer);
                if (fullSnapshot == null) return;

                if (Config.CaptureDelay > 0) await Task.Delay(Config.CaptureDelay).ConfigureAwait(false);

                lock (LastRegionSync)
                {
                    _frozenSnapshot?.Dispose();
                    _frozenSnapshot = fullSnapshot;
                    _frozenVirtualBounds = virtualBounds;
                }

                var screens = await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var lifetime = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
                    var screens = lifetime?.Windows.FirstOrDefault()?.Screens.All;
                    if (screens == null || !screens.Any())
                    {
                        var probe = new Avalonia.Controls.Window();
                        screens = probe.Screens.All;
                    }
                    return screens?.ToList();
                });

                if (screens == null || !screens.Any()) 
                {
                    ClearFrozenSnapshot();
                    return;
                }

                var screenData = new List<(PixelRect Bounds, byte[] PngData)>();
                foreach (var screen in screens)
                {
                    var cropRect = ClampCropRectangle(new Rectangle(screen.Bounds.X - virtualBounds.Left, screen.Bounds.Y - virtualBounds.Top, screen.Bounds.Width, screen.Bounds.Height), fullSnapshot.Width, fullSnapshot.Height);
                    if (cropRect.Width <= 0 || cropRect.Height <= 0) continue;

                    using var cropped = fullSnapshot.Clone(x => x.Crop(cropRect));
                    if (Config.AddFrameBorders) cropped.Mutate(x => { int t = 3; if (cropped.Width > t * 2 && cropped.Height > t * 2) x.Crop(new Rectangle(t, t, cropped.Width - t * 2, cropped.Height - t * 2)).Pad(cropped.Width, cropped.Height, SixLabors.ImageSharp.Color.Black); });
                    using var ms = new MemoryStream();
                    cropped.Save(ms, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
                    screenData.Add((screen.Bounds, ms.ToArray()));
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
                });
            }
            catch (Exception ex) 
            { 
                Log.Fatal("CaptureRegion failed.", ex);
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
            _ = App.FlickerTrayIcon();
            
            RECT virtualBounds = GetVirtualDesktopBounds();
            var fullSnapshot = NativeCapture.CaptureRegion(virtualBounds, Config.CaptureMousepointer);

            Task.Run(async () =>
            {
                try
                {
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
                                var owned = fullSnapshot.Clone(x => x.Crop(cropRect));
                                
                                // Mandate: Every capture saved to %TMP%\SnapVox immediately (raw)
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

                                if (Config.AddFrameBorders) owned.Mutate(x => { int t = 3; if (owned.Width > t * 2 && owned.Height > t * 2) x.Crop(new Rectangle(t, t, owned.Width - t * 2, owned.Height - t * 2)).Pad(owned.Width, owned.Height, SixLabors.ImageSharp.Color.Black); });
                                
                                RememberRegion(rawRect);
                                await UiClipboard.SetImageAsync(owned).ConfigureAwait(false);
                                await Dispatcher.UIThread.InvokeAsync(() =>
                                {
                                    ShowEditorForOwnedImage(owned, rawRect, "region");
                                });
                            }
                        }
                    }
                }
                catch (Exception ex) { Log.Fatal("CaptureActiveWindow failed.", ex); }
                finally { fullSnapshot?.Dispose(); }
            });
        }

        public static void CaptureFullscreen(bool fromHotkey, ScreenCaptureMode mode)
        {
            _ = App.FlickerTrayIcon();
            Task.Run(async () =>
            {
                RECT virtualBounds = GetVirtualDesktopBounds();
                using var fullSnapshot = NativeCapture.CaptureRegion(virtualBounds, Config.CaptureMousepointer);

                if (Config.CaptureDelay > 0) await Task.Delay(Config.CaptureDelay).ConfigureAwait(false);
                
                if (fullSnapshot != null)
                {
                    var owned = fullSnapshot.Clone(x => { });
                    // Mandate: Every capture saved to %TMP%\SnapVox immediately (raw)
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

                    if (Config.AddFrameBorders) owned.Mutate(x => { int t = 3; if (owned.Width > t * 2 && owned.Height > t * 2) x.Crop(new Rectangle(t, t, owned.Width - t * 2, owned.Height - t * 2)).Pad(owned.Width, owned.Height, SixLabors.ImageSharp.Color.Black); });

                    await UiClipboard.SetImageAsync(owned).ConfigureAwait(false);
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        ShowEditorForOwnedImage(owned, virtualBounds, "region");
                    });
                }
            });
        }

        public static void CaptureClipboard()
        {
            Task.Run(async () =>
            {
                try
                {
                    ImageSharpImage image = await UiClipboard.GetImageAsync();
                    if (image != null)
                    {
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            ShowEditorForOwnedImage(image, RECT.Empty, "clipboard");
                        });
                    }
                }
                catch (Exception ex) { Log.Error("CaptureClipboard failed", ex); }
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
            RECT lastRegion;
            lock (LastRegionSync) lastRegion = _lastRegion;
            if (lastRegion.IsEmpty || lastRegion.Width <= 0 || lastRegion.Height <= 0) return;
            _ = OpenEditorForRegionAsync(lastRegion);
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

        private static async Task OpenEditorForRegionAsync(RECT region)
        {
            ImageSharpImage owned = null;
            try
            {
                owned = await Task.Run(() =>
                {
                    using ImageSharpImage captured = NativeCapture.CaptureRegion(region, Config.CaptureMousepointer);
                    if (captured == null) return null;
                    var clone = captured.Clone(x => { });

                    // Mandate: Every capture saved to %TMP%\SnapVox immediately (raw)
                    try
                    {
                        string tempDir = Path.Combine(Path.GetTempPath(), "SnapVox");
                        Directory.CreateDirectory(tempDir);
                        string fileName = $"Raw_{DateTime.Now:yyyy-MM-dd_HH-mm-ss_fff}.jpg";
                        clone.Save(Path.Combine(tempDir, fileName), new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = snapvox.foundation.IniFile.IniConfig.GetIniSection<CoreConfiguration>().OutputFileJpegQuality });
                    }
                    catch (Exception ex)
                    {
                        Log.Error("[TEMP_SAVE_FAILURE] Failed to save raw capture.", ex);
                    }

                    if (Config.AddFrameBorders) clone.Mutate(x => { int t = 2; if (clone.Width > t * 2 && clone.Height > t * 2) x.Crop(new Rectangle(t, t, clone.Width - t * 2, clone.Height - t * 2)).Pad(clone.Width, clone.Height, SixLabors.ImageSharp.Color.Black); });
                    return clone;
                }).ConfigureAwait(false);

                if (owned == null) return;
                RememberRegion(region);
                await UiClipboard.SetImageAsync(owned).ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ShowEditorForOwnedImage(owned, region, "region");
                });
            }
            catch (Exception ex) { owned?.Dispose(); Log.Fatal("OpenEditorForRegion failed.", ex); }
        }

        private static void ShowEditorForOwnedImage(ImageSharpImage image, RECT region, string context)
        {
            ImageEditorWindow editor = null;
            try
            {
                editor = new ImageEditorWindow();
                editor.SetImage(image, region);
                editor.Show();
            }
            catch (Exception ex)
            {
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
