using System;
using System.Runtime.InteropServices;
using System.Text;
using snapvox.native.foundation;

namespace snapvox.native
{
    public static class Win32WindowHelper
    {
        private const int GwlExstyle = -20;
        private const long WsExTransparent = 0x00000020L;

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

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            return IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);
        }

        private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            return IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : SetWindowLong32(hWnd, nIndex, dwNewLong);
        }

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
            for (int attempt = 0; attempt < 16 && hWnd != IntPtr.Zero; attempt++)
            {
                GetWindowThreadProcessId(hWnd, out uint processId);
                if (processId != currentProcessId)
                {
                    return hWnd;
                }

                IntPtr under = WindowFromPointSkipping(hWnd, point);
                if (under == IntPtr.Zero || under == hWnd)
                {
                    return IntPtr.Zero;
                }

                hWnd = under;
            }

            return IntPtr.Zero;
        }

        private static IntPtr WindowFromPointSkipping(IntPtr skipHwnd, POINT point)
        {
            IntPtr style = GetWindowLongPtr(skipHwnd, GwlExstyle);
            SetWindowLongPtr(skipHwnd, GwlExstyle, (IntPtr)(style.ToInt64() | WsExTransparent));
            try
            {
                return WindowFromPoint(point);
            }
            finally
            {
                SetWindowLongPtr(skipHwnd, GwlExstyle, style);
            }
        }

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);
    }
}
