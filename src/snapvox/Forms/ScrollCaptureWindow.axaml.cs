using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using log4net;
using snapvox.foundation.core;
using snapvox.helpers;
using snapvox.native;
using snapvox.native.foundation;
using ImageSharpBgra = SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Bgra32>;

namespace snapvox.forms
{
    public partial class ScrollCaptureWindow : Window
    {
        private static readonly ILog Log = LogHelper.GetLogger(typeof(ScrollCaptureWindow));
        private static readonly object Sync = new object();
        private static readonly List<ScrollCaptureWindow> ActiveWindows = new List<ScrollCaptureWindow>();
        private static RECT SelectedRect = RECT.Empty;
        private static IntPtr SelectedWindowHandle = IntPtr.Zero;
        private static ScrollCaptureRecorder Recorder;
        private static Window OwnerWindow;
        private static bool OwnerWasVisible;
        private static bool IsRecording;
        private static bool IsClosingAll;
        private static bool IsFinishing;

        private PixelRect _screenBounds;
        private Canvas _mainCanvas;
        private Border _highlightBorder;
        private Border _instructionBorder;
        private TextBlock _instructionText;
        private TextBlock _statusText;
        private Button _exitButton;
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int WM_MOUSEHWHEEL = 0x020E;
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessageW(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private static DispatcherTimer _pollTimer;

        public void SetClickThrough(bool clickThrough)
        {
            var hwnd = this.TryGetPlatformHandle()?.Handle;
            if (hwnd == null || hwnd.Value == IntPtr.Zero) return;

            int exStyle = GetWindowLong(hwnd.Value, GWL_EXSTYLE);
            if (clickThrough)
                exStyle |= (WS_EX_TRANSPARENT | WS_EX_LAYERED);
            else
                exStyle &= ~(WS_EX_TRANSPARENT | WS_EX_LAYERED);

            SetWindowLong(hwnd.Value, GWL_EXSTYLE, exStyle);
        }

        private static void StartInputPolling()
        {
            if (_pollTimer != null) return;
            int ticks = 0;
            _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _pollTimer.Tick += (s, e) =>
            {
                if (!IsRecording)
                {
                    StopInputPolling();
                    return;
                }
                
                ticks++;
                if (ticks < 10) return; // 500ms grace period

                bool spacePressed = (GetAsyncKeyState(0x20) & 0x8000) != 0;
                bool leftClickPressed = (GetAsyncKeyState(0x01) & 0x8000) != 0;

                if (spacePressed || leftClickPressed) // Space or Left Click
                {
                    StopInputPolling();
                    _ = FinishRecordingAsync();
                }
                else if ((GetAsyncKeyState(0x1B) & 0x8000) != 0) // Escape
                {
                    StopInputPolling();
                    _ = ExitModeAsync();
                }
                else if ((GetAsyncKeyState(0x02) & 0x8000) != 0) // Right Mouse
                {
                    StopInputPolling();
                    _ = ExitModeAsync();
                }
            };
            _pollTimer.Start();
        }

        private static void StopInputPolling()
        {
            if (_pollTimer != null)
            {
                _pollTimer.Stop();
                _pollTimer = null;
            }
        }


        public ScrollCaptureWindow()
        {
            InitializeComponent();
        }

        public ScrollCaptureWindow(PixelRect screenBounds)
        {
            _screenBounds = screenBounds;
            InitializeComponent();

            double scaling = 1.0;
            try
            {
                var screen = Screens.ScreenFromPoint(new PixelPoint(screenBounds.X, screenBounds.Y));
                if (screen != null)
                {
                    scaling = screen.Scaling;
                }
            }
            catch
            {
            }

            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(screenBounds.X, screenBounds.Y);
            Width = screenBounds.Width / scaling;
            Height = screenBounds.Height / scaling;

            _mainCanvas = this.FindControl<Canvas>("MainCanvas");
            _highlightBorder = this.FindControl<Border>("HighlightBorder");
            _instructionBorder = this.FindControl<Border>("InstructionBorder");
            _instructionText = this.FindControl<TextBlock>("InstructionText");
            _statusText = this.FindControl<TextBlock>("StatusText");
            _exitButton = this.FindControl<Button>("ExitButton");

            KeyDown += OnKeyDown;
            Closed += OnClosed;

            LayoutFixedChrome();
            lock (Sync)
            {
                ActiveWindows.Add(this);
            }
        }

        public static async Task StartAsync(Window ownerWindow = null)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ActiveWindows.Count > 0)
                {
                    foreach (ScrollCaptureWindow window in ActiveWindows.ToList())
                    {
                        window.Activate();
                        window.Focus();
                    }
                    return;
                }

