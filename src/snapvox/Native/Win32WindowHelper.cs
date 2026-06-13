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
        public static extern IntPtr GetShellWindow();

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

        private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

        public static bool GetWindowRectActual(IntPtr hWnd, out RECT rect)
        {
            if (DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf<RECT>()) == 0)
            {
                return true;
            }
            return GetWindowRect(hWnd, out rect);
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

        public static RECT GetRootWindowRect(POINT point)
        {
            IntPtr hWnd = ResolveWindowAtPoint(point);
            if (hWnd == IntPtr.Zero) return RECT.Empty;

            IntPtr rootHWnd = GetAncestor(hWnd, GA_ROOT);
            if (rootHWnd == IntPtr.Zero) rootHWnd = hWnd;

            string className = GetWindowClassName(rootHWnd);
            if (className == "Progman" || className == "WorkerW" || className == "Shell_TrayWnd" || className == "Shell_SecondaryTrayWnd")
            {
                return RECT.Empty;
            }

            if (rootHWnd == GetDesktopWindow() || rootHWnd == GetShellWindow()) return RECT.Empty;

            if (GetWindowRectActual(rootHWnd, out RECT rect))
            {
                int virtualW = GetSystemMetrics(78);
                int virtualH = GetSystemMetrics(79);
                if (rect.Width >= virtualW && rect.Height >= virtualH) return RECT.Empty;

                return rect;
            }
            return RECT.Empty;
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
