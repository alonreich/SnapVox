using System;
using System.Runtime.InteropServices;
using System.Threading;
using snapvox.foundation.IniFile;
using snapvox.native.foundation;
using log4net;

namespace snapvox.foundation.core
{
    /// <summary>
    /// Keeps warm-light / night-mode tinting out of captures.
    ///
    /// Windows Night Light and f.lux in its classic mode apply their warm tint at scan-out
    /// (display colour transform / gamma ramp), which a GDI screen copy never sees. The tools
    /// that DO leak into a screenshot - f.lux software mode, Iris, SunsetScreen, Twilight,
    /// Dimmer, Redshift ports and most "night mode" utilities - work by parking a full-screen,
    /// click-through layered window on top of the desktop.
    ///
    /// A plain BitBlt from the screen DC excludes layered windows. It is the CAPTUREBLT flag
    /// that deliberately pulls them in, which is exactly why those overlays end up baked into
    /// the snip as a yellow/orange cast. So when such an overlay is on screen we drop
    /// CAPTUREBLT for that capture and the tint disappears, while every other capture keeps
    /// CAPTUREBLT so legitimate layered UI (menus, tooltips, drop shadows) is still included.
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

        private const int CoverageNumerator = 90;
        private const int CoverageDenominator = 100;
        private const int MaxScannedWindows = 400;

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

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        private const int SmCxscreen = 0;
        private const int SmCyscreen = 1;

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

                if (!CoversRegion(bounds, region) && !CoversPrimaryScreen(bounds)) return true;

                // A tinting overlay is click-through and/or refuses focus - a normal
                // full-screen layered app window (a media player, a game overlay HUD)
                // is not, so this keeps genuine content in the shot.
                bool passive = (exStyle & WsExTransparent) != 0
                    || (exStyle & WsExNoactivate) != 0
                    || ((exStyle & WsExToolwindow) != 0 && (exStyle & WsExTopmost) != 0);
                if (!passive) return true;

                // Finally, it must actually be translucent or colour-keyed.
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

        private static bool CoversRegion(RECT candidate, RECT region)
        {
            RECT overlap = RECT.Intersect(candidate, region);
            if (overlap.Width <= 0 || overlap.Height <= 0) return false;

            long regionArea = (long)region.Width * region.Height;
            long overlapArea = (long)overlap.Width * overlap.Height;
            return overlapArea * CoverageDenominator >= regionArea * CoverageNumerator;
        }

        private static bool CoversPrimaryScreen(RECT candidate)
        {
            int screenWidth = GetSystemMetrics(SmCxscreen);
            int screenHeight = GetSystemMetrics(SmCyscreen);
            if (screenWidth <= 0 || screenHeight <= 0) return false;

            return candidate.Width * CoverageDenominator >= screenWidth * CoverageNumerator
                && candidate.Height * CoverageDenominator >= screenHeight * CoverageNumerator;
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