                OwnerWindow = ownerWindow;
                OwnerWasVisible = ownerWindow != null && ownerWindow.IsVisible;
                if (OwnerWasVisible)
                {
                    ownerWindow.Hide();
                }

                IsRecording = false;
                IsFinishing = false;
                IsClosingAll = false;
                SelectedRect = RECT.Empty;
                App.ForceRedTrayIcon(true);

                IReadOnlyList<PixelRect> screens = GetScreens(ownerWindow);
                if (screens == null || screens.Count == 0)
                {
                    RestoreOwner();
                    App.ForceRedTrayIcon(false);
                    return;
                }

                foreach (PixelRect screen in screens)
                {
                    var window = new ScrollCaptureWindow(screen);
                    window.Show();
                    window.Activate();
                    window.Focus();
                }
            });
        }

        private static IReadOnlyList<PixelRect> GetScreens(Window owner)
        {
            try
            {
                var screens = owner?.Screens.All;
                if (screens != null && screens.Count > 0)
                {
                    return screens.Select(screen => screen.Bounds).ToList();
                }

                var lifetime = Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
                screens = lifetime?.Windows.FirstOrDefault()?.Screens.All;
                if (screens != null && screens.Count > 0)
                {
                    return screens.Select(screen => screen.Bounds).ToList();
                }

                var probe = new Window();
                return probe.Screens.All.Select(screen => screen.Bounds).ToList();
            }
            catch
            {
                var probe = new Window();
                return probe.Screens.Primary == null ? Array.Empty<PixelRect>() : new[] { probe.Screens.Primary.Bounds };
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            Activate();
            Focus();
            UpdateSelectionFromCursor();
        }

        private void OnClosed(object sender, EventArgs e)
        {
            lock (Sync)
            {
                ActiveWindows.Remove(this);
                if (ActiveWindows.Count == 0)
                {
                    App.ForceRedTrayIcon(false);
                }
            }
        }

        private void OnPointerMoved(object sender, PointerEventArgs e)
        {
            if (!IsRecording)
            {
                UpdateSelectionFromCursor();
            }
        }

        private void OnPointerPressed(object sender, PointerPressedEventArgs e)
        {
            var props = e.GetCurrentPoint(this).Properties;
            if (props.IsRightButtonPressed)
            {
                _ = ExitModeAsync();
                e.Handled = true;
            }
            else if (props.IsLeftButtonPressed)
            {
                if (!IsRecording)
                {
                    StartRecording();
                }
                e.Handled = true;
            }
        }

        private void OnPointerWheelChanged(object sender, PointerWheelEventArgs e)
        {
            // Handled via WS_EX_TRANSPARENT | WS_EX_LAYERED click-through
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                _ = ExitModeAsync();
                e.Handled = true;
                return;
            }

            if (IsDelimiterKey(e))
            {
                if (IsRecording)
                {
                    _ = FinishRecordingAsync();
                }
                else
                {
                    StartRecording();
                }

                e.Handled = true;
            }
        }

        private static bool IsDelimiterKey(KeyEventArgs e)
        {
            var config = snapvox.foundation.IniFile.IniConfig.GetIniSection<CoreConfiguration>();
            string key = config.ScrollCaptureDelimiterHotkey;
            if (string.IsNullOrWhiteSpace(key))
            {
                key = "Space";
            }

            bool ctrl = key.Contains("Ctrl", StringComparison.OrdinalIgnoreCase);
            bool alt = key.Contains("Alt", StringComparison.OrdinalIgnoreCase);
            bool shift = key.Contains("Shift", StringComparison.OrdinalIgnoreCase);
            bool win = key.Contains("Win", StringComparison.OrdinalIgnoreCase);
            if (ctrl != e.KeyModifiers.HasFlag(KeyModifiers.Control)) return false;
            if (alt != e.KeyModifiers.HasFlag(KeyModifiers.Alt)) return false;
            if (shift != e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return false;
            if (win != e.KeyModifiers.HasFlag(KeyModifiers.Meta)) return false;

            string keyName = key.Split('+').Last().Trim();
            return string.Equals(e.Key.ToString(), keyName, StringComparison.OrdinalIgnoreCase);
        }

        private void OnExitClick(object sender, RoutedEventArgs e)
        {
            _ = ExitModeAsync();
        }

        private async void StartRecording()
        {
            RECT rect = SelectedRect.Normalize();
            if (rect.IsEmpty || rect.Width <= 20 || rect.Height <= 20)
            {
                BroadcastStatus("Point at a window", "No window selected");
                return;
            }

            try
            {
                if (SelectedWindowHandle != IntPtr.Zero)
                {
                    Win32WindowHelper.SetForegroundWindow(SelectedWindowHandle);
                    await Task.Delay(150); // Small delay to ensure focus is processed
                }

                Recorder = new ScrollCaptureRecorder(rect);
                Recorder.Start();
                IsRecording = true;
                foreach (var win in ActiveWindows) 
                { 
                    win.Cursor = new Avalonia.Input.Cursor(StandardCursorType.Arrow); 
                    win.SetClickThrough(true);
                    if (win._highlightBorder != null) win._highlightBorder.IsVisible = false;
                }
                StartInputPolling();
                BroadcastStatus("SCROLLING ACTIVE", "Space/Left-click finishes");
            }
            catch (Exception ex)
            {
                Log.Error("Could not start scroll capture.", ex);
                BroadcastStatus("Try again", "Could not start recording");
                IsRecording = false;
                Recorder = null;
            }
        }

        private static async Task FinishRecordingAsync()
        {
            if (IsFinishing)
            {
                return;
            }

            IsFinishing = true;
            StopInputPolling();
            foreach (var win in ActiveWindows) win.SetClickThrough(false);
            BroadcastStatus("Building image", "Please wait");
            ScrollCaptureRecorder recorder = Recorder;
            Recorder = null;
            IsRecording = false;

            ImageSharpBgra result = null;
            try
            {
                if (recorder != null)
                {
                    result = await recorder.FinishAsync().ConfigureAwait(false);
                    await recorder.DisposeAsync().ConfigureAwait(false);
                }

                if (result == null)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        BroadcastStatus("Try again more slowly", "Space = start");
                        IsFinishing = false;
                    });
                    return;
                }

                ImageSharpBgra clipboardImage = result.Clone();
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    RECT captureRect = RECT.FromXYWH(SelectedRect.Left, SelectedRect.Top, result.Width, result.Height);
                    CloseAllOverlays();
                    RestoreOwner();
                    CaptureHelper.OpenEditorForOwnedImage(result, captureRect);
                    result = null;
                });
                await UiClipboard.SetImageAsync(clipboardImage).ConfigureAwait(false);
                clipboardImage.Dispose();
            }
            catch (Exception ex)
            {
                result?.Dispose();
                Log.Error("Scroll capture finish failed.", ex);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    BroadcastStatus("Try again more slowly", "Space = start");
                    IsFinishing = false;
                });
            }
            finally
            {
                result?.Dispose();
            }
        }

        private static async Task CancelAttemptAsync()
        {
            ScrollCaptureRecorder recorder = Recorder;
            Recorder = null;
            IsRecording = false;
            IsFinishing = false;
            if (recorder != null)
            {
                await recorder.CancelAsync().ConfigureAwait(false);
                await recorder.DisposeAsync().ConfigureAwait(false);
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                SelectedRect = RECT.Empty;
                BroadcastStatus("Point at a window", "Space = start");
            });
        }

        private static async Task ExitModeAsync()
        {
            ScrollCaptureRecorder recorder = Recorder;
            Recorder = null;
            IsRecording = false;
            IsFinishing = false;
            if (recorder != null)
            {
                await recorder.CancelAsync().ConfigureAwait(false);
                await recorder.DisposeAsync().ConfigureAwait(false);
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                CloseAllOverlays();
                RestoreOwner();
            });
        }

        private void UpdateSelectionFromCursor()
        {
            POINT cursor = Win32WindowHelper.GetCursorPosition();
            IntPtr hwnd = Win32WindowHelper.GetRootWindowHandle(cursor);
            RECT rect = hwnd == IntPtr.Zero || !Win32WindowHelper.GetWindowRectActual(hwnd, out RECT windowRect) ? RECT.Empty : windowRect;
            SelectedWindowHandle = hwnd;
            SelectedRect = rect;
            BroadcastSelection(rect);
            if (rect.IsEmpty)
            {
                BroadcastStatus("Point at a window", "Space = start");
            }
            else
            {
                BroadcastStatus("Window ready", "Space = start");
            }
        }

        private static void BroadcastSelection(RECT rect)
        {
            foreach (ScrollCaptureWindow window in ActiveWindows.ToList())
            {
                window.ShowSelection(rect);
            }
        }

        private static void SendMouseWheel(IntPtr hwnd, int message, int delta, POINT cursor)
        {
            IntPtr wParam = new IntPtr(delta << 16);
            long packed = ((long)(cursor.Y & 0xFFFF) << 16) | (uint)(cursor.X & 0xFFFF);
            SendMessageW(hwnd, message, wParam, new IntPtr(packed));
        }

        private static void BroadcastStatus(string instruction, string status)
        {
            foreach (ScrollCaptureWindow window in ActiveWindows.ToList())
            {
                window.SetStatus(instruction, status);
            }
        }

        private void ShowSelection(RECT rect)
        {
            if (_highlightBorder == null)
            {
                return;
            }

            if (rect.IsEmpty)
            {
                _highlightBorder.IsVisible = false;
                return;
            }

            double scaling = RenderScaling;
            double left = (rect.Left - _screenBounds.X) / scaling;
            double top = (rect.Top - _screenBounds.Y) / scaling;
            double width = rect.Width / scaling;
            double height = rect.Height / scaling;
            if (left + width < 0 || top + height < 0 || left > Bounds.Width || top > Bounds.Height)
            {
                _highlightBorder.IsVisible = false;
                return;
            }

            _highlightBorder.IsVisible = true;
            Canvas.SetLeft(_highlightBorder, Math.Max(0, left));
            Canvas.SetTop(_highlightBorder, Math.Max(0, top));
            _highlightBorder.Width = Math.Min(Bounds.Width, left + width) - Math.Max(0, left);
            _highlightBorder.Height = Math.Min(Bounds.Height, top + height) - Math.Max(0, top);
        }

        private void SetStatus(string instruction, string status)
        {
            if (_instructionText != null)
            {
                _instructionText.Text = instruction;
            }

            if (_statusText != null)
            {
                _statusText.Text = status;
            }
        }

        private void LayoutFixedChrome()
        {
            if (_instructionBorder != null)
            {
                Canvas.SetLeft(_instructionBorder, 20);
                Canvas.SetTop(_instructionBorder, 20);
            }

            if (_exitButton != null)
            {
                Canvas.SetRight(_exitButton, 20);
                Canvas.SetTop(_exitButton, 20);
            }
        }

        private static void CloseAllOverlays()
        {
            if (IsClosingAll)
            {
                return;
            }

            IsClosingAll = true;
            List<ScrollCaptureWindow> windows;
            lock (Sync)
            {
                windows = ActiveWindows.ToList();
                ActiveWindows.Clear();
            }

            foreach (ScrollCaptureWindow window in windows)
            {
                window.Close();
            }

            App.ForceRedTrayIcon(false);
            IsClosingAll = false;
        }

        private static void RestoreOwner()
        {
            if (OwnerWindow != null && OwnerWasVisible)
            {
                OwnerWindow.Show();
                OwnerWindow.Activate();
            }

            OwnerWindow = null;
            OwnerWasVisible = false;
        }
    }
}
