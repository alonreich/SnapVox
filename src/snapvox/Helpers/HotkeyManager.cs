using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using snapvox.native;
using Avalonia.Threading;
using snapvox.native.foundation;
using snapvox.foundation.core;
using snapvox.foundation.core.AvaloniaShims;
using snapvox.foundation.IniFile;
using snapvox.foundation.Interfaces;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace snapvox.helpers
{
    public unsafe static class HotkeyManager
    {
        private static IntPtr _hwnd;
        private static Thread _msgLoopThread;
        private static volatile bool _running;

        private const int WM_HOTKEY = 0x0312;
        private const int WM_APP_EXIT = 0x8001;
        private const int WM_DESTROY = 0x0002;
        private const int HOTKEY_REGION = 1;
        private const int HOTKEY_WINDOW = 2;
        private const int HOTKEY_FULLSCREEN = 3;
        private const int HOTKEY_LASTREGION = 4;
        private const int HOTKEY_CLIPBOARD = 5;

        private const int MOD_ALT = 0x0001;
        private const int MOD_CONTROL = 0x0002;
        private const int MOD_SHIFT = 0x0004;
        private const int MOD_WIN = 0x0008;
        private const int MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowExW(uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern void PostQuitMessage(int nExitCode);

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
        }

        [DllImport("user32.dll")]
        private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref MSG lpMsg);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern ushort RegisterClassExW(WNDCLASSEX* lpwcx);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 8)]
        private struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public IntPtr lpszMenuName;
            public IntPtr lpszClassName;
            public IntPtr hIconSm;
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_HOTKEY)
            {
                int id = (int)wParam;
                HandleHotkey(id);
                return IntPtr.Zero;
            }

            if (msg == WM_APP_EXIT)
            {
                PostQuitMessage(0);
                return IntPtr.Zero;
            }

            if (msg == WM_DESTROY)
            {
                return IntPtr.Zero;
            }

            return DefWindowProcW(hWnd, msg, wParam, lParam);
        }

        public static void Start()
        {
            if (_running)
            {
                return;
            }

            _running = true;
            _msgLoopThread = new Thread(MessageLoop) { IsBackground = true, Name = "HotkeyLoop" };
            _msgLoopThread.Start();
        }

        private static void MessageLoop()
        {
            BootstrapDebug.Log("HotkeyManager: MessageLoop starting.");
            string className = "LG_Hotkey_Host_V3_" + Guid.NewGuid().ToString("N")[..8];
            IntPtr classNamePtr = Marshal.StringToHGlobalUni(className);

            try
            {
                WNDCLASSEX wc = default;
                wc.cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>();
                wc.lpfnWndProc = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, IntPtr>)&WndProc;
                wc.lpszClassName = classNamePtr;
                wc.hInstance = Marshal.GetHINSTANCE(typeof(HotkeyManager).Module);

                if (RegisterClassExW(&wc) == 0)
                {
                    BootstrapDebug.Log("Failed to register hotkey class. Error: " + Marshal.GetLastWin32Error());
                    className = "Static";
                }

                _hwnd = CreateWindowExW(0, className, "LG_Hotkey_Host", 0, 0, 0, 0, 0, (IntPtr)(-3), IntPtr.Zero, wc.hInstance, IntPtr.Zero);
                if (_hwnd == IntPtr.Zero)
                {
                    BootstrapDebug.Log("Failed to create hotkey host window. Error: " + Marshal.GetLastWin32Error());
                    return;
                }

                RegisterAll();

                MSG msg;
                while (_running && GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
            }
            catch (Exception ex)
            {
                BootstrapDebug.Log($"HotkeyManager: Fatal error: {ex.Message}");
            }
            finally
            {
                Marshal.FreeHGlobal(classNamePtr);
                UnregisterAll();
                if (_hwnd != IntPtr.Zero)
                {
                    DestroyWindow(_hwnd);
                    _hwnd = IntPtr.Zero;
                }

                BootstrapDebug.Log("HotkeyManager: MessageLoop exited.");
            }
        }

        public static bool IsHotkeyAvailable(string hotkeyString)
        {
            if (string.IsNullOrWhiteSpace(hotkeyString) || string.Equals(hotkeyString, "None", StringComparison.OrdinalIgnoreCase)) return true;
            try
            {
                uint fsModifiers = MOD_NOREPEAT;
                if (hotkeyString.Contains("Alt", StringComparison.OrdinalIgnoreCase)) fsModifiers |= MOD_ALT;
                if (hotkeyString.Contains("Ctrl", StringComparison.OrdinalIgnoreCase)) fsModifiers |= MOD_CONTROL;
                if (hotkeyString.Contains("Shift", StringComparison.OrdinalIgnoreCase)) fsModifiers |= MOD_SHIFT;
                if (hotkeyString.Contains("Win", StringComparison.OrdinalIgnoreCase)) fsModifiers |= MOD_WIN;
                string keyPart = hotkeyString.Split('+').Last().Trim();
                if (Enum.TryParse<Keys>(keyPart, true, out var vk))
                {
                    bool success = RegisterHotKey(IntPtr.Zero, 9999, fsModifiers, (uint)vk);
                    if (success) UnregisterHotKey(IntPtr.Zero, 9999);
                    return success;
                }
            }
            catch { }
            return false;
        }

        public static void Stop()
        {
            _running = false;
            IntPtr hwnd = _hwnd;
            if (hwnd != IntPtr.Zero)
            {
                PostMessageW(hwnd, WM_APP_EXIT, IntPtr.Zero, IntPtr.Zero);
            }

            var thread = _msgLoopThread;
            if (thread != null && thread.IsAlive)
            {
                try
                {
                    thread.Join(TimeSpan.FromSeconds(3));
                }
                catch
                {
                }
            }
        }

        public static void TriggerHotkey(int id)
        {
            HandleHotkey(id);
        }

        private static void RegisterAll()
        {
            var cfg = IniConfig.GetIniSection<CoreConfiguration>();
            var failures = new System.Collections.Generic.List<string>();

            if (!RegisterOne(HOTKEY_REGION, cfg.RegionHotkey)) failures.Add(cfg.RegionHotkey);
            if (!RegisterOne(HOTKEY_WINDOW, cfg.WindowHotkey)) failures.Add(cfg.WindowHotkey);
            if (!RegisterOne(HOTKEY_FULLSCREEN, cfg.FullscreenHotkey)) failures.Add(cfg.FullscreenHotkey);


            if (failures.Count > 0)
            {
                string msg = "Failed to register: " + string.Join(", ", failures);
                ToastHelper.ShowToast("Hotkey Conflict", msg + ". Another app (like OneDrive or Windows) might be using them.");
            }
        }

        private static bool RegisterOne(int id, string hotkeyString)
        {
            if (string.IsNullOrWhiteSpace(hotkeyString) || string.Equals(hotkeyString, "None", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            try
            {
                uint fsModifiers = MOD_NOREPEAT;
                if (hotkeyString.Contains("Alt", StringComparison.OrdinalIgnoreCase))
                {
                    fsModifiers |= MOD_ALT;
                }

                if (hotkeyString.Contains("Ctrl", StringComparison.OrdinalIgnoreCase))
                {
                    fsModifiers |= MOD_CONTROL;
                }

                if (hotkeyString.Contains("Shift", StringComparison.OrdinalIgnoreCase))
                {
                    fsModifiers |= MOD_SHIFT;
                }

                if (hotkeyString.Contains("Win", StringComparison.OrdinalIgnoreCase))
                {
                    fsModifiers |= MOD_WIN;
                }

                string keyPart = hotkeyString.Split('+').Last().Trim();
                if (Enum.TryParse<Keys>(keyPart, true, out var vk))
                {
                    UnregisterHotKey(_hwnd, id);
                    if (RegisterHotKey(_hwnd, id, fsModifiers, (uint)vk))
                    {
                        BootstrapDebug.Log($"Registered hotkey '{hotkeyString}' (ID: {id})");
                        return true;
                    }
                    else
                    {
                        BootstrapDebug.Log($"Failed to register hotkey {hotkeyString}. Error: {Marshal.GetLastWin32Error()}");
                    }
                }
            }
            catch (Exception ex)
            {
                BootstrapDebug.Log($"Hotkey registration failure: {ex.Message}");
            }
            return false;
        }

        private static void UnregisterAll()
        {
            if (_hwnd == IntPtr.Zero)
            {
                return;
            }

            for (int i = 1; i <= 5; i++)
            {
                UnregisterHotKey(_hwnd, i);
            }
        }

        private static void HandleHotkey(int id)
        {
            BootstrapDebug.Log($"Hotkey handled: {id}");
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    switch (id)
                    {
                        case HOTKEY_REGION:
                            CaptureHelper.CaptureRegion(true);
                            break;
                        case HOTKEY_FULLSCREEN:
                            CaptureHelper.CaptureFullscreen(false, ScreenCaptureMode.FullScreen);
                            break;
                        case HOTKEY_WINDOW:
                            CaptureHelper.CaptureActiveWindow(true);
                            break;

                    }
                }
                catch (Exception ex)
                {
                    BootstrapDebug.Log($"Hotkey action failed: {ex.Message}");
                }
            });
        }
    }
}
