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

        public static RECT GetRootWindowRect(POINT point)
        {
            IntPtr hWnd = GetRootWindowHandle(point);
            if (hWnd == IntPtr.Zero) return RECT.Empty;

            string className = GetWindowClassName(hWnd);
            if (className == "Progman" || className == "WorkerW" || className == "Shell_TrayWnd" || className == "Shell_SecondaryTrayWnd")
            {
                return RECT.Empty;
            }

            if (hWnd == GetDesktopWindow() || hWnd == GetShellWindow()) return RECT.Empty;

            if (GetWindowRectActual(hWnd, out RECT rect))
            {
                int virtualW = GetSystemMetrics(78);
                int virtualH = GetSystemMetrics(79);
                if (rect.Width >= virtualW && rect.Height >= virtualH) return RECT.Empty;

                return rect;
            }
            return RECT.Empty;
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
