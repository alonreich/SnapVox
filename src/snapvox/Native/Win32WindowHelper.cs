using System;
using System.Runtime.InteropServices;
using System.Text;
using snapvox.native.foundation;

namespace snapvox.native
{
    public static class Win32WindowHelper
    {
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(POINT Point);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        public const uint GA_ROOT = 2;

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        public static POINT GetCursorPosition()
        {
            if (GetCursorPos(out POINT point)) return point;
            return new POINT(0, 0);
        }

        [DllImport("user32.dll")]
        public static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetShellWindow();

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

        private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

        private const int DWMWA_CLOAKED = 13;

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttributeInt(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

        // ISSUE_033 (whole-display snap captured black): a cloaked window (suspended UWP host,
        // shell-hidden or swapped-out surface) is invisible on screen yet still hit-testable,
        // and rendering it prints a solid black frame. Never offer such windows as snap targets.
        private static bool IsCloakedWindow(IntPtr hWnd)
        {
            try
            {
                if (DwmGetWindowAttributeInt(hWnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) != 0) return false;
                return cloaked != 0;
            }
            catch
            {
                return false;
            }
        }

        public static bool GetWindowRectActual(IntPtr hWnd, out RECT rect)
        {
            if (DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf<RECT>()) == 0)
            {
                return true;
            }
            return GetWindowRect(hWnd, out rect);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        private const uint MONITOR_DEFAULTTONEAREST = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

        public static RECT GetMonitorBounds(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return RECT.Empty;
            IntPtr monitor = MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero) return RECT.Empty;
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfoW(monitor, ref info)) return RECT.Empty;
            return info.rcMonitor;
        }

        // ISSUE_024 (window capture accuracy): maximized windows report DWM extended frame
        // bounds that extend PAST the monitor edges by the invisible border thickness, so
        // captures came out larger than what was actually visible on screen (off-screen
        // overhang, slivers of neighbouring monitors, black bars). Clamping the frame to the
        // window's own monitor trims exactly that invisible overhang.
        public static RECT ClampRectToMonitor(IntPtr hWnd, RECT rect)
        {
            RECT monitor = GetMonitorBounds(hWnd);
            if (monitor.IsEmpty) return rect;
            int left = Math.Max(rect.Left, monitor.Left);
            int top = Math.Max(rect.Top, monitor.Top);
            int right = Math.Min(rect.Right, monitor.Right);
            int bottom = Math.Min(rect.Bottom, monitor.Bottom);
            if (right <= left || bottom <= top) return rect;
            return RECT.FromXYWH(left, top, right - left, bottom - top);
        }

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        public static string GetWindowClassName(IntPtr hWnd)
        {
            var sb = new StringBuilder(256);
            if (GetClassName(hWnd, sb, sb.Capacity) > 0) return sb.ToString();
            return string.Empty;
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool GetTokenInformation(IntPtr TokenHandle, int TokenInformationClass, IntPtr TokenInformation, uint TokenInformationLength, out uint ReturnLength);

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const uint TOKEN_QUERY = 0x0008;
        private const int TokenElevation = 20;

        public static bool IsProcessElevated(uint pid)
        {
            IntPtr processHandle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (processHandle == IntPtr.Zero) return true; // Assume elevated if we can't even open it

            try
            {
                if (OpenProcessToken(processHandle, TOKEN_QUERY, out IntPtr tokenHandle))
                {
                    try
                    {
                        IntPtr elevationPtr = Marshal.AllocHGlobal(sizeof(int));
                        try
                        {
                            if (GetTokenInformation(tokenHandle, TokenElevation, elevationPtr, sizeof(int), out _))
                            {
                                return Marshal.ReadInt32(elevationPtr) != 0;
                            }
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(elevationPtr);
                        }
                    }
                    finally
                    {
                        CloseHandle(tokenHandle);
                    }
                }
            }
            finally
            {
                CloseHandle(processHandle);
            }
            return false;
        }

        public static bool IsWindowElevated(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return false;
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == (uint)Environment.ProcessId) return false;
            return IsProcessElevated(pid);
        }

        public static RECT GetRootWindowRect(POINT point) => GetSnappableWindow(point, out _);

