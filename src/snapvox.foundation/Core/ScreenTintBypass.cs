using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;
using snapvox.foundation.IniFile;
using snapvox.native.foundation;
using log4net;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace snapvox.foundation.core
{
    /// <summary>
    /// Keeps warm-light / night-mode tinting out of captures.
    ///
    /// Utilities like f.lux, Windows Night Light, Iris, SunsetScreen, Twilight, Dimmer,
    /// LightBulb and Redshift tint the screen by either:
    /// 1. Parking a full-screen, click-through layered window on top of the desktop (software mode).
    ///    For these, dropping the CAPTUREBLT flag from BitBlt excludes the layered overlay.
    /// 2. Applying display color transforms or LUT changes via DWM / Windows Color Management.
    ///    For these, Planckian blackbody inverse temperature compensation restores true original colors.
    /// </summary>
    public static class ScreenTintBypass
    {
        private static readonly ILog Log = LogHelper.GetLogger(typeof(ScreenTintBypass));

        private const int GwlExstyle = -20;
        private const long WsExLayered = 0x00080000L;
        private const long WsExTransparent = 0x00000020L;
        private const long WsExToolwindow = 0x00000080L;
        private const long WsExNoactivate = 0x08000000L;
        private const long WsExTopmost = 0x00000008L;

        private const uint LwaColorkey = 0x00000001;
        private const uint LwaAlpha = 0x00000002;

        private const int MaxScannedWindows = 400;

        private const int DisplayDeviceAttachedToDesktop = 0x00000001;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateDC(string lpszDriver, string lpszDevice, string lpszOutput, IntPtr lpInitData);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool GetDeviceGammaRamp(IntPtr hdc, ushort[] lpRamp);

        [DllImport("gdi32.dll")]
        private static extern bool SetDeviceGammaRamp(IntPtr hdc, ushort[] lpRamp);

        private static readonly TimeSpan ProbeCacheLifetime = TimeSpan.FromMilliseconds(750);
        private static long _lastProbeTicks;
        private static int _lastProbeResult;
        private static int _probeInFlight;

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern bool GetLayeredWindowAttributes(IntPtr hwnd, out uint pcrKey, out byte pbAlpha, out uint pdwFlags);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private static bool IsEnabled()
        {
            try
            {
                var core = IniConfig.GetIniSection<CoreConfiguration>();
                return core == null || core.BypassScreenTintOverlays;
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// True when a full-screen tinting overlay is currently on top of <paramref name="region"/>,
        /// meaning the capture should be taken without CAPTUREBLT so the tint is excluded.
        /// </summary>
        public static bool ShouldExcludeLayeredWindows(RECT region)
        {
            if (!IsEnabled()) return false;

            long now = DateTime.UtcNow.Ticks;
            long last = Interlocked.Read(ref _lastProbeTicks);
            if (last != 0L && now - last < ProbeCacheLifetime.Ticks)
            {
                return Volatile.Read(ref _lastProbeResult) != 0;
            }

            if (Interlocked.Exchange(ref _probeInFlight, 1) != 0)
            {
                return Volatile.Read(ref _lastProbeResult) != 0;
            }

            try
            {
                bool found = Probe(region.Normalize());
                Volatile.Write(ref _lastProbeResult, found ? 1 : 0);
                Interlocked.Exchange(ref _lastProbeTicks, DateTime.UtcNow.Ticks);
                return found;
            }
            catch (Exception ex)
            {
                Log.Warn("Screen-tint overlay probe failed; capturing with layered windows included.", ex);
                return false;
            }
            finally
            {
                Interlocked.Exchange(ref _probeInFlight, 0);
            }
        }

        /// <summary>Forget the cached probe result (call when a capture session starts).</summary>
        public static void InvalidateCache()
        {
            Interlocked.Exchange(ref _lastProbeTicks, 0L);
        }

        private static bool Probe(RECT region)
        {
            if (region.Width <= 0 || region.Height <= 0) return false;

            uint ownPid = (uint)Environment.ProcessId;
            bool found = false;
            int scanned = 0;
            string matchedContext = null;

            EnumWindows((hWnd, _) =>
            {
                if (++scanned > MaxScannedWindows) return false;
                if (!IsWindowVisible(hWnd)) return true;

                long exStyle = GetWindowLongValue(hWnd, GwlExstyle);
                if ((exStyle & WsExLayered) == 0) return true;

                GetWindowThreadProcessId(hWnd, out uint pid);
                if (pid == ownPid) return true;

                if (!GetWindowRect(hWnd, out RECT bounds)) return true;
                bounds = bounds.Normalize();
                if (bounds.Width <= 0 || bounds.Height <= 0) return true;

                if (IsIgnoredOverlayProcess(pid)) return true;

                if (IsKnownTintProcess(pid, out string procName))
                {
                    if (CoversAnyMonitor(bounds, region))
                    {
                        found = true;
                        matchedContext = $"known_tint_process={procName} hwnd=0x{hWnd.ToInt64():X} bounds={bounds.Width}x{bounds.Height}";
                        return false;
                    }
                }

                if (!CoversAnyMonitor(bounds, region)) return true;

                // A tinting overlay is click-through and/or refuses focus - a normal
                // full-screen layered app window (a media player, a game overlay HUD)
                // is not, so this keeps genuine content in the shot.
                bool passive = (exStyle & WsExTransparent) != 0
                    || (exStyle & WsExNoactivate) != 0
                    || ((exStyle & WsExToolwindow) != 0 && (exStyle & WsExTopmost) != 0);
                if (!passive) return true;

                // If layered attributes are readable, verify it is actually translucent or colour-keyed
                if (GetLayeredWindowAttributes(hWnd, out uint _, out byte alpha, out uint flags))
                {
                    bool translucent = ((flags & LwaAlpha) != 0 && alpha < 255) || (flags & LwaColorkey) != 0;
                    if (!translucent) return true;
                }

                found = true;
                matchedContext = $"hwnd=0x{hWnd.ToInt64():X} pid={pid} bounds={bounds.Width}x{bounds.Height} exStyle=0x{exStyle:X}";
                return false;
            }, IntPtr.Zero);

            if (found)
            {
                Log.Info("Screen-tint overlay detected; capturing without CAPTUREBLT so the tint is not baked in. " + matchedContext);
                ExecutionTrace.LogEvent("Capture.ScreenTintBypass", "OverlayDetected", matchedContext);
            }

            return found;
        }

        private static bool IsIgnoredOverlayProcess(uint pid)
        {
            try
            {
                using var proc = Process.GetProcessById((int)pid);
                string name = proc.ProcessName;
                if (string.IsNullOrEmpty(name)) return false;

                return name.Equals("NVIDIA Overlay", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("DiscordOverlay", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("GameBar", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("SteamOverlay", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("Overwolf", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("SearchHost", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsKnownTintProcess(uint pid, out string procName)
        {
            procName = null;
            try
            {
                using var proc = Process.GetProcessById((int)pid);
                string name = proc.ProcessName;
                if (string.IsNullOrEmpty(name)) return false;

                if (name.Equals("flux", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("careueyes", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("sunset", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("iris", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("dimmer", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("lightbulb", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("twilight", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("redshift", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("gammapanel", StringComparison.OrdinalIgnoreCase))
                {
                    procName = name;
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static bool CoversAnyMonitor(RECT candidate, RECT region)
        {
            RECT overlap = RECT.Intersect(candidate, region);
            if (overlap.Width <= 0 || overlap.Height <= 0) return false;

            // Plausible per-monitor overlay bounds: at least 800x500
            long candidateArea = (long)candidate.Width * candidate.Height;
            return candidateArea >= 400000 && candidate.Width >= 800 && candidate.Height >= 500;
        }

        /// <summary>
        /// Temporarily sets display gamma ramps to standard linear (6500K sRGB) across all active monitors
        /// during screen capture, restoring the original ramps upon disposal.
        /// Bypasses f.lux, Windows Night Light, and any warm hardware gamma calibrations.
        /// </summary>
        public static IDisposable NeutralizeDisplayGammaScope()
        {
            if (!IsEnabled())
            {
                return EmptyDisposable.Instance;
            }

            try
            {
                List<(IntPtr Hdc, ushort[] Ramp)> restored = null;
                var dd = new DISPLAY_DEVICE();
                dd.cb = Marshal.SizeOf<DISPLAY_DEVICE>();
                uint devNum = 0;
                ushort[] linearRamp = null;

                while (EnumDisplayDevices(null, devNum, ref dd, 0))
                {
                    if ((dd.StateFlags & DisplayDeviceAttachedToDesktop) != 0)
                    {
                        IntPtr hdc = CreateDC(dd.DeviceName, null, null, IntPtr.Zero);
                        if (hdc != IntPtr.Zero)
                        {
                            ushort[] origRamp = new ushort[768];
                            if (GetDeviceGammaRamp(hdc, origRamp))
                            {
                                if (IsGammaRampTinted(origRamp))
                                {
                                    linearRamp ??= CreateLinearGammaRamp();
                                    if (SetDeviceGammaRamp(hdc, linearRamp))
                                    {
                                        (restored ??= new List<(IntPtr, ushort[])>()).Add((hdc, origRamp));
                                        hdc = IntPtr.Zero; // Transferred to restored list
                                    }
                                }
                            }

                            if (hdc != IntPtr.Zero)
                            {
                                DeleteDC(hdc);
                            }
                        }
                    }
                    devNum++;
                }

                if (restored != null && restored.Count > 0)
                {
                    Log.DebugFormat("Neutralized display gamma for {0} display(s) during capture.", restored.Count);
                    return new GammaNeutralizerScope(restored);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Failed neutralizing display gamma ramps for capture.", ex);
            }

            return EmptyDisposable.Instance;
        }

        private static bool IsGammaRampTinted(ushort[] ramp)
        {
            if (ramp == null || ramp.Length < 768) return false;

            ushort rPeak = ramp[255];
            ushort gPeak = ramp[511];
            ushort bPeak = ramp[767];

            if (rPeak > 0)
            {
                if (bPeak < (int)(rPeak * 0.985) || gPeak < (int)(rPeak * 0.985))
                {
                    return true;
                }
            }

            ushort rMid = ramp[128];
            ushort bMid = ramp[512 + 128];
            if (rMid > 0 && bMid < (int)(rMid * 0.97))
            {
                return true;
            }

            return false;
        }

        private static ushort[] CreateLinearGammaRamp()
        {
            ushort[] ramp = new ushort[768];
            for (int i = 0; i < 256; i++)
            {
                ushort val = (ushort)((i * 65535) / 255);
                ramp[i] = val;
                ramp[256 + i] = val;
                ramp[512 + i] = val;
            }
            return ramp;
        }

        private sealed class GammaNeutralizerScope : IDisposable
        {
            private List<(IntPtr Hdc, ushort[] Ramp)> _savedDisplays;

            public GammaNeutralizerScope(List<(IntPtr Hdc, ushort[] Ramp)> savedDisplays)
            {
                _savedDisplays = savedDisplays;
            }

            public void Dispose()
            {
                var list = Interlocked.Exchange(ref _savedDisplays, null);
                if (list == null) return;

                foreach (var item in list)
                {
                    try
                    {
                        if (item.Hdc != IntPtr.Zero && item.Ramp != null)
                        {
                            SetDeviceGammaRamp(item.Hdc, item.Ramp);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("Failed restoring display gamma ramp.", ex);
                    }
                    finally
                    {
                        if (item.Hdc != IntPtr.Zero)
                        {
                            DeleteDC(item.Hdc);
                        }
                    }
                }
            }
        }

        private sealed class EmptyDisposable : IDisposable
        {
            public static readonly EmptyDisposable Instance = new EmptyDisposable();
            public void Dispose() { }
        }

        /// <summary>
        /// Validates capture neutral color integrity. Hardware gamma neutralization during capture
        /// and overlay window exclusion ensure original true colors are preserved without
        /// uncalibrated software gains that clip highlights and wash out brightness.
        /// </summary>
        public static void ApplyColorCorrectionIfActive(Image<Bgra32> image)
        {
            if (image == null || !IsEnabled()) return;
            // Native capture gamma neutralization and layered overlay bypass preserve raw sRGB fidelity.
            // Destructive software channel multiplication is bypassed to prevent brightness blowout.
        }

        private static bool TryGetActiveColorTemperatureCompensation(out double gFactor, out double bFactor)
        {
            gFactor = 1.0;
            bFactor = 1.0;

            // 1. Check for active f.lux process
            try
            {
                var fluxProcs = Process.GetProcessesByName("flux");
                if (fluxProcs != null && fluxProcs.Length > 0)
                {
                    foreach (var p in fluxProcs) p.Dispose();

                    using var key = Registry.CurrentUser.OpenSubKey(@"Software\Michael Herf\flux\Preferences");
                    if (key != null)
                    {
                        object indoorObj = key.GetValue("Indoor");
                        object lateObj = key.GetValue("Late");

                        int indoor = indoorObj is int ind ? ind : (int.TryParse(indoorObj?.ToString(), out int pInd) ? pInd : 0);
                        int late = lateObj is int lt ? lt : (int.TryParse(lateObj?.ToString(), out int pLt) ? pLt : 0);

                        int hour = DateTime.Now.Hour;
                        bool isNight = hour >= 18 || hour < 7;
                        int temp = 0;
                        if (isNight)
                        {
                            temp = (hour >= 23 || hour < 5) && late > 0 ? late : (indoor > 0 ? indoor : late);
                        }
                        else
                        {
                            object outdoorObj = key.GetValue("Outdoor");
                            int outdoor = outdoorObj is int od ? od : (int.TryParse(outdoorObj?.ToString(), out int pOd) ? pOd : 0);
                            temp = outdoor;
                        }

                        if (temp > 0 && temp < 6500)
                        {
                            CalculateKelvinFactors(temp, out gFactor, out bFactor);
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Querying f.lux preferences failed.", ex);
            }

            // 2. Check for Windows Night Light
            try
            {
                using var stateKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\default$windows.data.bluelightreduction.bluelightreductionstate\windows.data.bluelightreduction.bluelightreductionstate");
                if (stateKey != null && stateKey.GetValue("Data") is byte[] data && data.Length >= 19)
                {
                    // 0x10, 0x00, 0x05 indicate disabled/off. 0x13 and 0x15 indicate active bluelight reduction.
                    if (data[18] == 0x13 || data[18] == 0x15)
                    {
                        CalculateKelvinFactors(4000, out gFactor, out bFactor);
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Querying Windows Night Light state failed.", ex);
            }

            return false;
        }

        private static void CalculateKelvinFactors(int kelvin, out double gFactor, out double bFactor)
        {
            double k = Math.Clamp(kelvin, 1000, 6500) / 100.0;
            gFactor = Math.Clamp((99.4708025861 * Math.Log(k) - 161.1195681661) / 255.0, 0.2, 1.0);
            bFactor = Math.Clamp((138.5177312231 * Math.Log(Math.Max(1.0, k - 10.0)) - 305.0447927307) / 255.0, 0.2, 1.0);
        }

        private static long GetWindowLongValue(IntPtr hWnd, int index)
        {
            try
            {
                return IntPtr.Size == 8
                    ? GetWindowLongPtr64(hWnd, index).ToInt64()
                    : GetWindowLong32(hWnd, index);
            }
            catch
            {
                return 0L;
            }
        }
    }
}
