using System;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
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
        private static readonly object StartStopSync = new object();
        private static readonly ManualResetEventSlim LoopExited = new ManualResetEventSlim(true);

        private static volatile string[] _lastFailedHotkeys = Array.Empty<string>();
        private static volatile string[] _ownedHotkeys = Array.Empty<string>();
        private static volatile bool _registrationCompleted;

        private const int WM_HOTKEY = 0x0312;
        private const int WM_APP_EXIT = 0x8001;
        private const int WM_DESTROY = 0x0002;
        private const int HOTKEY_REGION = 1;
        private const int HOTKEY_WINDOW = 2;
        private const int HOTKEY_FULLSCREEN = 3;
        private const int HOTKEY_LASTREGION = 4;
        private const int HOTKEY_CLIPBOARD = 5;
        private const int HOTKEY_PROBE = 9999;
        private static readonly TimeSpan LoopShutdownTimeout = TimeSpan.FromSeconds(3);

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
            lock (StartStopSync)
            {
                if (_running)
                {
                    return;
                }

                if (!LoopExited.Wait(LoopShutdownTimeout))
                {
                    BootstrapDebug.Log("HotkeyManager: previous message loop did not release its hotkeys in time; continuing anyway.");
                }

                _registrationCompleted = false;
                _ownedHotkeys = Array.Empty<string>();
                _running = true;
                LoopExited.Reset();
                _msgLoopThread = new Thread(MessageLoop) { IsBackground = true, Name = "HotkeyLoop" };
                _msgLoopThread.Start();
            }
        }

        public static Task RestartAsync()
        {
            return Task.Run(() =>
            {
                Stop();
                Start();
            });
        }

        private static void MessageLoop()
        {
            BootstrapDebug.Log("HotkeyManager: MessageLoop starting.");
            string className = "LG_Hotkey_Host_V3_" + Guid.NewGuid().ToString("N")[..8];
            IntPtr classNamePtr = Marshal.StringToHGlobalUni(className);
            IntPtr ownedWindow = IntPtr.Zero;

            try
            {
                WNDCLASSEX wc = default;
                wc.cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>();
                wc.lpfnWndProc = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, IntPtr>)&WndProc;
                wc.lpszClassName = classNamePtr;
                wc.hInstance = Marshal.GetHINSTANCE(typeof(HotkeyManager).Module);

                if (RegisterClassExW(&wc) == 0)
                {
                    ReportHotkeyHostFailure("register the hotkey listener class", Marshal.GetLastWin32Error());
                    return;
                }

                IntPtr hostWindow = CreateWindowExW(0, className, "LG_Hotkey_Host", 0, 0, 0, 0, 0, (IntPtr)(-3), IntPtr.Zero, wc.hInstance, IntPtr.Zero);
                if (hostWindow == IntPtr.Zero)
                {
                    ReportHotkeyHostFailure("create the hotkey listener window", Marshal.GetLastWin32Error());
                    return;
                }

                _hwnd = hostWindow;
                ownedWindow = hostWindow;

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
                if (ownedWindow != IntPtr.Zero)
                {
                    UnregisterAll(ownedWindow);
                    DestroyWindow(ownedWindow);
                    Interlocked.CompareExchange(ref _hwnd, IntPtr.Zero, ownedWindow);
                }

                _running = false;
                _ownedHotkeys = Array.Empty<string>();
                LoopExited.Set();
                BootstrapDebug.Log("HotkeyManager: MessageLoop exited.");
            }
        }

        private static void ReportHotkeyHostFailure(string action, int win32Error)
        {
            BootstrapDebug.Log($"HotkeyManager: failed to {action}. Error: {win32Error}");

            string[] configured;
            try
            {
                var cfg = IniConfig.GetIniSection<CoreConfiguration>();
                configured = new[] { cfg.RegionHotkey, cfg.WindowHotkey, cfg.FullscreenHotkey, cfg.LastregionHotkey, cfg.ClipboardHotkey }
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToArray();
            }
            catch
            {
                configured = new[] { "PrintScreen" };
            }

            _lastFailedHotkeys = configured;
            _registrationCompleted = true;

            try
            {
                ToastHelper.ShowToast(
                    "SnapVox Hotkeys Unavailable",
                    $"Windows refused to {action} (error {win32Error}). Global capture shortcuts are off for this session - use the tray icon, and restart SnapVox to try again.");
            }
            catch
            {
            }
        }

        public static bool IsHotkeyAvailable(string hotkeyString)
        {
            if (string.IsNullOrWhiteSpace(hotkeyString) || string.Equals(hotkeyString, "None", StringComparison.OrdinalIgnoreCase)) return true;

            if (IsOwnedBySnapVox(hotkeyString))
            {
                return true;
            }

            try
            {
                if (!TryParseHotkey(hotkeyString, out uint fsModifiers, out uint virtualKey))
                {
                    return false;
                }

                bool success = RegisterHotKey(IntPtr.Zero, HOTKEY_PROBE, fsModifiers, virtualKey);
                if (success) UnregisterHotKey(IntPtr.Zero, HOTKEY_PROBE);
                return success;
            }
            catch { }
            return false;
        }

        public static bool IsOwnedBySnapVox(string hotkeyString)
        {
            string normalized = NormalizeHotkey(hotkeyString);
            if (normalized.Length == 0) return false;

            string[] owned = _ownedHotkeys;
            for (int i = 0; i < owned.Length; i++)
            {
                if (string.Equals(owned[i], normalized, StringComparison.Ordinal)) return true;
            }

            return false;
        }

        public static string NormalizeHotkey(string hotkeyString)
        {
            if (!TryParseHotkey(hotkeyString, out uint fsModifiers, out uint virtualKey)) return string.Empty;
            return fsModifiers.ToString(CultureInfo.InvariantCulture) + ":" + virtualKey.ToString(CultureInfo.InvariantCulture);
        }

        private static bool TryParseHotkey(string hotkeyString, out uint fsModifiers, out uint virtualKey)
        {
            fsModifiers = MOD_NOREPEAT;
            virtualKey = 0;

            if (string.IsNullOrWhiteSpace(hotkeyString) || string.Equals(hotkeyString, "None", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (hotkeyString.Contains("Alt", StringComparison.OrdinalIgnoreCase)) fsModifiers |= MOD_ALT;
            if (hotkeyString.Contains("Ctrl", StringComparison.OrdinalIgnoreCase)) fsModifiers |= MOD_CONTROL;
            if (hotkeyString.Contains("Shift", StringComparison.OrdinalIgnoreCase)) fsModifiers |= MOD_SHIFT;
            if (hotkeyString.Contains("Win", StringComparison.OrdinalIgnoreCase)) fsModifiers |= MOD_WIN;

            string keyPart = hotkeyString.Split('+').Last().Trim();
            if (!Enum.TryParse<Keys>(keyPart, true, out var vk)) return false;

            virtualKey = (uint)vk;
            return true;
        }

        public static void Stop()
        {
            lock (StartStopSync)
            {
                if (!_running)
                {
                    return;
                }

                _running = false;
                IntPtr hwnd = _hwnd;
                if (hwnd != IntPtr.Zero)
                {
                    PostMessageW(hwnd, WM_APP_EXIT, IntPtr.Zero, IntPtr.Zero);
                }

                _msgLoopThread = null;
            }
        }

        public static bool RegistrationCompleted => _registrationCompleted;

        public static bool HasPrintScreenRegistrationFailure
        {
            get
            {
                var failed = _lastFailedHotkeys;
                if (failed == null) return false;
                foreach (var hotkey in failed)
                {
                    if (!string.IsNullOrEmpty(hotkey) && hotkey.IndexOf("PrintScreen", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
                return false;
            }
        }

        private static void RegisterAll()
        {
            var cfg = IniConfig.GetIniSection<CoreConfiguration>();
            if (cfg.DisableHotkeys)
            {
                _lastFailedHotkeys = System.Array.Empty<string>();
                _ownedHotkeys = System.Array.Empty<string>();
                _registrationCompleted = true;
                BootstrapDebug.Log("HotkeyManager: DisableHotkeys is set. No global hotkeys registered.");
                return;
            }

            var failures = new System.Collections.Generic.List<string>();
            var owned = new System.Collections.Generic.List<string>();

            RegisterTracked(HOTKEY_REGION, cfg.RegionHotkey, failures, owned);
            RegisterTracked(HOTKEY_WINDOW, cfg.WindowHotkey, failures, owned);
            RegisterTracked(HOTKEY_FULLSCREEN, cfg.FullscreenHotkey, failures, owned);
            RegisterTracked(HOTKEY_LASTREGION, cfg.LastregionHotkey, failures, owned);
            RegisterTracked(HOTKEY_CLIPBOARD, cfg.ClipboardHotkey, failures, owned);

            _ownedHotkeys = owned.ToArray();
            _lastFailedHotkeys = failures.ToArray();
            _registrationCompleted = true;

            if (failures.Count > 0)
            {
                string msg = "Failed to register: " + string.Join(", ", failures);
                if (HasPrintScreenRegistrationFailure)
                    {
                        PrintScreenConflictHelper.ShowConflictToast();
                    }
                    else
                    {
                        ToastHelper.ShowToast("Hotkey Conflict", msg + ". Another app (like OneDrive or Windows) might be using them.");
                    }
            }
        }

        private static void RegisterTracked(int id, string hotkeyString, System.Collections.Generic.List<string> failures, System.Collections.Generic.List<string> owned)
        {
            if (RegisterOne(id, hotkeyString))
            {
                string normalized = NormalizeHotkey(hotkeyString);
                if (normalized.Length > 0 && !owned.Contains(normalized)) owned.Add(normalized);
                return;
            }

            failures.Add(hotkeyString);
        }

        private static bool RegisterOne(int id, string hotkeyString)
        {
            if (string.IsNullOrWhiteSpace(hotkeyString) || string.Equals(hotkeyString, "None", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            try
            {
                if (TryParseHotkey(hotkeyString, out uint fsModifiers, out uint virtualKey))
                {
                    UnregisterHotKey(_hwnd, id);
                    if (RegisterHotKey(_hwnd, id, fsModifiers, virtualKey))
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

        private static void UnregisterAll(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            for (int i = 1; i <= 5; i++)
            {
                UnregisterHotKey(hwnd, i);
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
                        case HOTKEY_LASTREGION:
                            CaptureHelper.CaptureLastRegion(true);
                            break;
                        case HOTKEY_CLIPBOARD:
                            CaptureHelper.CaptureClipboard();
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
