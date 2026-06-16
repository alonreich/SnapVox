using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using snapvox.native.foundation;
using snapvox.foundation.core;
using snapvox.foundation.IniFile;
using snapvox.foundation.interfaces.Ocr;
using snapvox.helpers;
using snapvox.native;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using ImageSharpImage = SixLabors.ImageSharp.Image;
using AvaloniaSize = Avalonia.Size;
using AvaloniaColor = Avalonia.Media.Color;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace snapvox.forms
{
    public partial class CaptureWindow : Window
    {
        private static readonly Avalonia.Input.Cursor HandCursor = new Avalonia.Input.Cursor(StandardCursorType.Hand);
        private static readonly Avalonia.Input.Cursor CrossCursor = new Avalonia.Input.Cursor(StandardCursorType.Cross);
        private static readonly IBrush OcrHighlightBrush = new SolidColorBrush(AvaloniaColor.FromArgb(102, 255, 255, 0));

        private static readonly log4net.ILog Log = LogHelper.GetLogger(typeof(CaptureWindow));
        private static List<CaptureWindow> _activeWindows = new List<CaptureWindow>();
        private static readonly object SharedOcrLock = new object();
        private static bool _globalOcrMode;
        private const int MagneticThresholdPixels = 10;
        private Avalonia.Point _startPoint;
        private bool _isDragging;
        private bool _isWindowSnapActive;
        private Border _rubberband;
        private Border _highlightBorder;
        private bool _isPainterMode;
        private RECT _snappedRect = RECT.Empty;
        private OcrInformation _ocrScanResult;
        private OcrWordSpatialIndex _ocrWordIndex;
        private readonly object _ocrStateLock = new object();
        private HashSet<OcrWord> _paintedWords = new HashSet<OcrWord>();
        private List<Border> _highlightRects = new List<Border>();
        private Border _ocrHoverPreview;
        private Canvas _mainCanvas;
        private Border _instructionBorder;
        private Avalonia.Controls.Image _backgroundControl;
        private Border _dimensionBadge;
        private Border _windowSnapBadge;
        private TextBlock _dimensionText;
        private TextBlock _ocrText;
        private Border _ocrProcessingStatus;
        private CancellationTokenSource _ocrCts;
        private Task _ocrTask;
        private PixelRect _screenBounds;
        private bool _isClosingAll;
        private bool _ocrFailed;
        private Avalonia.Media.Imaging.Bitmap _backgroundBitmap;
        private volatile bool _isWindowOpen;
        private Border _magnifierPanel;
        private Avalonia.Controls.Image _magnifierImage;

        private double Scaling => this.RenderScaling;

        public CaptureWindow() : this(new PixelRect(0, 0, 1920, 1080), null)
        {
        }

        public CaptureWindow(PixelRect screenBounds, Avalonia.Media.Imaging.Bitmap background = null)
        {
            _screenBounds = screenBounds;
            InitializeComponent();
            
            double scaling = 1.0;
            try
            {
                var screen = Screens.ScreenFromPoint(new PixelPoint(screenBounds.X, screenBounds.Y));
                if (screen != null) scaling = screen.Scaling;
            }
            catch { }

            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(screenBounds.X, screenBounds.Y);
            Width = screenBounds.Width / scaling;
            Height = screenBounds.Height / scaling;

            _mainCanvas = this.FindControl<Canvas>("MainCanvas");
            _rubberband = this.FindControl<Border>("Rubberband");
            _backgroundControl = this.FindControl<Avalonia.Controls.Image>("BackgroundImage");
            _highlightBorder = this.FindControl<Border>("HighlightBorder");
            _instructionBorder = this.FindControl<Border>("InstructionBorder");
            _dimensionBadge = this.FindControl<Border>("DimensionBadge");
            _windowSnapBadge = this.FindControl<Border>("WindowSnapBadge");
            _dimensionText = this.FindControl<TextBlock>("DimensionText");
            _ocrText = this.FindControl<TextBlock>("OcrText");
            _ocrProcessingStatus = this.FindControl<Border>("OcrProcessingStatus");
            _magnifierPanel = this.FindControl<Border>("MagnifierPanel");
            _magnifierImage = this.FindControl<Avalonia.Controls.Image>("MagnifierImage");
            
            if (_backgroundControl != null && background != null)
            {
                _backgroundBitmap = background;
                _backgroundControl.Source = background;
                _backgroundControl.Width = screenBounds.Width / scaling;
                _backgroundControl.Height = screenBounds.Height / scaling;
                
                if (_magnifierImage != null)
                {
                    _magnifierImage.Source = background;
                }
            }

            _isWindowOpen = true;

            KeyDown += OnKeyDown;
            Closed += OnClosed;
            lock (_activeWindows) { _activeWindows.Add(this); }
            
            LayoutInstructionBanner(null);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            
            this.Activate();
            this.Focus();
            
            LayoutInstructionBanner(null);
            LayoutOcrStatus();
            App.ForceRedTrayIcon(true);
            UiClipboard.Register(text => Clipboard?.SetTextAsync(text) ?? Task.CompletedTask);
            if (_highlightBorder != null) _highlightBorder.IsVisible = false;
            if (_windowSnapBadge != null) _windowSnapBadge.IsVisible = false;
        }

        private async void OnClosed(object sender, EventArgs e)
        {
            _isWindowOpen = false;
            CloseAll();
            CaptureHelper.ClearFrozenSnapshot();
            App.ForceRedTrayIcon(false);
            await StopLocalOcrAsync().ConfigureAwait(true);
            lock (_activeWindows)
            {
                if (_activeWindows.Count == 0)
                {
                    _globalOcrMode = false;
                    CancelLocalPreemptiveOcr();
                }
            }
            if (_backgroundControl != null)
            {
                _backgroundControl.Source = null;
            }

            _backgroundBitmap?.Dispose();
            _backgroundBitmap = null;
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            base.OnClosing(e);
        }

        private void TryPostToUi(Action action)
        {
            if (!_isWindowOpen)
            {
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (_isWindowOpen)
                {
                    action();
                }
            });
        }

        private void CloseAll()
        {
            if (_isClosingAll) return;
            _isClosingAll = true;
            List<CaptureWindow> toClose;
            lock (_activeWindows) { toClose = _activeWindows.ToList(); _activeWindows.Clear(); }
            foreach (var win in toClose) { if (win != this) win.Close(); }
        }

        private void LayoutInstructionBanner(Avalonia.Point? cursorPosition)
        {
            if (_instructionBorder == null || _mainCanvas == null) return;
            _instructionBorder.Measure(new AvaloniaSize(Width, Height));
            double bannerWidth = _instructionBorder.DesiredSize.Width;
            double bannerHeight = _instructionBorder.DesiredSize.Height;
            
            double left = (Width - bannerWidth) / 2;
            double top = Height - bannerHeight - 60;

            if (cursorPosition.HasValue)
            {
                Rect bannerRect = new Rect(left - 20, top - 20, bannerWidth + 40, bannerHeight + 40);
                if (bannerRect.Contains(cursorPosition.Value))
                {
                    top = 60;
                }
            }

            Canvas.SetLeft(_instructionBorder, left);
            Canvas.SetTop(_instructionBorder, top);
        }

        private void LayoutOcrStatus()
        {
            if (_ocrProcessingStatus == null || _mainCanvas == null) return;
            _ocrProcessingStatus.Measure(new AvaloniaSize(Width, Height));
            double left = (Width - _ocrProcessingStatus.DesiredSize.Width) / 2;
            double top = (Height - _ocrProcessingStatus.DesiredSize.Height) / 2;
            Canvas.SetLeft(_ocrProcessingStatus, left);
            Canvas.SetTop(_ocrProcessingStatus, top);
        }

        private readonly object LocalOcrLock = new object();
        private CancellationTokenSource _localOcrCts;
        private Task<OcrInformation> _localOcrTask;

        private Task<OcrInformation> StartLocalPreemptiveOcr(PixelRect targetRegion)
        {
            lock (LocalOcrLock)
            {
                if (_localOcrTask != null && !_localOcrTask.IsCompleted)
                {
                    return _localOcrTask;
                }

                _localOcrCts?.Dispose();
                _localOcrCts = new CancellationTokenSource();
                _localOcrTask = RunLocalPreemptiveOcrAsync(targetRegion, _localOcrCts.Token);
                return _localOcrTask;
            }
        }

        private void CancelLocalPreemptiveOcr()
        {
            CancellationTokenSource cts;
            Task<OcrInformation> task;
            lock (LocalOcrLock)
            {
                cts = _localOcrCts;
                task = _localOcrTask;
                _localOcrCts = null;
                _localOcrTask = null;
            }

            if (cts == null)
            {
                return;
            }

            try
            {
                cts.Cancel();
            }
            finally
            {
                if (task != null)
                {
                    _ = task.ContinueWith(completed =>
                    {
                        _ = completed.Exception;
                        cts.Dispose();
                    }, TaskScheduler.Default);
                }
                else
                {
                    cts.Dispose();
                }
            }
        }

        private static IOcrProvider SelectOcrProvider()
        {
            var providers = SimpleServiceProvider.Current.GetAllInstances<IOcrProvider>();
            var config = IniConfig.GetIniSection<CoreConfiguration>();
            return OcrProviderSelector.Select(providers, config.OcrEngine);
        }

        private static async Task<OcrInformation> RunLocalPreemptiveOcrAsync(PixelRect targetRegion, CancellationToken cancellationToken)
        {
            var ocrProvider = SelectOcrProvider();
            if (ocrProvider == null)
            {
                return null;
            }

            var nativeRect = RECT.FromXYWH(targetRegion.X, targetRegion.Y, targetRegion.Width, targetRegion.Height);
            using ImageSharpImage image = await Task.Run(() => CaptureHelper.GetFrozenSnapshot(nativeRect)).ConfigureAwait(false);
            if (image == null)
            {
                return null;
            }

            OcrInformation result = await ocrProvider.DoOcrAsync(image, cancellationToken).ConfigureAwait(false);
            result?.Offset(targetRegion.X, targetRegion.Y);
            return result;
        }

        private async Task ObserveLocalPreemptiveOcrAsync(CancellationTokenSource cts)
        {
            try
            {
                TryPostToUi(() => { if (_isPainterMode && _ocrProcessingStatus != null) _ocrProcessingStatus.IsVisible = true; });
                OcrInformation result = await StartLocalPreemptiveOcr(_screenBounds).WaitAsync(cts.Token).ConfigureAwait(false);
                if (result == null || cts.Token.IsCancellationRequested || !_isWindowOpen)
                {
                    _ocrFailed = result == null;
                    TryPostToUi(() =>
                    {
                        if (_ocrProcessingStatus != null) _ocrProcessingStatus.IsVisible = false;
                        if (_isPainterMode)
                        {
                            var instruction = this.FindControl<TextBlock>("InstructionText");
                            if (instruction != null) instruction.Text = "Click text to capture";
                        }
                    });
                    return;
                }

                lock (_ocrStateLock)
                {
                    if (cts.Token.IsCancellationRequested || !_isWindowOpen)
                    {
                        return;
                    }

                    _ocrScanResult = result;
                    _ocrWordIndex = OcrWordSpatialIndex.Create(result.Words);
                    _ocrFailed = false;
                }

                TryPostToUi(() =>
                {
                    if (_ocrProcessingStatus != null) _ocrProcessingStatus.IsVisible = false;
                    if (_isPainterMode)
                    {
                        var instruction = this.FindControl<TextBlock>("InstructionText");
                        if (instruction != null) instruction.Text = "Click text to capture";
                    }
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                BootstrapDebug.Log($"OCR failed: {ex.Message}");
                _ocrFailed = true;
                TryPostToUi(() =>
                {
                    if (_ocrProcessingStatus != null) _ocrProcessingStatus.IsVisible = false;
                    if (_isPainterMode)
                    {
                        var instruction = this.FindControl<TextBlock>("InstructionText");
                        if (instruction != null) instruction.Text = "Click text to capture";
                    }
                });
            }
            finally
            {
                lock (_ocrStateLock)
                {
                    if (ReferenceEquals(_ocrCts, cts))
                    {
                        _ocrCts = null;
                        _ocrTask = null;
                    }
                }

                cts.Dispose();
            }
        }

        private async Task StopLocalOcrAsync()
        {
            Task task;
            CancellationTokenSource cts;
            lock (_ocrStateLock)
            {
                task = _ocrTask;
                cts = _ocrCts;
                _ocrTask = null;
                _ocrCts = null;
                _ocrScanResult = null;
                _ocrWordIndex = null;
            }

            cts?.Cancel();

            if (task != null)
            {
                try
                {
                    await task.ConfigureAwait(false);
                }
                catch
                {
                }
            }
            else
            {
                cts?.Dispose();
            }
        }

        private bool IsOcrReady()
        {
            lock (_ocrStateLock) { return _ocrScanResult?.Words != null; }
        }

        private void ShowOcrWaitingState()
        {
            if (_ocrFailed)
            {
                var text = this.FindControl<TextBlock>("InstructionText");
                if (text != null) text.Text = "Text capture unavailable";
                if (_ocrProcessingStatus != null) _ocrProcessingStatus.IsVisible = false;
                return;
            }

            if (_ocrProcessingStatus != null) _ocrProcessingStatus.IsVisible = true;
            var instruction = this.FindControl<TextBlock>("InstructionText");
            if (instruction != null) instruction.Text = "Text capture loading...";
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            Log.Debug($"CaptureWindow KeyDown: {e.Key}");
            if (e.Key == Key.Escape) { Close(); return; }
            if (e.Key == Key.C) { CaptureAndCopyCurrentSelection(); return; }
            if (e.Key == Key.T || e.Key == Key.O)
            {
                Log.Info("OCR mode toggled via hotkey (scoping to local screens).");
                SetGlobalOcrMode(!_globalOcrMode);
                e.Handled = true;
            }
        }

        private static void SetGlobalOcrMode(bool enable)
        {
            _globalOcrMode = enable;

            List<CaptureWindow> windows;
            lock (_activeWindows) { windows = _activeWindows.ToList(); }
            foreach (var window in windows)
            {
                window.ApplyOcrMode(enable);
            }
        }

        private void ApplyOcrMode(bool enable)
        {
            if (_isPainterMode == enable) return;
            var text = this.FindControl<TextBlock>("InstructionText");
            if (enable) EnterOcrMode(text);
            else ExitOcrMode(text);
        }

        private void EnterOcrMode(TextBlock instructionText)
        {
            _isPainterMode = true;
            _ocrFailed = false;
            _isDragging = false;
            _isWindowSnapActive = false;
            _snappedRect = RECT.Empty;

            lock (_ocrStateLock)
            {
                if (_ocrScanResult == null && _ocrCts == null)
                {
                    _ocrCts = new CancellationTokenSource();
                    _ocrTask = ObserveLocalPreemptiveOcrAsync(_ocrCts);
                }
                else if (_ocrScanResult == null && _ocrProcessingStatus != null)
                {
                    _ocrProcessingStatus.IsVisible = true;
                }
            }

            if (instructionText != null) instructionText.Text = IsOcrReady() ? "Click text to capture" : "Text capture loading...";
            if (_ocrText != null)
            {
                _ocrText.Text = "T = Exit text capture";
                _ocrText.Foreground = Brushes.White;
            }
            Cursor = HandCursor;
            _rubberband.IsVisible = false;
            if (_highlightBorder != null) _highlightBorder.IsVisible = false;
            if (_windowSnapBadge != null) _windowSnapBadge.IsVisible = false;
            if (_dimensionBadge != null) _dimensionBadge.IsVisible = false;
            if (_magnifierPanel != null) _magnifierPanel.IsVisible = false;
        }

        private void ExitOcrMode(TextBlock instructionText)
        {
            _isPainterMode = false;
            _ = StopLocalOcrAsync();
            CancelLocalPreemptiveOcr();

            _isDragging = false;
            _isWindowSnapActive = false;
            _snappedRect = RECT.Empty;
            ClearHighlights();
            _paintedWords.Clear();
            lock (_ocrStateLock)
            {
                _ocrScanResult = null;
                _ocrWordIndex = null;
            }

            if (_rubberband != null)
            {
                _rubberband.IsVisible = false;
                _rubberband.Width = 0;
                _rubberband.Height = 0;
            }
            if (_highlightBorder != null) _highlightBorder.IsVisible = false;
            if (_windowSnapBadge != null) _windowSnapBadge.IsVisible = false;
            if (_dimensionBadge != null) _dimensionBadge.IsVisible = false;
            if (_ocrProcessingStatus != null) _ocrProcessingStatus.IsVisible = false;
            if (_instructionBorder != null) _instructionBorder.IsVisible = true;

            if (instructionText != null) instructionText.Text = "Drag to capture or hover window";
            if (_ocrText != null)
            {
                _ocrText.Text = "T = Text capture";
                _ocrText.Foreground = new SolidColorBrush(AvaloniaColor.Parse("#AAAAAA"));
            }
            Cursor = new Cursor(StandardCursorType.Cross);
        }

        private RECT GetFullscreenRect()
        {
            var screens = this.Screens.All;
            if (screens == null || !screens.Any()) return RECT.Empty;
            var union = screens.Aggregate(screens.First().Bounds, (current, screen) => current.Union(screen.Bounds));
            return RECT.FromXYWH(union.X, union.Y, union.Width, union.Height);
        }

        private void CaptureAndCopyCurrentSelection()
        {
            RECT target = RECT.Empty;
            double scaling = Scaling;
            if (_isWindowSnapActive && !_snappedRect.IsEmpty) target = _snappedRect;
            else if (_rubberband.IsVisible && _rubberband.Width > 5) 
                target = RECT.FromXYWH((int)(Canvas.GetLeft(_rubberband) * scaling + Position.X), (int)(Canvas.GetTop(_rubberband) * scaling + Position.Y), (int)(_rubberband.Width * scaling), (int)(_rubberband.Height * scaling));

            if (target.IsEmpty) return;
            _ = CaptureAndCopyCurrentSelectionAsync(target);
        }

        private async Task CaptureAndCopyCurrentSelectionAsync(RECT target)
        {
            using var captured = NativeCapture.CaptureRegion(target);
            if (captured == null) return;

            if (IniConfig.GetIniSection<CoreConfiguration>().KeepBackup)
            {
                try
                {
                    string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SnapVox");
                    System.IO.Directory.CreateDirectory(tempDir);
                    string fileName = $"Capture_{DateTime.Now:yyyy-MM-dd HH_mm_ss_fff}.jpg";
                    string fullPath = System.IO.Path.Combine(tempDir, fileName);
                    await Task.Run(() => captured.Save(fullPath, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = snapvox.foundation.IniFile.IniConfig.GetIniSection<CoreConfiguration>().OutputFileJpegQuality })).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.Error("[BACKUP_FAILURE] Could not write to temp folder during direct copy.", ex);
                }
            }

            await UiClipboard.SetImageAsync(captured).ConfigureAwait(false);
            TryPostToUi(Close);
        }

        private void OnPointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
            {
                Close();
                return;
            }

            _isDragging = true;
            if (_instructionBorder != null) _instructionBorder.IsVisible = false;
            var pos = e.GetPosition(this);
            double scaling = Scaling;
            var absolutePos = new POINT((int)(pos.X * scaling) + Position.X, (int)(pos.Y * scaling) + Position.Y);
            RECT windowRect = Win32WindowHelper.GetRootWindowRect(absolutePos);
            double snapX = pos.X;
            double snapY = pos.Y;
            if (!windowRect.IsEmpty)
            {
                double relLeft = (windowRect.Left - Position.X) / scaling;
                double relTop = (windowRect.Top - Position.Y) / scaling;
                double relRight = (windowRect.Right - Position.X) / scaling;
                double relBottom = (windowRect.Bottom - Position.Y) / scaling;
                if (Math.Abs(pos.X - relLeft) < MagneticThresholdPixels) snapX = relLeft;
                else if (Math.Abs(pos.X - relRight) < MagneticThresholdPixels) snapX = relRight;
                if (Math.Abs(pos.Y - relTop) < MagneticThresholdPixels) snapY = relTop;
                else if (Math.Abs(pos.Y - relBottom) < MagneticThresholdPixels) snapY = relBottom;
            }
            _startPoint = new Avalonia.Point(snapX, snapY);
            if (_isPainterMode)
            {
                if (!IsOcrReady())
                {
                    _isDragging = false;
                    if (_instructionBorder != null) _instructionBorder.IsVisible = true;
                    ShowOcrWaitingState();
                    return;
                }

                ClearHighlights();
                _paintedWords.Clear();
                PaintWords(pos);
                return;
            }
            _rubberband.IsVisible = true;
            Canvas.SetLeft(_rubberband, _startPoint.X);
            Canvas.SetTop(_rubberband, _startPoint.Y);
            _rubberband.Width = 0;
            _rubberband.Height = 0;
            if (_highlightBorder != null) _highlightBorder.IsVisible = false;
            if (_windowSnapBadge != null) _windowSnapBadge.IsVisible = false;
        }

        private void OnPointerMoved(object sender, PointerEventArgs e)
        {
            var currentPoint = e.GetPosition(this);
            double scaling = Scaling;
            
            if (_instructionBorder != null && !_isDragging)
            {
                LayoutInstructionBanner(currentPoint);
            }

            if (_isPainterMode)
            {
                if (!IsOcrReady())
                {
                    ShowOcrWaitingState();
                    return;
                }

                if (_isDragging)
                {
                    PaintWords(currentPoint);
                }
                else
                {
                    ShowOcrHoverPreview(currentPoint);
                }
                return;
            }

            if (!_isDragging)
            {
                UpdateMagnifier(currentPoint);
                CheckMagneticSnap(currentPoint);
                return;
            }
            var absoluteCurrent = new POINT((int)(currentPoint.X * scaling) + Position.X, (int)(currentPoint.Y * scaling) + Position.Y);
            RECT snapRect = Win32WindowHelper.GetRootWindowRect(absoluteCurrent);
            double snappedX = currentPoint.X;
            double snappedY = currentPoint.Y;
            if (!snapRect.IsEmpty)
            {
                double relLeft = (snapRect.Left - Position.X) / scaling;
                double relTop = (snapRect.Top - Position.Y) / scaling;
                double relRight = (snapRect.Right - Position.X) / scaling;
                double relBottom = (snapRect.Bottom - Position.Y) / scaling;
                if (Math.Abs(currentPoint.X - relLeft) < MagneticThresholdPixels) snappedX = relLeft;
                else if (Math.Abs(currentPoint.X - relRight) < MagneticThresholdPixels) snappedX = relRight;
                if (Math.Abs(currentPoint.Y - relTop) < MagneticThresholdPixels) snappedY = relTop;
                else if (Math.Abs(currentPoint.Y - relBottom) < MagneticThresholdPixels) snappedY = relBottom;
                bool isWholeWindowSnap = (Math.Abs(_startPoint.X - relLeft) < 1) && (Math.Abs(_startPoint.Y - relTop) < 1) && (Math.Abs(snappedX - relRight) < 1) && (Math.Abs(snappedY - relBottom) < 1);
                if (isWholeWindowSnap)
                {
                    _isWindowSnapActive = true;
                    _snappedRect = snapRect;
                    if (_highlightBorder != null)
                    {
                        _highlightBorder.IsVisible = true;
                        Canvas.SetLeft(_highlightBorder, relLeft);
                        Canvas.SetTop(_highlightBorder, relTop);
                        _highlightBorder.Width = relRight - relLeft;
                        _highlightBorder.Height = relBottom - relTop;
                    }
                    ShowWindowSnapBadge(relLeft, relTop);
                }
                else { _isWindowSnapActive = false; if (_highlightBorder != null) _highlightBorder.IsVisible = false; if (_windowSnapBadge != null) _windowSnapBadge.IsVisible = false; }
            }
            double x = Math.Min(_startPoint.X, snappedX);
            double y = Math.Min(_startPoint.Y, snappedY);
            double w = Math.Abs(_startPoint.X - snappedX);
            double h = Math.Abs(_startPoint.Y - snappedY);
            Canvas.SetLeft(_rubberband, x);
            Canvas.SetTop(_rubberband, y);
            _rubberband.Width = w;
            _rubberband.Height = h;

            if (_dimensionBadge != null && _dimensionText != null)
            {
                _dimensionBadge.IsVisible = w > 10 && h > 10;
                _dimensionText.Text = $"{(int)(w * scaling)} x {(int)(h * scaling)}";
                Canvas.SetLeft(_dimensionBadge, x);
                double badgeY = y - 28;
                if (badgeY < 0) badgeY = y + 5;
                Canvas.SetTop(_dimensionBadge, badgeY);
            }

            UpdateMagnifier(currentPoint);
        }

        private void UpdateMagnifier(Avalonia.Point pos)
        {
            if (_isPainterMode || _magnifierPanel == null)
            {
                if (_magnifierPanel != null) _magnifierPanel.IsVisible = false;
                return;
            }

            _magnifierPanel.IsVisible = true;

            double magSize = !double.IsNaN(_magnifierPanel.Width) && _magnifierPanel.Width > 0 ? _magnifierPanel.Width : 160;
            double offset = 30;
            var target = ChooseMagnifierPosition(pos, magSize, offset, GetMagnifierAvoidRect(12));

            Canvas.SetLeft(_magnifierPanel, target.X);
            Canvas.SetTop(_magnifierPanel, target.Y);

            if (_magnifierImage != null)
            {
                double scaling = Scaling;
                _magnifierImage.Width = _screenBounds.Width / scaling;
                _magnifierImage.Height = _screenBounds.Height / scaling;
                Canvas.SetLeft(_magnifierImage, magSize / 2 - pos.X * 2);
                Canvas.SetTop(_magnifierImage, magSize / 2 - pos.Y * 2);
                _magnifierImage.RenderTransform = new ScaleTransform(2, 2);
                _magnifierImage.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
            }
        }

        private Rect? GetMagnifierAvoidRect(double margin)
        {
            if (_rubberband != null && _rubberband.IsVisible && _rubberband.Width > 1 && _rubberband.Height > 1)
            {
                double left = Canvas.GetLeft(_rubberband);
                double top = Canvas.GetTop(_rubberband);
                if (double.IsNaN(left)) left = 0;
                if (double.IsNaN(top)) top = 0;
                return InflateRect(new Rect(left, top, _rubberband.Width, _rubberband.Height), margin);
            }

            if (_highlightBorder != null && _highlightBorder.IsVisible && _highlightBorder.Width > 1 && _highlightBorder.Height > 1)
            {
                double left = Canvas.GetLeft(_highlightBorder);
                double top = Canvas.GetTop(_highlightBorder);
                if (double.IsNaN(left)) left = 0;
                if (double.IsNaN(top)) top = 0;
                return InflateRect(new Rect(left, top, _highlightBorder.Width, _highlightBorder.Height), margin);
            }

            return null;
        }

        private Rect ChooseMagnifierPosition(Avalonia.Point cursor, double magSize, double offset, Rect? avoidRect)
        {
            double canvasWidth = Bounds.Width > 0 ? Bounds.Width : Width;
            double canvasHeight = Bounds.Height > 0 ? Bounds.Height : Height;
            if (double.IsNaN(canvasWidth) || canvasWidth <= 0) canvasWidth = _screenBounds.Width / Math.Max(Scaling, 1.0);
            if (double.IsNaN(canvasHeight) || canvasHeight <= 0) canvasHeight = _screenBounds.Height / Math.Max(Scaling, 1.0);

            Rect best = new Rect();
            double bestScore = double.MaxValue;

            if (avoidRect.HasValue)
            {
                var avoid = avoidRect.Value;
                double ar = avoid.X + avoid.Width;
                double ab = avoid.Y + avoid.Height;

                void Eval(double left, double top)
                {
                    Rect candidate = new Rect(left, top, magSize, magSize);
                    Rect clamped = ClampRect(candidate, canvasWidth, canvasHeight);
                    double overlapPenalty = avoidRect.HasValue ? RectOverlapArea(clamped, avoidRect.Value) * 100000 : 0;
                    double clampPenalty = Distance(candidate.X, candidate.Y, clamped.X, clamped.Y) * 10;
                    double cursorPenalty = Distance(cursor.X, cursor.Y, clamped.X + clamped.Width / 2, clamped.Y + clamped.Height / 2);
                    double avoidBonus = avoidRect.HasValue ? RectDistance(clamped, avoidRect.Value) * 0.25 : 0;
                    double score = overlapPenalty + clampPenalty + cursorPenalty - avoidBonus;
                    if (score < bestScore) { bestScore = score; best = clamped; }
                }

                Eval(ar + offset, cursor.Y - magSize / 2);
                Eval(avoid.X - magSize - offset, cursor.Y - magSize / 2);
                Eval(cursor.X - magSize / 2, ab + offset);
                Eval(cursor.X - magSize / 2, avoid.Y - magSize - offset);
                Eval(ar + offset, ab + offset);
                Eval(avoid.X - magSize - offset, ab + offset);
                Eval(ar + offset, avoid.Y - magSize - offset);
                Eval(avoid.X - magSize - offset, avoid.Y - magSize - offset);
            }

            void EvalDirect(double left, double top)
            {
                Rect candidate = new Rect(left, top, magSize, magSize);
                Rect clamped = ClampRect(candidate, canvasWidth, canvasHeight);
                double overlapPenalty = avoidRect.HasValue ? RectOverlapArea(clamped, avoidRect.Value) * 100000 : 0;
                double clampPenalty = Distance(candidate.X, candidate.Y, clamped.X, clamped.Y) * 10;
                double cursorPenalty = Distance(cursor.X, cursor.Y, clamped.X + clamped.Width / 2, clamped.Y + clamped.Height / 2);
                double avoidBonus = avoidRect.HasValue ? RectDistance(clamped, avoidRect.Value) * 0.25 : 0;
                double score = overlapPenalty + clampPenalty + cursorPenalty - avoidBonus;
                if (score < bestScore) { bestScore = score; best = clamped; }
            }

            EvalDirect(cursor.X + offset, cursor.Y + offset);
            EvalDirect(cursor.X - magSize - offset, cursor.Y + offset);
            EvalDirect(cursor.X + offset, cursor.Y - magSize - offset);
            EvalDirect(cursor.X - magSize - offset, cursor.Y - magSize - offset);

            return best;
        }

        private static Rect InflateRect(Rect rect, double margin)
        {
            return new Rect(rect.X - margin, rect.Y - margin, rect.Width + margin * 2, rect.Height + margin * 2);
        }

        private static Rect ClampRect(Rect rect, double canvasWidth, double canvasHeight)
        {
            double maxLeft = Math.Max(0, canvasWidth - rect.Width);
            double maxTop = Math.Max(0, canvasHeight - rect.Height);
            return new Rect(Math.Clamp(rect.X, 0, maxLeft), Math.Clamp(rect.Y, 0, maxTop), rect.Width, rect.Height);
        }

        private static double RectOverlapArea(Rect a, Rect b)
        {
            double left = Math.Max(a.X, b.X);
            double top = Math.Max(a.Y, b.Y);
            double right = Math.Min(a.X + a.Width, b.X + b.Width);
            double bottom = Math.Min(a.Y + a.Height, b.Y + b.Height);
            if (right <= left || bottom <= top) return 0;
            return (right - left) * (bottom - top);
        }

        private static double RectDistance(Rect a, Rect b)
        {
            double dx = Math.Max(Math.Max(b.X - (a.X + a.Width), a.X - (b.X + b.Width)), 0);
            double dy = Math.Max(Math.Max(b.Y - (a.Y + a.Height), a.Y - (b.Y + b.Height)), 0);
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static double Distance(double x1, double y1, double x2, double y2)
        {
            double dx = x1 - x2;
            double dy = y1 - y2;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private async Task OnPointerReleasedAsync(PointerReleasedEventArgs e)
        {
            var endPoint = e.GetPosition(this);
            double deltaX = Math.Abs(endPoint.X - _startPoint.X);
            double deltaY = Math.Abs(endPoint.Y - _startPoint.Y);
            bool isIntentionalDrag = deltaX > 5 || deltaY > 5;
            _isDragging = false;
            
            if (!_isPainterMode && _instructionBorder != null) _instructionBorder.IsVisible = true;
            if (_magnifierPanel != null) _magnifierPanel.IsVisible = false;

            if (_isPainterMode)
            {
                if (!IsOcrReady())
                {
                    ShowOcrWaitingState();
                    if (_instructionBorder != null) _instructionBorder.IsVisible = true;
                    return;
                }

                if (_paintedWords.Count > 0)
                {
                    var handler = SimpleServiceProvider.Current.GetInstance<IOcrResultHandler>(true);
                    if (handler != null)
                    {
                        string text = HebrewOcrCorrectionHelper.BuildVisualSelectionText(_paintedWords);
                        await handler.HandleOcrResult(text).ConfigureAwait(false);
                    }
                    TryPostToUi(Close);
                }
                else
                {
                    if (_instructionBorder != null) _instructionBorder.IsVisible = true;
                }
                return;
            }

            if (!isIntentionalDrag)
            {
                CheckMagneticSnap(_startPoint);
                if (_isWindowSnapActive && !_snappedRect.IsEmpty)
                {
                    StartCaptureAndClose(_snappedRect);
                    return;
                }
            }

            if (_isWindowSnapActive && _snappedRect.Width > 0 && isIntentionalDrag) { StartCaptureAndClose(_snappedRect); return; }
            if (isIntentionalDrag)
            {
                double scaling = Scaling;
                var captureRect = RECT.FromXYWH((int)(Canvas.GetLeft(_rubberband) * scaling + Position.X), (int)(Canvas.GetTop(_rubberband) * scaling + Position.Y), (int)(_rubberband.Width * scaling), (int)(_rubberband.Height * scaling));
                if (captureRect.Width > 2 && captureRect.Height > 2) { StartCaptureAndClose(captureRect); }
            }
        }

        private void OnPointerReleased(object sender, PointerReleasedEventArgs e) { _ = OnPointerReleasedAsync(e); }

        private void StartCaptureAndClose(RECT rect)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return;
            _ = CaptureAfterOverlaysHiddenAsync(rect);
        }

        private static async Task CloseAllCaptureOverlaysAsync()
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                List<CaptureWindow> toClose;
                lock (_activeWindows)
                {
                    toClose = _activeWindows.ToList();
                    _activeWindows.Clear();
                }

                foreach (var window in toClose)
                {
                    window.CancelLocalPreemptiveOcr();
                    window._isWindowOpen = false;
                    window.Close();
                }
            });

            await Task.Delay(100).ConfigureAwait(false);
        }

        private static async Task CaptureAfterOverlaysHiddenAsync(RECT rect)
        {
            App.ForceRedTrayIcon(true);
            ImageSharpImage owned = null;
            try
            {
                var nativeRect = RECT.FromXYWH(rect.X, rect.Y, rect.Width, rect.Height);
                ImageSharpImage frozenCaptured = CaptureHelper.GetFrozenSnapshot(nativeRect);

                await CloseAllCaptureOverlaysAsync().ConfigureAwait(false);
                if (rect.Width <= 0 || rect.Height <= 0)
                {
                    frozenCaptured?.Dispose();
                    CaptureHelper.ClearFrozenSnapshot();
                    App.ForceRedTrayIcon(false);
                    return;
                }

                CaptureHelper.RememberRegion(rect);
                owned = await Task.Run(() =>
                {
                    ImageSharpImage captured = frozenCaptured;
                    if (captured == null) captured = NativeCapture.CaptureRegion(nativeRect);
                    if (captured == null) return null;
                    var clone = captured.Clone(x => { });
                    if (captured != null) captured.Dispose();

                    if (IniConfig.GetIniSection<CoreConfiguration>().KeepBackup)
                    {
                        try
                        {
                            string tempDir = Path.Combine(Path.GetTempPath(), "SnapVox");
                            Directory.CreateDirectory(tempDir);
                            string fileName = $"Raw_{DateTime.Now:yyyy-MM-dd_HH-mm-ss_fff}.jpg";
                            clone.Save(Path.Combine(tempDir, fileName), new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = snapvox.foundation.IniFile.IniConfig.GetIniSection<CoreConfiguration>().OutputFileJpegQuality });
                        }
                        catch { }
                    }
                    return clone;
                }).ConfigureAwait(false);

                if (owned == null) 
                {
                    CaptureHelper.ClearFrozenSnapshot();
                    App.ForceRedTrayIcon(false);
                    return;
                }

                await UiClipboard.SetImageAsync(owned).ConfigureAwait(false);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ShowEditorForOwnedImage(owned, rect);
                    owned = null;
                    CaptureHelper.ClearFrozenSnapshot();
                    App.ForceRedTrayIcon(false);
                });
            }
            catch (Exception ex)
            {
                owned?.Dispose();
                CaptureHelper.ClearFrozenSnapshot();
                App.ForceRedTrayIcon(false);
                Log.Fatal("CaptureAfterOverlaysHiddenAsync failed.", ex);
            }
        }

        private static void ShowEditorForOwnedImage(ImageSharpImage image, RECT rect)
        {
            ImageSharpImage imageForEditor = image;
            snapvox.editor.forms.ImageEditorWindow editor = null;
            try
            {
                editor = new snapvox.editor.forms.ImageEditorWindow();
                editor.SetImage(imageForEditor, rect);
                imageForEditor = null;
                editor.Show();
            }
            catch
            {
                imageForEditor?.Dispose();
                editor?.Close();
                throw;
            }
        }

        private void CheckMagneticSnap(Avalonia.Point pos)
        {
            double scaling = Scaling;
            var absolutePos = new POINT((int)(pos.X * scaling) + Position.X, (int)(pos.Y * scaling) + Position.Y);
            RECT windowRect = Win32WindowHelper.GetRootWindowRect(absolutePos);
            if (windowRect.IsEmpty || windowRect.Width <= 0 || windowRect.Height <= 0)
            {
                _snappedRect = RECT.Empty;
                _isWindowSnapActive = false;
                if (_highlightBorder != null) _highlightBorder.IsVisible = false;
                if (_windowSnapBadge != null) _windowSnapBadge.IsVisible = false;
                Cursor = new Cursor(StandardCursorType.Cross);
                return;
            }
            _snappedRect = windowRect;
            _isWindowSnapActive = true;
            Cursor = new Cursor(StandardCursorType.Cross);
            if (_highlightBorder != null)
            {
                _highlightBorder.IsVisible = true;
                Canvas.SetLeft(_highlightBorder, (windowRect.Left - Position.X) / scaling);
                Canvas.SetTop(_highlightBorder, (windowRect.Top - Position.Y) / scaling);
                _highlightBorder.Width = windowRect.Width / scaling;
                _highlightBorder.Height = windowRect.Height / scaling;
            }
            ShowWindowSnapBadge((windowRect.Left - Position.X) / scaling, (windowRect.Top - Position.Y) / scaling);
        }

        private void ShowWindowSnapBadge(double left, double top)
        {
            if (_windowSnapBadge == null) return;
            _windowSnapBadge.IsVisible = true;
            Canvas.SetLeft(_windowSnapBadge, left + 8);
            Canvas.SetTop(_windowSnapBadge, Math.Max(8, top + 8));
        }

        private void PaintWords(Avalonia.Point pos)
        {
            OcrInformation scan;
            OcrWordSpatialIndex index;
            lock (_ocrStateLock) { scan = _ocrScanResult; index = _ocrWordIndex; }
            if (scan?.Words == null)
            {
                if (_ocrProcessingStatus != null) _ocrProcessingStatus.IsVisible = true;
                return;
            }
            double scaling = Scaling;
            var absolutePos = new POINT((int)(pos.X * scaling) + Position.X, (int)(pos.Y * scaling) + Position.Y);
            var word = index?.FindContaining(absolutePos);
            if (word == null)
            {
                return;
            }

            if (!_paintedWords.Add(word)) return;
            HideOcrHoverPreview();
            var highlight = new Border { Background = new SolidColorBrush(AvaloniaColor.FromArgb(102, 255, 255, 0)), Width = word.Bounds.Width / scaling, Height = word.Bounds.Height / scaling, IsHitTestVisible = false };
            Canvas.SetLeft(highlight, (word.Bounds.Left - Position.X) / scaling);
            Canvas.SetTop(highlight, (word.Bounds.Top - Position.Y) / scaling);
            _mainCanvas.Children.Add(highlight);
            _highlightRects.Add(highlight);
        }

        private void ShowOcrHoverPreview(Avalonia.Point pos)
        {
            OcrInformation scan;
            OcrWordSpatialIndex index;
            lock (_ocrStateLock) { scan = _ocrScanResult; index = _ocrWordIndex; }
            if (scan?.Words == null)
            {
                HideOcrHoverPreview();
                return;
            }

            double scaling = Scaling;
            var absolutePos = new POINT((int)(pos.X * scaling) + Position.X, (int)(pos.Y * scaling) + Position.Y);
            var word = index?.FindContaining(absolutePos);
            if (word == null)
            {
                HideOcrHoverPreview();
                return;
            }

            if (_ocrHoverPreview == null)
            {
                _ocrHoverPreview = new Border
                {
                    Background = new SolidColorBrush(AvaloniaColor.FromArgb(42, 0, 120, 255)),
                    BorderBrush = Brushes.DeepSkyBlue,
                    BorderThickness = new Thickness(1),
                    IsHitTestVisible = false
                };
                _mainCanvas.Children.Add(_ocrHoverPreview);
            }

            _ocrHoverPreview.Width = Math.Max(1, word.Bounds.Width / scaling);
            _ocrHoverPreview.Height = Math.Max(1, word.Bounds.Height / scaling);
            Canvas.SetLeft(_ocrHoverPreview, (word.Bounds.Left - Position.X) / scaling);
            Canvas.SetTop(_ocrHoverPreview, (word.Bounds.Top - Position.Y) / scaling);
            _ocrHoverPreview.IsVisible = true;
        }

        private void HideOcrHoverPreview()
        {
            if (_ocrHoverPreview != null)
            {
                _ocrHoverPreview.IsVisible = false;
            }
        }

        private void ClearHighlights() { HideOcrHoverPreview(); foreach (var rect in _highlightRects) { _mainCanvas.Children.Remove(rect); } _highlightRects.Clear(); }
    }
}
