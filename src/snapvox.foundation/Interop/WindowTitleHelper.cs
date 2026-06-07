using snapvox.native;
using snapvox.native.foundation;
using snapvox.native.graphics;
using snapvox.native.ui;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace snapvox.foundation.Interop
{

    public static class WindowTitleHelper
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);

        public static string GetWindowTitle(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                GetWindowThreadProcessId(hWnd, out int processId);
                if (processId == 0)
                {
                    return null;
                }

                using var process = Process.GetProcessById(processId);
                if (process.MainWindowHandle == hWnd && !string.IsNullOrEmpty(process.MainWindowTitle))
                {
                    return process.MainWindowTitle;
                }

                return null;
            }
            catch (ArgumentException)
            {

                return null;
            }
            catch (InvalidOperationException)
            {

                return null;
            }
            catch (Win32Exception)
            {

                return null;
            }
        }
    }
}