        // Resolves the snappable top-level window at the given point and reports both its
        // visible (DWM extended frame) bounds and its handle, so callers can capture that
        // specific window exclusively even when other windows cover it.
        public static RECT GetSnappableWindow(POINT point, out IntPtr rootWindowHandle)
        {
            rootWindowHandle = IntPtr.Zero;
            IntPtr hWnd = GetRootWindowHandle(point);
            if (hWnd == IntPtr.Zero) return RECT.Empty;

            string className = GetWindowClassName(hWnd);
            if (className == "Progman" || className == "WorkerW" || className == "Shell_TrayWnd" || className == "Shell_SecondaryTrayWnd")
            {
                return RECT.Empty;
            }

            // ISSUE_033 (whole-display snap captured black on the primary display): DWM/system
            // surface windows (DummyDWMTargetWindow & friends) and cloaked shell windows pass the
            // stock class filter, yet PrintWindow renders them as a solid black frame - snapping
            // to one opened an all-black snapshot of the entire display. They are never real app
            // windows: reject them up front (the capture pipeline also falls back to the frozen
            // screen crop whenever an exclusive render still comes back black).
            if (className == "DummyDWMTargetWindow" || className == "ThumbnailDeviceHelperWnd"
                || className == "Windows.UI.Composition.DesktopWindowContentBridge" || className == "EdgeUiInputTopWnd"
                || IsCloakedWindow(hWnd))
            {
                return RECT.Empty;
            }

            if (hWnd == GetDesktopWindow() || hWnd == GetShellWindow()) return RECT.Empty;

            if (GetWindowRectActual(hWnd, out RECT rect))
            {
                // ISSUE_024: trim the off-screen DWM frame overhang of maximized windows so
                // the highlighted/captured rect matches exactly what is visible on screen.
                rect = ClampRectToMonitor(hWnd, rect);
                int virtualW = GetSystemMetrics(78);
                int virtualH = GetSystemMetrics(79);
                if (rect.Width >= virtualW && rect.Height >= virtualH) return RECT.Empty;

                // ISSUE_027 (magnetic snap offered the entire display): the class filter above
                // only catches the stock desktop windows. Wallpaper-engine / desktop-surface
                // windows cover a whole monitor under a foreign class name, so hovering the
                // bare desktop background highlighted - and offered - the ENTIRE display as a
                // snap target. A candidate that covers (nearly) all of its monitor AND is the
                // bottom-most application window there (nothing but the desktop beneath it in
                // Z-order) is the background surface, not an app: never offer it.
                if (IsDesktopSurfaceWindow(hWnd, rect)) return RECT.Empty;

                rootWindowHandle = hWnd;
                return rect;
            }
            return RECT.Empty;
        }

        private static bool IsDesktopSurfaceWindow(IntPtr hWnd, RECT rect)
        {
            RECT monitor = GetMonitorBounds(hWnd);
            if (monitor.IsEmpty) return false;
            if ((long)rect.Width * rect.Height < ((long)monitor.Width * monitor.Height * 95) / 100) return false;

            // Walk DOWN the Z-order: if any other real app window shares this monitor below
            // the candidate, it is an ordinary fullscreen/maximized app - keep it snappable.
            uint currentPid = (uint)Environment.ProcessId;
            IntPtr below = GetWindow(hWnd, 2);
            while (below != IntPtr.Zero)
            {
                if (IsWindowVisible(below))
                {
                    GetWindowThreadProcessId(below, out uint pid);
                    if (pid != currentPid && GetWindowRect(below, out RECT r) && !r.IsEmpty
                        && r.Left < monitor.Right && r.Right > monitor.Left
                        && r.Top < monitor.Bottom && r.Bottom > monitor.Top)
                    {
                        string cls = GetWindowClassName(below);
                        if (cls != "Progman" && cls != "WorkerW" && cls != "Shell_TrayWnd" && cls != "Shell_SecondaryTrayWnd")
                        {
                            return false;
                        }
                    }
                }
                below = GetWindow(below, 2);
            }
            return true;
        }

        public static IntPtr GetRootWindowHandle(POINT point)
        {
            IntPtr hWnd = ResolveWindowAtPoint(point);
            if (hWnd == IntPtr.Zero) return IntPtr.Zero;

            IntPtr rootHWnd = GetAncestor(hWnd, GA_ROOT);
            return rootHWnd == IntPtr.Zero ? hWnd : rootHWnd;
        }

        private static IntPtr ResolveWindowAtPoint(POINT point)
        {
            IntPtr hWnd = WindowFromPoint(point);
            uint currentProcessId = (uint)Environment.ProcessId;
            while (hWnd != IntPtr.Zero)
            {
                GetWindowThreadProcessId(hWnd, out uint processId);
                if (processId != currentProcessId)
                {
                    if (IsWindowVisible(hWnd) && GetWindowRect(hWnd, out RECT r) && point.X >= r.Left && point.X < r.Right && point.Y >= r.Top && point.Y < r.Bottom) return hWnd;
                }
                hWnd = GetWindow(hWnd, 2);
            }
            return IntPtr.Zero;
        }

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);
    }
}
