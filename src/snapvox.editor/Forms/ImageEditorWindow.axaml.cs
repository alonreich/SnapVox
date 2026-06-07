using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Brushes = Avalonia.Media.Brushes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using snapvox.native.foundation;
using snapvox.foundation.core;
using snapvox.foundation.core.AvaloniaShims;
using snapvox.foundation.interfaces.Ocr;
using snapvox.editor.helpers;
using snapvox.foundation.IniFile;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ImageSharpImage = SixLabors.ImageSharp.Image;
using AvaloniaPoint = Avalonia.Point;
using AvaloniaColor = Avalonia.Media.Color;
using AvaloniaControl = Avalonia.Controls.Control;
using AvaloniaSize = Avalonia.Size;

namespace snapvox.editor.forms
{
    public enum EditorTool
    {
        None, Arrow, Text, FreeDraw, Highlight, Blur, Counter, Line, Emoji, Rectangle, Ellipse, Crop
    }

    public partial class ImageEditorWindow : Window
    {
        private ImageSharpImage _image;
        private Bitmap _displayBitmap;
        private static int _cascadeOffset = 0;
        private static readonly log4net.ILog Log = LogHelper.GetLogger(typeof(ImageEditorWindow));
        
        private Dictionary<EditorTool, IBrush> _toolBrushes = new Dictionary<EditorTool, IBrush>();
        private Dictionary<EditorTool, double> _toolFontSizes = new Dictionary<EditorTool, double>();
        private Dictionary<EditorTool, double> _toolThicknesses = new Dictionary<EditorTool, double>();
        private Dictionary<EditorTool, bool> _toolFillModes = new Dictionary<EditorTool, bool>();
        private double _currentThickness = 3.0;
        private bool _isFillMode = false;
        private AvaloniaColor _lastSelectedColor = AvaloniaColor.Parse("#007ACC");
        
        private static readonly IBrush HighlightBrushBase = new SolidColorBrush(AvaloniaColor.Parse("#50FFD700"));
        private EditorTool _currentTool = EditorTool.None;
        private AvaloniaPoint _startPoint;
        private bool _isDrawing;
        private Canvas _canvas;
        private Avalonia.Controls.Image _imageControl;
        private TextBlock _dimensionText;
        private TextBlock _titleDimensionText;
        private TextBlock _modeText;
        private Button _undoBtn;
        private Button _redoBtn;
        private Button _deleteBtn;
        private int _counterValue = 1;
        private string _pendingEmoji = "😊";
        private readonly LinkedList<EditorSnapshot> _undoStack = new LinkedList<EditorSnapshot>();
        private readonly LinkedList<EditorSnapshot> _redoStack = new LinkedList<EditorSnapshot>();
        private const int MaxStackSize = 40;
        private Avalonia.Controls.Shapes.Polyline _activePolyline;
        private double _zoomFactor = 1.0;

        private Avalonia.Controls.Shapes.Rectangle _selectionIndicator;
        private List<Avalonia.Controls.Shapes.Rectangle> _resizeHandles = new List<Avalonia.Controls.Shapes.Rectangle>();
        private AvaloniaControl _selectedControl;
        private AvaloniaPoint _dragLastPoint;
        private bool _isDraggingSelected;
        private bool _isResizing;
        private int _resizeHandleIndex = -1;
        private bool _dragUndoCaptured;
        private bool _resizeUndoCaptured;
        private bool _isBusy;
        private AvaloniaControl _previewControl;
        private bool _showFirstRunHints;

        private StackPanel _contextToolbar;
        private Canvas _startHandle;
        private Canvas _endHandle;
        private bool _isDraggingStartPoint;
        private bool _isDraggingEndPoint;

        private sealed class EditorSnapshot
        {
            public ImageSharpImage Image { get; init; }
            public List<AvaloniaControl> Annotations { get; init; } = new List<AvaloniaControl>();
        }

        private Slider _pixelateSlider;
        private int _lastPixelateStrength = 30;

        public ImageEditorWindow()
        {
            InitializeComponent();
            _canvas = this.FindControl<Canvas>("AnnotationCanvas");
            _imageControl = this.FindControl<Avalonia.Controls.Image>("EditorImage");
            _dimensionText = this.FindControl<TextBlock>("DimensionText");
            _titleDimensionText = this.FindControl<TextBlock>("TitleDimensionText");
            _modeText = this.FindControl<TextBlock>("ModeText");
            _undoBtn = this.FindControl<Button>("UndoButton");
            _redoBtn = this.FindControl<Button>("RedoButton");
            _deleteBtn = this.FindControl<Button>("DeleteButton");
            _contextToolbar = this.FindControl<StackPanel>("ContextToolbar");
            _startHandle = this.FindControl<Canvas>("StartPointHandle");
            _endHandle = this.FindControl<Canvas>("EndPointHandle");
            _pixelateSlider = this.FindControl<Slider>("PixelateSlider");

            var config = IniConfig.GetIniSection<CoreConfiguration>();
            _showFirstRunHints = config.IsFirstLaunch;
            if (_showFirstRunHints)
            {
                config.IsFirstLaunch = false;
                IniConfig.Save();
            }

            _lastPixelateStrength = Math.Max(5, config.LastPixelateStrength);
            if (_pixelateSlider != null) 
            {
                _pixelateSlider.Value = _lastPixelateStrength;
                _pixelateSlider.PropertyChanged += OnPixelateSliderChanged;
            }

            InitializeToolDefaults();
            ApplyFirstRunHints(_showFirstRunHints);
            UpdateModeStatus();
            UpdateContextToolbarHotkeyTooltips();
            RefreshColorPresetsPanel();

            Closed += OnWindowClosed;
            Opened += OnWindowOpened;
            Closing += OnWindowClosing;
            this.AddHandler(InputElement.PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel);
            KeyDown += OnWindowKeyDown;
            Title = "SnapVox Editor";

            _selectionIndicator = new Avalonia.Controls.Shapes.Rectangle { 
                Classes = { "selection-indicator" },
                IsVisible = false,
                IsHitTestVisible = false,
                ZIndex = 9999
            };
            _canvas.Children.Add(_selectionIndicator);

            for (int i = 0; i < 4; i++)
            {
                var handle = new Avalonia.Controls.Shapes.Rectangle {
                    Width = 14, Height = 14, Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1, 
                    IsVisible = false, ZIndex = 10000, Tag = i
                };
                
                if (i == 0 || i == 2) handle.Cursor = new Avalonia.Input.Cursor(StandardCursorType.TopLeftCorner); 
                else handle.Cursor = new Avalonia.Input.Cursor(StandardCursorType.TopRightCorner); 
                
                handle.PointerPressed += (s, e) => { 
                    _isResizing = true; 
                    _resizeUndoCaptured = false; 
                    _resizeHandleIndex = (int)((AvaloniaControl)s).Tag; 
                    _dragLastPoint = e.GetPosition(_canvas); 
                    e.Pointer.Capture(_canvas);
                    e.Handled = true; 
                };
                _resizeHandles.Add(handle); _canvas.Children.Add(handle);
            }

            _startHandle.PointerPressed += (s, e) => { 
                _isDraggingStartPoint = true; 
                _dragUndoCaptured = false; 
                _dragLastPoint = e.GetPosition(_canvas); 
                e.Pointer.Capture(_canvas);
                e.Handled = true; 
            };
            _endHandle.PointerPressed += (s, e) => { 
                _isDraggingEndPoint = true; 
                _dragUndoCaptured = false; 
                _dragLastPoint = e.GetPosition(_canvas); 
                e.Pointer.Capture(_canvas);
                e.Handled = true; 
            };
        }

        private void InitializeToolDefaults()
        {
            var config = IniConfig.GetIniSection<CoreConfiguration>();
            var defaultBrush = new SolidColorBrush(AvaloniaColor.Parse("#007ACC"));
            var redBrush = Brushes.Red;

            foreach (EditorTool tool in Enum.GetValues<EditorTool>())
            {
                _toolBrushes[tool] = defaultBrush;
                _toolFontSizes[tool] = 18.0;
                _toolThicknesses[tool] = 3.0;
                _toolFillModes[tool] = false;
            }
            
            // Mandate: Counters are smaller and Red by default OOB
            _toolBrushes[EditorTool.Counter] = redBrush;
            _toolFontSizes[EditorTool.Counter] = 35.0; // Slightly smaller than before

            _toolBrushes[EditorTool.Highlight] = HighlightBrushBase;
            _toolThicknesses[EditorTool.Highlight] = 1.0;
            _toolThicknesses[EditorTool.Arrow] = 8.0;
            _toolThicknesses[EditorTool.Line] = 8.0;
            
            _toolFontSizes[EditorTool.Text] = Math.Max(20, config.LastTextSize);
            _toolFontSizes[EditorTool.Emoji] = Math.Max(60, config.LastEmojiSize);
            _toolFontSizes[EditorTool.Counter] = config.LastCounterSize > 0 ? config.LastCounterSize : 35.0;

            if (_toolBrushes.TryGetValue(EditorTool.Rectangle, out var b) && b is SolidColorBrush initialBrush)
            {
                UpdateCurrentColorDisplay(initialBrush.Color);
            }

            UpdateFillToggleVisual();
        }

        private void ApplyFirstRunHints(bool show)
        {
            var saveHint = this.FindControl<TextBlock>("SaveHintLabel");
            if (saveHint != null) saveHint.IsVisible = show;

            var copyHint = this.FindControl<TextBlock>("CopyHintLabel");
            if (copyHint != null) copyHint.IsVisible = show;

            var saveButton = this.FindControl<Button>("DownloadButton");
            if (saveButton != null)
            {
                saveButton.Width = show ? 76 : 40;
                saveButton.Padding = show ? new Thickness(6, 0) : new Thickness(0);
            }

            var copyButton = this.FindControl<Button>("CopyButton");
            if (copyButton != null)
            {
                copyButton.Width = show ? 78 : 40;
                copyButton.Padding = show ? new Thickness(6, 0) : new Thickness(0);
            }
        }

        private void OnPixelateSliderChanged(object sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property.Name == "Value" && _pixelateSlider != null)
            {
                int val = (int)_pixelateSlider.Value;
                _lastPixelateStrength = val;
                var config = IniConfig.GetIniSection<CoreConfiguration>();
                config.LastPixelateStrength = val;
                
                if (_selectedControl != null && _selectedControl.Tag?.ToString() == "Pixelate")
                {
                    // Update live if selected
                    UpdateSelectionIndicator();
                }
            }
        }

        private bool MatchesHotkey(KeyEventArgs e, string configHotkey)
        {
            if (string.IsNullOrWhiteSpace(configHotkey) || configHotkey == "None") return false;
            var modifiers = new System.Collections.Generic.List<string>();
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) modifiers.Add("Ctrl");
            if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) modifiers.Add("Alt");
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) modifiers.Add("Shift");
            if (e.KeyModifiers.HasFlag(KeyModifiers.Meta)) modifiers.Add("Win");
            string eString = modifiers.Count > 0 ? string.Join(" + ", modifiers) + " + " + e.Key.ToString() : e.Key.ToString();
            return string.Equals(eString, configHotkey, StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatHotkeyForTooltip(string hotkey)
        {
            if (string.IsNullOrWhiteSpace(hotkey) || hotkey == "None") return "not set";
            return string.Equals(hotkey, "Delete", StringComparison.OrdinalIgnoreCase) ? "Del" : hotkey;
        }

        private void UpdateContextToolbarHotkeyTooltips()
        {
            var config = IniConfig.GetIniSection<CoreConfiguration>();
            var duplicateButton = this.FindControl<Button>("ContextDuplicateButton");
            if (duplicateButton != null)
            {
                ToolTip.SetTip(duplicateButton, $"Duplicate ({FormatHotkeyForTooltip(config.DuplicateObjectHotkey)})");
            }

            var deleteButton = this.FindControl<Button>("ContextDeleteButton");
            if (deleteButton != null)
            {
                ToolTip.SetTip(deleteButton, $"Delete ({FormatHotkeyForTooltip(config.DeleteObjectHotkey)})");
            }
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            var config = IniConfig.GetIniSection<CoreConfiguration>();
            
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                if (e.Key == Key.Z) { OnUndoClick(null, null); e.Handled = true; return; }
                if (e.Key == Key.Y) { OnRedoClick(null, null); e.Handled = true; return; }
                if (e.Key == Key.C) { OnCopyClick(null, null); e.Handled = true; return; }
                if (e.Key == Key.S) { OnDownloadClick(null, null); e.Handled = true; return; }
            }

            var focusManager = this.FocusManager;
            if (focusManager?.GetFocusedElement() is TextBox)
            {
                if (e.Key == Key.Escape)
                {
                    ResetToolsAndSelection();
                    _canvas.Focus();
                    e.Handled = true;
                }
                return;
            }

            if (e.Key == Key.Escape) { ResetToolsAndSelection(); e.Handled = true; return; }
            if (_selectedControl != null && MatchesHotkey(e, config.DeleteObjectHotkey)) { OnDeleteClick(null, null); e.Handled = true; return; }
            if (_selectedControl != null && MatchesHotkey(e, config.DuplicateObjectHotkey)) { OnDuplicateClick(null, null); e.Handled = true; return; }

            // Mandated Default Hotkeys (I, A, L, D, H)
            if (e.Key == Key.I) { TriggerToolButton("CounterTool"); e.Handled = true; return; }
            if (e.Key == Key.A) { TriggerToolButton("ArrowTool"); e.Handled = true; return; }
            if (e.Key == Key.L) { TriggerToolButton("LineTool"); e.Handled = true; return; }
            if (e.Key == Key.D) { TriggerToolButton("FreeDrawTool"); e.Handled = true; return; }
            if (e.Key == Key.H) { TriggerToolButton("HighlightTool"); e.Handled = true; return; }

            if (MatchesHotkey(e, config.RotateCcwHotkey)) OnRotateCcwClick(null, null);
            else if (MatchesHotkey(e, config.RotateCwHotkey)) OnRotateCwClick(null, null);
            else if (MatchesHotkey(e, config.TextHotkey)) TriggerToolButton("TextTool");
            else if (MatchesHotkey(e, config.ResizeHotkey)) OnResizeClick(null, null);
            else if (MatchesHotkey(e, config.EmojiHotkey)) TriggerToolButton("EmojiTool");
            else if (MatchesHotkey(e, config.PixelateHotkey1) || MatchesHotkey(e, config.PixelateHotkey2)) TriggerToolButton("BlurTool");
        }

        private void ResetToolsAndSelection()
        {
            _currentTool = EditorTool.None;
            _selectedControl = null;
            RemovePreviewShape();
            UpdateSelectionIndicator();
            UpdateThicknessPanelVisibility();
            Cursor = new Avalonia.Input.Cursor(StandardCursorType.Arrow);
            var toolBar = this.FindControl<StackPanel>("LeftToolBar");
            if (toolBar != null) { foreach (var child in toolBar.Children) { if (child is Button b) b.Classes.Remove("selected"); } }
            this.FindControl<Button>("CropTool")?.Classes.Remove("selected");
            _isFillMode = false;
            UpdateFillToggleVisual();
            UpdateModeStatus();
        }
        
        private void TriggerToolButton(string name)
        {
            var btn = this.FindControl<Button>(name);
            if (btn != null && btn.IsEnabled) OnToolClick(btn, new RoutedEventArgs());
        }

        private void OnPointerWheelChanged(object sender, PointerWheelEventArgs e)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                var scrollViewer = this.FindControl<ScrollViewer>("EditorScrollViewer");
                if (scrollViewer == null) return;

                double oldZoom = _zoomFactor;
                if (e.Delta.Y > 0) _zoomFactor = Math.Min(4.0, _zoomFactor + 0.1);
                else _zoomFactor = Math.Max(0.1, _zoomFactor - 0.1);

                if (Math.Abs(oldZoom - _zoomFactor) < 0.01) return;

                var pos = e.GetPosition(_canvas);
                ApplyZoom();

                double ratio = _zoomFactor / oldZoom;
                scrollViewer.Offset = new Avalonia.Vector((scrollViewer.Offset.X + pos.X) * ratio - pos.X, (scrollViewer.Offset.Y + pos.Y) * ratio - pos.Y);
                e.Handled = true;
            }
        }

        private void ApplyZoom()
        {
            if (_imageControl != null && _canvas != null && _image != null)
            {
                _imageControl.Width = _image.Width;
                _imageControl.Height = _image.Height;
                _canvas.Width = _image.Width;
                _canvas.Height = _image.Height;

                _imageControl.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
                _canvas.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
                _imageControl.RenderTransform = new ScaleTransform(_zoomFactor, _zoomFactor);
                _canvas.RenderTransform = new ScaleTransform(_zoomFactor, _zoomFactor);

                var snipBorder = this.FindControl<Border>("SnipBorder");
                if (snipBorder != null)
                {
                    snipBorder.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
                    snipBorder.RenderTransform = new ScaleTransform(_zoomFactor, _zoomFactor);
                }

                var zoomText = this.FindControl<TextBlock>("ZoomText");
                if (zoomText != null) zoomText.Text = $"{(int)(_zoomFactor * 100)}%";
            }
        }

        private bool _forceClose = false;
        private bool _isClosingPromptOpen = false;
        private void OnWindowClosing(object sender, WindowClosingEventArgs e)
        {
            var config = IniConfig.GetIniSection<CoreConfiguration>();
            if (_forceClose || _undoStack.Count == 0 || !config.WarnBeforeClosingEditor) return;
            
            if (!_isClosingPromptOpen)
            {
                e.Cancel = true;
                _isClosingPromptOpen = true;
                var prompt = this.FindControl<Grid>("ClosePromptOverlay");
                if (prompt != null) prompt.IsVisible = true;
            }
            else
            {
                e.Cancel = true;
            }
        }

        private void OnPromptSaveClose(object sender, RoutedEventArgs e)
        {
            var prompt = this.FindControl<Grid>("ClosePromptOverlay");
            if (prompt != null) prompt.IsVisible = false;
            OnDownloadClick(null, null);
        }

        private void OnPromptDiscard(object sender, RoutedEventArgs e)
        {
            _forceClose = true;
            Close();
        }

        private void OnPromptCancel(object sender, RoutedEventArgs e)
        {
            _isClosingPromptOpen = false;
            var prompt = this.FindControl<Grid>("ClosePromptOverlay");
            if (prompt != null) prompt.IsVisible = false;
        }

        private void OnWindowOpened(object sender, EventArgs e) { UiClipboard.Register(text => Clipboard?.SetTextAsync(text) ?? Task.CompletedTask); }
        private void OnWindowClosed(object sender, EventArgs e) { ReleaseImageResources(); }

        public void SetImage(ImageSharpImage image, RECT captureRect)
        {
            ReleaseImageResources();
            _image = image ?? throw new ArgumentNullException(nameof(image));
            UpdateDisplay();
            PositionWindow(captureRect);
        }

        private void UpdateDisplay()
        {
            if (_image == null) return;
            _displayBitmap?.Dispose();
            _displayBitmap = _image.ToAvaloniaBitmap();
            if (_imageControl != null) { _imageControl.Source = _displayBitmap; _imageControl.Width = _image.Width; _imageControl.Height = _image.Height; }
            if (_canvas != null) { _canvas.Width = _image.Width; _canvas.Height = _image.Height; }
            ApplyZoom();
            string dim = $"{_image.Width} x {_image.Height}";
            if (_titleDimensionText != null) _titleDimensionText.Text = dim;
            UpdateUndoRedoState();
        }

        private void UpdateUndoRedoState()
        {
            if (_undoBtn != null) _undoBtn.IsEnabled = _undoStack.Count > 0;
            if (_redoBtn != null) _redoBtn.IsEnabled = _redoStack.Count > 0;
            if (_undoBtn != null) ToolTip.SetTip(_undoBtn, _undoStack.Count > 0 ? "Undo available (Ctrl+Z)" : "Undo (Ctrl+Z)");
            if (_redoBtn != null) ToolTip.SetTip(_redoBtn, _redoStack.Count > 0 ? "Redo available (Ctrl+Y)" : "Redo (Ctrl+Y)");
        }

        private static string GetToolDisplayName(EditorTool tool) => tool switch
        {
            EditorTool.None => "Move",
            EditorTool.FreeDraw => "Free draw",
            EditorTool.Blur => "Pixelate",
            EditorTool.Rectangle => "Rectangle",
            EditorTool.Ellipse => "Circle",
            EditorTool.Crop => "Crop",
            EditorTool.Text => "Text",
            EditorTool.Counter => "Counter",
            EditorTool.Emoji => "Emoji",
            _ => tool.ToString()
        };

        private void UpdateModeStatus()
        {
            if (_modeText == null) return;

            if (_selectedControl != null)
            {
                var selectedTool = GetToolFromControl(_selectedControl);
                _modeText.Text = selectedTool == EditorTool.None
                    ? "Object selected"
                    : $"{GetToolDisplayName(selectedTool)} selected";
                return;
            }

            _modeText.Text = _currentTool == EditorTool.None
                ? "Move mode"
                : $"{GetToolDisplayName(_currentTool)} tool";
        }

        private bool IsEditorChrome(AvaloniaControl control)
        {
            return ReferenceEquals(control, _selectionIndicator) || _resizeHandles.Contains(control) || ReferenceEquals(control, _previewControl) || ReferenceEquals(control, _contextToolbar) || ReferenceEquals(control, _startHandle) || ReferenceEquals(control, _endHandle);
        }

        private List<AvaloniaControl> GetUserAnnotations()
        {
            return _canvas.Children.OfType<AvaloniaControl>().Where(c => !IsEditorChrome(c)).ToList();
        }

        private List<(AvaloniaControl Control, bool WasVisible)> SetEditorChromeVisible(bool visible)
        {
            var chrome = new List<AvaloniaControl>();
            if (_selectionIndicator != null) chrome.Add(_selectionIndicator);
            chrome.AddRange(_resizeHandles);
            if (_contextToolbar != null) chrome.Add(_contextToolbar);
            if (_startHandle != null) chrome.Add(_startHandle);
            if (_endHandle != null) chrome.Add(_endHandle);

            var state = new List<(AvaloniaControl Control, bool WasVisible)>();
            foreach (var control in chrome)
            {
                state.Add((control, control.IsVisible));
                control.IsVisible = visible && control.IsVisible;
            }
            return state;
        }

        private void RestoreVisibility(List<(AvaloniaControl Control, bool WasVisible)> state)
        {
            if (state == null) return;
            foreach (var item in state) item.Control.IsVisible = item.WasVisible;
        }

        private void RemoveUserAnnotations()
        {
            foreach (var child in GetUserAnnotations()) _canvas.Children.Remove(child);
        }

        private void SaveUndoState()
        {
            _undoStack.AddLast(CreateSnapshot());
            if (_undoStack.Count > MaxStackSize)
            {
                var oldest = _undoStack.First.Value;
                _undoStack.RemoveFirst();
                DisposeSnapshot(oldest);
            }
            ClearSnapshotStack(_redoStack);
            UpdateUndoRedoState();
        }

        private void ShowUndoAvailableHint()
        {
            if (_undoStack.Count > 0)
            {
                OverlayHelper.ShowLightToast("Press Ctrl+Z to undo", this);
            }
        }

        private EditorSnapshot CreateSnapshot()
        {
            return new EditorSnapshot
            {
                Image = _image?.Clone(x => { }),
                Annotations = GetUserAnnotations().Select(CloneAnnotationControl).Where(c => c != null).ToList()
            };
        }

        private void RestoreSnapshot(EditorSnapshot snapshot)
        {
            if (snapshot == null) return;

            RemoveUserAnnotations();
            _selectedControl = null;
            _isDraggingSelected = false;
            _isResizing = false;
            _activePolyline = null;

            _image?.Dispose();
            _image = snapshot.Image;
            foreach (var annotation in snapshot.Annotations) _canvas.Children.Add(annotation);

            UpdateDisplay();
            UpdateSelectionIndicator();
            UpdateThicknessPanelVisibility();
        }

        private void ClearSnapshotStack(LinkedList<EditorSnapshot> stack)
        {
            foreach (var snapshot in stack) DisposeSnapshot(snapshot);
            stack.Clear();
        }

        private void DisposeSnapshot(EditorSnapshot snapshot)
        {
            snapshot?.Image?.Dispose();
        }

        private AvaloniaControl CloneAnnotationControl(AvaloniaControl source)
        {
            AvaloniaControl clone = source switch
            {
                Avalonia.Controls.Shapes.Line line => new Avalonia.Controls.Shapes.Line
                {
                    StartPoint = line.StartPoint,
                    EndPoint = line.EndPoint,
                    Stroke = line.Stroke,
                    StrokeThickness = line.StrokeThickness,
                    IsHitTestVisible = line.IsHitTestVisible
                },
                Avalonia.Controls.Shapes.Polyline polyline => new Avalonia.Controls.Shapes.Polyline
                {
                    Points = new List<AvaloniaPoint>(polyline.Points),
                    Stroke = polyline.Stroke,
                    StrokeThickness = polyline.StrokeThickness,
                    IsHitTestVisible = polyline.IsHitTestVisible
                },
                Avalonia.Controls.Shapes.Rectangle rectangle => new Avalonia.Controls.Shapes.Rectangle
                {
                    Width = rectangle.Width,
                    Height = rectangle.Height,
                    Fill = rectangle.Fill,
                    Stroke = rectangle.Stroke,
                    StrokeThickness = rectangle.StrokeThickness,
                    Tag = rectangle.Tag,
                    IsHitTestVisible = rectangle.IsHitTestVisible
                },
                Avalonia.Controls.Shapes.Ellipse ellipse => new Avalonia.Controls.Shapes.Ellipse
                {
                    Width = ellipse.Width,
                    Height = ellipse.Height,
                    Fill = ellipse.Fill,
                    Stroke = ellipse.Stroke,
                    StrokeThickness = ellipse.StrokeThickness,
                    Tag = ellipse.Tag,
                    IsHitTestVisible = ellipse.IsHitTestVisible
                },
                Avalonia.Controls.Shapes.Polygon polygon => new Avalonia.Controls.Shapes.Polygon
                {
                    Points = new List<AvaloniaPoint>(polygon.Points),
                    Fill = polygon.Fill,
                    Stroke = polygon.Stroke,
                    StrokeThickness = polygon.StrokeThickness,
                    IsHitTestVisible = polygon.IsHitTestVisible
                },
                Canvas canvas => CloneAnnotationCanvas(canvas),
                Border border => CloneAnnotationBorder(border),
                TextBlock textBlock => new TextBlock
                {
                    Text = textBlock.Text,
                    Foreground = textBlock.Foreground,
                    FontSize = textBlock.FontSize,
                    FontWeight = textBlock.FontWeight,
                    FontFamily = textBlock.FontFamily,
                    Width = textBlock.Width,
                    Height = textBlock.Height,
                    HorizontalAlignment = textBlock.HorizontalAlignment,
                    VerticalAlignment = textBlock.VerticalAlignment,
                    IsHitTestVisible = textBlock.IsHitTestVisible
                },
                TextBox textBox => CloneTextBox(textBox),
                _ => null
            };

            if (clone == null) return null;
            CopyCommonState(source, clone);
            return clone;
        }

        private Canvas CloneAnnotationCanvas(Canvas source)
        {
            var clone = new Canvas
            {
                Width = source.Width,
                Height = source.Height,
                Background = source.Background,
                IsHitTestVisible = source.IsHitTestVisible
            };

            foreach (var child in source.Children.OfType<AvaloniaControl>())
            {
                var childClone = CloneAnnotationControl(child);
                if (childClone != null) clone.Children.Add(childClone);
            }
            return clone;
        }

        private Border CloneAnnotationBorder(Border source)
        {
            var clone = new Border
            {
                Width = source.Width,
                Height = source.Height,
                Background = source.Background,
                BorderBrush = source.BorderBrush,
                BorderThickness = source.BorderThickness,
                CornerRadius = source.CornerRadius,
                Padding = source.Padding,
                IsHitTestVisible = source.IsHitTestVisible
            };

            if (source.Child is AvaloniaControl child)
            {
                clone.Child = CloneAnnotationControl(child);
                if (clone.Child is TextBox textBox)
                {
                    AttachTextBoxBehavior(clone, textBox, IniConfig.GetIniSection<CoreConfiguration>());
                }
            }

            return clone;
        }

        private TextBox CloneTextBox(TextBox source)
        {
            return new TextBox
            {
                Text = source.Text,
                Watermark = source.Watermark,
                Foreground = source.Foreground,
                CaretBrush = source.CaretBrush,
                Background = source.Background,
                BorderThickness = source.BorderThickness,
                FontSize = source.FontSize,
                FontWeight = source.FontWeight,
                Padding = source.Padding,
                AcceptsReturn = source.AcceptsReturn,
                MinWidth = source.MinWidth,
                Width = source.Width,
                Height = source.Height,
                IsHitTestVisible = source.IsHitTestVisible,
                TextAlignment = source.TextAlignment,
                VerticalContentAlignment = source.VerticalContentAlignment
            };
        }

        private void AttachTextBoxBehavior(Border border, TextBox textBox, CoreConfiguration config)
        {
            textBox.GotFocus += (s, e) => { border.BorderThickness = new Thickness(config.UseTextFrame ? 2 : 1); };
            textBox.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(textBox.Text)) _canvas.Children.Remove(border);
                else {
                    border.BorderThickness = new Thickness(config.UseTextFrame ? 2 : 0);
                    textBox.IsReadOnly = true;
                    textBox.CaretBrush = Brushes.Transparent;
                    textBox.Cursor = new Avalonia.Input.Cursor(StandardCursorType.SizeAll);
                }
            };
            textBox.TextChanged += (s, e) => {
                string text = textBox.Text ?? "";
                if (!string.IsNullOrEmpty(text)) {
                    char first = text.FirstOrDefault(c => char.IsLetter(c));
                    if (first != default && first >= 0x0590 && first <= 0x05FF) {
                        textBox.FlowDirection = FlowDirection.RightToLeft;
                    } else {
                        textBox.FlowDirection = FlowDirection.LeftToRight;
                    }
                }
            };
            textBox.AddHandler(InputElement.PointerPressedEvent, (object s, PointerPressedEventArgs e) =>
            {
                if (e.GetCurrentPoint(textBox).Properties.IsRightButtonPressed) {
                    _canvas.Focus();
                    OnWindowKeyDown(this, new KeyEventArgs { Key = Key.Escape, RoutedEvent = InputElement.KeyDownEvent });
                    e.Handled = true;
                }
            }, RoutingStrategies.Tunnel);
            textBox.AddHandler(InputElement.KeyDownEvent, (object s, KeyEventArgs e) =>
            {
                if (e.Key == Key.Escape) { _canvas.Focus(); OnWindowKeyDown(this, new KeyEventArgs { Key = Key.Escape, RoutedEvent = InputElement.KeyDownEvent }); e.Handled = true; }
                else if (e.Key == Key.Enter)
                {
                    if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Alt) || e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                    {
                        int caret = textBox.CaretIndex;
                        string currentText = textBox.Text ?? "";
                        textBox.Text = currentText.Insert(caret, Environment.NewLine);
                        textBox.CaretIndex = caret + Environment.NewLine.Length;
                        e.Handled = true;
                    }
                    else { 
                        _canvas.Focus(); 
                        OnWindowKeyDown(this, new KeyEventArgs { Key = Key.Escape, RoutedEvent = InputElement.KeyDownEvent }); 
                        e.Handled = true; 
                    }
                }
            }, RoutingStrategies.Tunnel);
        }

        private void CopyCommonState(AvaloniaControl source, AvaloniaControl clone)
        {
            Canvas.SetLeft(clone, Canvas.GetLeft(source));
            Canvas.SetTop(clone, Canvas.GetTop(source));
            clone.Width = source.Width;
            clone.Height = source.Height;
            clone.Tag = source.Tag;
            clone.ZIndex = source.ZIndex;
            clone.RenderTransform = source.RenderTransform;
            clone.RenderTransformOrigin = source.RenderTransformOrigin;
            clone.Cursor = source.Cursor;
            foreach (var className in source.Classes)
            {
                if (string.IsNullOrEmpty(className) || className[0] == ':')
                {
                    continue;
                }

                clone.Classes.Add(className);
            }
        }

        private void PositionWindow(RECT captureRect)
        {
            var screens = Screens.All.ToList();
            var targetScreen = Screens.Primary;
            long maxArea = -1;
            foreach (var screen in screens)
            {
                var intersect = captureRect.Intersect(RECT.FromXYWH(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height));
                if (!intersect.IsEmpty) { long area = (long)intersect.Width * intersect.Height; if (area > maxArea) { maxArea = area; targetScreen = screen; } }
            }

            double scaling = targetScreen.Scaling;
            double maxAllowedWidth = (targetScreen.WorkingArea.Width - 100) / scaling;
            double maxAllowedHeight = (targetScreen.WorkingArea.Height - 160) / scaling;

            double targetWidth = ((_image.Width * _zoomFactor) + 50) / scaling;
            double targetHeight = ((_image.Height * _zoomFactor) + 50) / scaling;

            if (targetWidth < 850) targetWidth = 850;
            if (targetHeight < 650) targetHeight = 650;
            
            if (targetWidth > maxAllowedWidth) targetWidth = maxAllowedWidth;
            if (targetHeight > maxAllowedHeight) targetHeight = maxAllowedHeight;
            
            Width = targetWidth; Height = targetHeight;
            
            double centerPhysicalX = targetScreen.WorkingArea.X + (targetScreen.WorkingArea.Width - (Width * scaling)) / 2;
            double centerPhysicalY = targetScreen.WorkingArea.Y + (targetScreen.WorkingArea.Height - (Height * scaling)) / 2;
            
            int cascade = (_cascadeOffset++ % 5) * 20;
            Position = new PixelPoint((int)(centerPhysicalX + cascade), (int)(centerPhysicalY + cascade));
        }

        private void ReleaseImageResources()
        {
            RemovePreviewShape();
            _image?.Dispose();
            _image = null;
            _displayBitmap?.Dispose();
            _displayBitmap = null;
            ClearSnapshotStack(_undoStack);
            ClearSnapshotStack(_redoStack);
        }
        private void InitializeComponent() { AvaloniaXamlLoader.Load(this); }
        private void OnZoomInClick(object sender, RoutedEventArgs e) { _zoomFactor = Math.Min(4.0, _zoomFactor + 0.1); ApplyZoom(); }
        private void OnZoomOutClick(object sender, RoutedEventArgs e) { _zoomFactor = Math.Max(0.1, _zoomFactor - 0.1); ApplyZoom(); }
        private void OnZoomResetButtonClick(object sender, RoutedEventArgs e) { _zoomFactor = 1.0; ApplyZoom(); }
        private void OnResetZoomClick(object sender, PointerPressedEventArgs e) { _zoomFactor = 1.0; ApplyZoom(); e.Handled = true; }
        private void OnCounterDoubleTapped(object sender, TappedEventArgs e) { _counterValue = 1; }

        private void OnCanvasPointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (_isBusy) return;
            var pos = e.GetPosition(_canvas);
            if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed) { OnWindowKeyDown(this, new KeyEventArgs { Key = Key.Escape }); e.Handled = true; return; }

            // Mandate: Selection should be released when starting to draw a new object
            if (_currentTool != EditorTool.None)
            {
                _selectedControl = null;
                UpdateSelectionIndicator();
            }

            if (_currentTool == EditorTool.None)
            {
                double tolerance = 10.0;
                var clicked = _canvas.Children.Cast<AvaloniaControl>().LastOrDefault(c => {
                    if (c == _selectionIndicator || _resizeHandles.Contains(c) || ReferenceEquals(c, _contextToolbar) || ReferenceEquals(c, _startHandle) || ReferenceEquals(c, _endHandle) || !c.IsHitTestVisible) return false;
                    if (c is Avalonia.Controls.Shapes.Polyline pl) 
                    { 
                        double left = Canvas.GetLeft(pl); double top = Canvas.GetTop(pl);
                        if (double.IsNaN(left)) left = 0; if (double.IsNaN(top)) top = 0;
                        for (int i = 0; i < pl.Points.Count - 1; i++) { if (PointToLineDistance(pos, new AvaloniaPoint(pl.Points[i].X + left, pl.Points[i].Y + top), new AvaloniaPoint(pl.Points[i+1].X + left, pl.Points[i+1].Y + top)) < tolerance) return true; }
                    }
                    return c.Bounds.Inflate(tolerance).Contains(pos);
                });

                if (clicked != null) 
                { 
                    _selectedControl = clicked; _isDraggingSelected = true; _dragUndoCaptured = false; _dragLastPoint = pos; UpdateSelectionIndicator(); e.Handled = true; 
                    
                    IBrush existingBrush = null;
                    if (_selectedControl is Avalonia.Controls.Shapes.Shape s) { 
                        _currentThickness = s.StrokeThickness; 
                        existingBrush = s.Stroke ?? s.Fill;
                    }
                    else if (_selectedControl is Border b) existingBrush = b.BorderBrush ?? b.Background;
                    else if (_selectedControl is TextBlock tb) existingBrush = tb.Foreground;

                    if (existingBrush is SolidColorBrush scb)
                    {
                        UpdateCurrentColorDisplay(scb.Color);
                    }

                    Cursor = new Avalonia.Input.Cursor(StandardCursorType.Hand);
                }
                else { _selectedControl = null; UpdateSelectionIndicator(); Cursor = new Avalonia.Input.Cursor(StandardCursorType.Arrow); }
                
                UpdateThicknessPanelVisibility();
                return;
            }
            _isDrawing = true; _startPoint = pos; var brush = _toolBrushes[_currentTool];
            if (_currentTool == EditorTool.Counter) { PlaceCounter(_startPoint, brush); _isDrawing = false; }
            else if (_currentTool == EditorTool.Emoji) { PlaceEmoji(_startPoint, _pendingEmoji); _isDrawing = false; }
            else if (_currentTool == EditorTool.Text) { PlaceText(_startPoint, brush); _isDrawing = false; }
            else if (_currentTool == EditorTool.FreeDraw) { _activePolyline = new Avalonia.Controls.Shapes.Polyline { Stroke = brush, StrokeThickness = _currentThickness, IsHitTestVisible = true }; Canvas.SetLeft(_activePolyline, _startPoint.X); Canvas.SetTop(_activePolyline, _startPoint.Y); _activePolyline.Points.Add(new AvaloniaPoint(0, 0)); AddAnnotation(_activePolyline); }
            else BeginPreviewShape(_startPoint, brush);
        }

        private double PointToLineDistance(AvaloniaPoint p, AvaloniaPoint a, AvaloniaPoint b) { double l2 = Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2); if (l2 == 0) return Math.Sqrt(Math.Pow(p.X - a.X, 2) + Math.Pow(p.Y - a.Y, 2)); double t = ((p.X - a.X) * (b.X - a.X) + (p.Y - a.Y) * (b.Y - a.Y)) / l2; t = Math.Max(0, Math.Min(1, t)); return Math.Sqrt(Math.Pow(p.X - (a.X + t * (b.X - a.X)), 2) + Math.Pow(p.Y - (a.Y + t * (b.Y - a.Y)), 2)); }

        private void BeginPreviewShape(AvaloniaPoint start, IBrush brush)
        {
            RemovePreviewShape();
            _previewControl = _currentTool switch
            {
                EditorTool.Line => new Avalonia.Controls.Shapes.Line { Stroke = brush, StrokeThickness = _currentThickness, IsHitTestVisible = false, ZIndex = 9998 },
                EditorTool.Arrow => CreateArrowPreview(brush),
                EditorTool.Highlight => new Avalonia.Controls.Shapes.Rectangle { Fill = brush, Stroke = brush, StrokeThickness = 1, IsHitTestVisible = false, ZIndex = 9998 },
                EditorTool.Rectangle => new Avalonia.Controls.Shapes.Rectangle { Stroke = brush, StrokeThickness = _currentThickness, Fill = _toolFillModes[EditorTool.Rectangle] ? brush : Brushes.Transparent, IsHitTestVisible = false, ZIndex = 9998 },
                EditorTool.Ellipse => new Avalonia.Controls.Shapes.Ellipse { Stroke = brush, StrokeThickness = _currentThickness, Fill = _toolFillModes[EditorTool.Ellipse] ? brush : Brushes.Transparent, IsHitTestVisible = false, ZIndex = 9998 },
                EditorTool.Blur => new Avalonia.Controls.Shapes.Rectangle { Fill = new SolidColorBrush(AvaloniaColor.FromArgb(120, 100, 100, 100)), Stroke = Brushes.DeepSkyBlue, StrokeThickness = Math.Max(1, _currentThickness), IsHitTestVisible = false, ZIndex = 9998 },
                EditorTool.Crop => new Avalonia.Controls.Shapes.Rectangle { Stroke = brush, StrokeThickness = Math.Max(1, _currentThickness), Fill = Brushes.Transparent, IsHitTestVisible = false, ZIndex = 9998 },
                _ => null
            };

            if (_previewControl != null)
            {
                _canvas.Children.Add(_previewControl);
                UpdatePreviewShape(start);
            }
        }

        private Canvas CreateArrowPreview(IBrush brush)
        {
            var group = new Canvas { IsHitTestVisible = false, ZIndex = 9998 };
            group.Children.Add(new Avalonia.Controls.Shapes.Line { Stroke = brush, StrokeThickness = _currentThickness, StrokeJoin = PenLineJoin.Round, StrokeLineCap = PenLineCap.Round });
            group.Children.Add(new Avalonia.Controls.Shapes.Polygon { Fill = brush });
            return group;
        }

        private void UpdatePreviewShape(AvaloniaPoint end)
        {
            if (_previewControl == null) return;
            double minX = Math.Min(_startPoint.X, end.X);
            double minY = Math.Min(_startPoint.Y, end.Y);
            double maxX = Math.Max(_startPoint.X, end.X);
            double maxY = Math.Max(_startPoint.Y, end.Y);
            double width = Math.Max(1, maxX - minX);
            double height = Math.Max(1, maxY - minY);

            Canvas.SetLeft(_previewControl, minX);
            Canvas.SetTop(_previewControl, minY);
            _previewControl.Width = width;
            _previewControl.Height = height;

            if (_previewControl is Avalonia.Controls.Shapes.Line line)
            {
                line.StartPoint = new AvaloniaPoint(_startPoint.X - minX, _startPoint.Y - minY);
                line.EndPoint = new AvaloniaPoint(end.X - minX, end.Y - minY);
            }
            else if (_previewControl is Canvas group)
            {
                UpdateArrowPreview(group, end);
            }
        }

        private void UpdateArrowPreview(Canvas group, AvaloniaPoint end)
        {
            UpdateArrowVisuals(group, _startPoint, end);
        }

        private void UpdateArrowVisuals(Canvas group, AvaloniaPoint start, AvaloniaPoint end)
        {
            double minX = Canvas.GetLeft(group);
            double minY = Canvas.GetTop(group);
            var line = group.Children.OfType<Avalonia.Controls.Shapes.Line>().FirstOrDefault();
            var head = group.Children.OfType<Avalonia.Controls.Shapes.Polygon>().FirstOrDefault();
            if (line == null || head == null) return;

            line.StartPoint = new AvaloniaPoint(start.X - minX, start.Y - minY);
            line.EndPoint = new AvaloniaPoint(end.X - minX, end.Y - minY);

            double angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
            double headLength = 12 + _currentThickness * 2;
            double headWidth = 8 + _currentThickness * 1.5;

            double cos = Math.Cos(angle);
            double sin = Math.Sin(angle);

            var tip = new AvaloniaPoint(end.X - minX, end.Y - minY);
            var p1 = new AvaloniaPoint(end.X - headLength * cos + headWidth * sin - minX, end.Y - headLength * sin - headWidth * cos - minY);
            var p2 = new AvaloniaPoint(end.X - (headLength * 0.7) * cos - minX, end.Y - (headLength * 0.7) * sin - minY);
            var p3 = new AvaloniaPoint(end.X - headLength * cos - headWidth * sin - minX, end.Y - headLength * sin + headWidth * cos - minY);

            head.Points = new List<AvaloniaPoint> { tip, p1, p2, p3 };
        }

        private void RemovePreviewShape()
        {
            if (_previewControl != null)
            {
                _canvas.Children.Remove(_previewControl);
                _previewControl = null;
            }
        }

        private class ArrowProperties { public AvaloniaPoint Start; public AvaloniaPoint End; }

        private void OnCanvasPointerMoved(object sender, PointerEventArgs e)
        {
            var pos = e.GetPosition(_canvas);
            if (_contextToolbar != null && (_isDraggingSelected || _isResizing || _isDraggingStartPoint || _isDraggingEndPoint))
            {
                _contextToolbar.IsVisible = false;
            }

            if (_currentTool == EditorTool.None && !_isDraggingSelected && !_isResizing && !_isDraggingStartPoint && !_isDraggingEndPoint)
            {
                if (e.Source is AvaloniaControl src && (_resizeHandles.Contains(src) || src == _startHandle || src == _endHandle)) return;
                double tolerance = 10.0;
                var hovered = _canvas.Children.Cast<AvaloniaControl>().LastOrDefault(c => {
                    if (c == _selectionIndicator || _resizeHandles.Contains(c) || ReferenceEquals(c, _contextToolbar) || ReferenceEquals(c, _startHandle) || ReferenceEquals(c, _endHandle) || !c.IsHitTestVisible) return false;
                    if (c is Avalonia.Controls.Shapes.Polyline pl) { double l = Canvas.GetLeft(pl); double t = Canvas.GetTop(pl); if (double.IsNaN(l)) l = 0; if (double.IsNaN(t)) t = 0; for (int i = 0; i < pl.Points.Count - 1; i++) if (PointToLineDistance(pos, new AvaloniaPoint(pl.Points[i].X + l, pl.Points[i].Y + t), new AvaloniaPoint(pl.Points[i+1].X + l, pl.Points[i+1].Y + t)) < tolerance) return true; }
                    return c.Bounds.Inflate(tolerance).Contains(pos);
                });
                Cursor = hovered != null ? new Avalonia.Input.Cursor(StandardCursorType.Hand) : new Avalonia.Input.Cursor(StandardCursorType.Arrow);
                
                if (hovered != null)
                {
                    IBrush b = null;
                    if (hovered is Avalonia.Controls.Shapes.Shape s) b = s.Stroke ?? s.Fill;
                    else if (hovered is Border brd) b = brd.BorderBrush ?? brd.Background;
                    else if (hovered is TextBlock tb) b = tb.Foreground;

                    if (b is SolidColorBrush scb)
                    {
                        UpdateCurrentColorDisplay(scb.Color);
                    }
                }
            }
            if (_isDraggingStartPoint || _isDraggingEndPoint)
            {
                double dx = pos.X - _dragLastPoint.X; double dy = pos.Y - _dragLastPoint.Y;
                if ((Math.Abs(dx) > 0.01 || Math.Abs(dy) > 0.01) && !_dragUndoCaptured) { SaveUndoState(); _dragUndoCaptured = true; }
                
                double left = Canvas.GetLeft(_selectedControl); if (double.IsNaN(left)) left = 0;
                double top = Canvas.GetTop(_selectedControl); if (double.IsNaN(top)) top = 0;

                if (_selectedControl is Avalonia.Controls.Shapes.Line line)
                {
                    if (_isDraggingStartPoint) line.StartPoint = new AvaloniaPoint(line.StartPoint.X + dx, line.StartPoint.Y + dy);
                    else line.EndPoint = new AvaloniaPoint(line.EndPoint.X + dx, line.EndPoint.Y + dy);
                }
                else if (_selectedControl is Canvas group && group.Tag is ArrowProperties props)
                {
                    if (_isDraggingStartPoint) props.Start = new AvaloniaPoint(props.Start.X + dx, props.Start.Y + dy);
                    else props.End = new AvaloniaPoint(props.End.X + dx, props.End.Y + dy);
                    
                    UpdateArrowVisuals(group, props.Start, props.End);
                }
                _dragLastPoint = pos;
                UpdateSelectionIndicator();
                return;
            }
            if (_isResizing && _selectedControl != null) { double dx = pos.X - _dragLastPoint.X; double dy = pos.Y - _dragLastPoint.Y; if ((Math.Abs(dx) > 0.01 || Math.Abs(dy) > 0.01) && !_resizeUndoCaptured) { SaveUndoState(); _resizeUndoCaptured = true; } ResizeSelectedControl(dx, dy); _dragLastPoint = pos; UpdateSelectionIndicator(); return; }
            if (_isDraggingSelected && _selectedControl != null) { double dx = pos.X - _dragLastPoint.X; double dy = pos.Y - _dragLastPoint.Y; if ((Math.Abs(dx) > 0.01 || Math.Abs(dy) > 0.01) && !_dragUndoCaptured) { SaveUndoState(); _dragUndoCaptured = true; } double curL = Canvas.GetLeft(_selectedControl); if (double.IsNaN(curL)) curL = _selectedControl.Bounds.X; double curT = Canvas.GetTop(_selectedControl); if (double.IsNaN(curT)) curT = _selectedControl.Bounds.Y; Canvas.SetLeft(_selectedControl, curL + dx); Canvas.SetTop(_selectedControl, curT + dy); _dragLastPoint = pos; UpdateSelectionIndicator(); return; }
            if (!_isDrawing) return;
            if (_currentTool == EditorTool.FreeDraw && _activePolyline != null) _activePolyline.Points.Add(new AvaloniaPoint(pos.X - _startPoint.X, pos.Y - _startPoint.Y));
            else UpdatePreviewShape(pos);
        }

        private void ResizeSelectedControl(double dx, double dy)
        {
            double oldW = double.IsNaN(_selectedControl.Width) ? _selectedControl.Bounds.Width : _selectedControl.Width;
            double oldH = double.IsNaN(_selectedControl.Height) ? _selectedControl.Bounds.Height : _selectedControl.Height;
            double left = Canvas.GetLeft(_selectedControl); if (double.IsNaN(left)) left = _selectedControl.Bounds.X;
            double top = Canvas.GetTop(_selectedControl); if (double.IsNaN(top)) top = _selectedControl.Bounds.Y;
            bool keepRatio = (_selectedControl is TextBlock) || (_selectedControl is Border b && b.Child is TextBlock);
            double newW = oldW; double newH = oldH;
            if (keepRatio)
            {
                double scale = 1.0;
                switch (_resizeHandleIndex) { case 0: scale = Math.Max(10 / oldW, Math.Max((oldW - dx) / oldW, (oldH - dy) / oldH)); break; case 1: scale = Math.Max(10 / oldW, Math.Max((oldW + dx) / oldW, (oldH - dy) / oldH)); break; case 2: scale = Math.Max(10 / oldW, Math.Max((oldW + dx) / oldW, (oldH + dy) / oldH)); break; case 3: scale = Math.Max(10 / oldW, Math.Max((oldW - dx) / oldW, (oldH + dy) / oldH)); break; }
                newW = oldW * scale; newH = oldH * scale;
                switch (_resizeHandleIndex) { case 0: Canvas.SetLeft(_selectedControl, left + (oldW - newW)); Canvas.SetTop(_selectedControl, top + (oldH - newH)); break; case 1: Canvas.SetTop(_selectedControl, top + (oldH - newH)); break; case 3: Canvas.SetLeft(_selectedControl, left + (oldW - newW)); break; }
            }
            else
            {
                switch (_resizeHandleIndex) { case 0: newW = Math.Max(10, oldW - dx); newH = Math.Max(10, oldH - dy); Canvas.SetLeft(_selectedControl, left + (oldW - newW)); Canvas.SetTop(_selectedControl, top + (oldH - newH)); break; case 1: newW = Math.Max(10, oldW + dx); newH = Math.Max(10, oldH - dy); Canvas.SetTop(_selectedControl, top + (oldH - newH)); break; case 2: newW = Math.Max(10, oldW + dx); newH = Math.Max(10, oldH + dy); break; case 3: newW = Math.Max(10, oldW - dx); newH = Math.Max(10, oldH + dy); Canvas.SetLeft(_selectedControl, left + (oldW - newW)); break; }
            }

            double scaleX = newW / oldW;
            double scaleY = newH / oldH;
            _selectedControl.Width = newW; _selectedControl.Height = newH;
            
            if (_selectedControl is Avalonia.Controls.Shapes.Line lineCtrl)
            {
                lineCtrl.StartPoint = new AvaloniaPoint(lineCtrl.StartPoint.X * scaleX, lineCtrl.StartPoint.Y * scaleY);
                lineCtrl.EndPoint = new AvaloniaPoint(lineCtrl.EndPoint.X * scaleX, lineCtrl.EndPoint.Y * scaleY);
            }
            else if (_selectedControl is Avalonia.Controls.Shapes.Polyline polyCtrl)
            {
                var newPoints = new System.Collections.Generic.List<AvaloniaPoint>();
                foreach (var pt in polyCtrl.Points)
                {
                    newPoints.Add(new AvaloniaPoint(pt.X * scaleX, pt.Y * scaleY));
                }
                polyCtrl.Points = newPoints;
                polyCtrl.InvalidateVisual();
            }
            else if (_selectedControl is Canvas group)
            {
                if (group.Tag is ArrowProperties props)
                {
                    double oldLX = Math.Min(props.Start.X, props.End.X);
                    double oldLY = Math.Min(props.Start.Y, props.End.Y);
                    
                    props.Start = new AvaloniaPoint(props.Start.X + (left - (oldLX - 20 - _currentThickness * 2)), props.Start.Y + (top - (oldLY - 20 - _currentThickness * 2)));
                    props.End = new AvaloniaPoint(props.End.X + (left - (oldLX - 20 - _currentThickness * 2)), props.End.Y + (top - (oldLY - 20 - _currentThickness * 2)));

                    UpdateArrowVisuals(group, props.Start, props.End);
                }
                group.InvalidateVisual();
            }
            else if (_selectedControl is TextBlock t) { t.FontSize *= scaleX; UpdatePersistedSize(EditorTool.Emoji, t.FontSize); }
            else if (_selectedControl is Border borderCtrl) { if (borderCtrl.Child is TextBox tb) { tb.FontSize *= scaleX; tb.Width = borderCtrl.Width - 12; UpdatePersistedSize(EditorTool.Text, tb.FontSize); } else if (borderCtrl.Child is TextBlock txt) { txt.FontSize *= scaleX; borderCtrl.CornerRadius = new CornerRadius(newW / 2); UpdatePersistedSize(EditorTool.Counter, txt.FontSize); } }
        }

        private void UpdatePersistedSize(EditorTool tool, double size) { var config = IniConfig.GetIniSection<CoreConfiguration>(); if (tool == EditorTool.Text) config.LastTextSize = size; else if (tool == EditorTool.Emoji) config.LastEmojiSize = size; else if (tool == EditorTool.Counter) config.LastCounterSize = size; _toolFontSizes[tool] = size; }

        private void OnCanvasPointerReleased(object sender, PointerReleasedEventArgs e)
        {
            bool wasTransformingSelection = _selectedControl != null && (_isDraggingSelected || _isResizing || _isDraggingStartPoint || _isDraggingEndPoint);

            _isResizing = false;
            _resizeUndoCaptured = false;
            _isDraggingStartPoint = false;
            _isDraggingEndPoint = false;
            e.Pointer.Capture(null);

            if (wasTransformingSelection)
            {
                UpdateSelectionIndicator();
                UpdateThicknessPanelVisibility();
            }

            if (_isDraggingSelected)
            {
                _isDraggingSelected = false;
                _dragUndoCaptured = false;
                UpdateSelectionIndicator();
                return;
            }

            if (!_isDrawing) return;
            _isDrawing = false;
            var endPoint = e.GetPosition(_canvas);
            RemovePreviewShape();
            if (_currentTool != EditorTool.FreeDraw) CommitShape(_startPoint, endPoint);
            _activePolyline = null;
        }

        private void UpdateSelectionIndicator()
        {
            if (_selectedControl == null || _image == null) { 
                if (_selectionIndicator != null) _selectionIndicator.IsVisible = false; 
                if (_resizeHandles != null) foreach (var handleItem in _resizeHandles) handleItem.IsVisible = false; 
                if (_deleteBtn != null) _deleteBtn.IsEnabled = false; 
                if (_contextToolbar != null) _contextToolbar.IsVisible = false;
                if (_startHandle != null) _startHandle.IsVisible = false;
                if (_endHandle != null) _endHandle.IsVisible = false;
                if (_pixelateSlider != null) _pixelateSlider.IsVisible = false;
                UpdateModeStatus();
                return; 
            }
            
            _selectionIndicator.IsVisible = true; 
            if (_deleteBtn != null) _deleteBtn.IsEnabled = true;
            
            double absX = Canvas.GetLeft(_selectedControl); if (double.IsNaN(absX)) absX = 0;
            double absY = Canvas.GetTop(_selectedControl); if (double.IsNaN(absY)) absY = 0;
            double w = _selectedControl.Width;
            double h = _selectedControl.Height;

            // Handle special cases for bounds calculation
            if (double.IsNaN(w) || w == 0) w = _selectedControl.Bounds.Width;
            if (double.IsNaN(h) || h == 0) h = _selectedControl.Bounds.Height;

            if (_selectedControl is Avalonia.Controls.Shapes.Polyline pl)
            {
                if (pl.Points.Count > 0)
                {
                    double minX = pl.Points.Min(p => p.X);
                    double minY = pl.Points.Min(p => p.Y);
                    double maxX = pl.Points.Max(p => p.X);
                    double maxY = pl.Points.Max(p => p.Y);
                    absX += minX; absY += minY;
                    w = maxX - minX; h = maxY - minY;
                }
            }
            else if (_selectedControl is Avalonia.Controls.Shapes.Line lineCtrl)
            {
                double minX = Math.Min(lineCtrl.StartPoint.X, lineCtrl.EndPoint.X);
                double minY = Math.Min(lineCtrl.StartPoint.Y, lineCtrl.EndPoint.Y);
                double maxX = Math.Max(lineCtrl.StartPoint.X, lineCtrl.EndPoint.X);
                double maxY = Math.Max(lineCtrl.StartPoint.Y, lineCtrl.EndPoint.Y);
                absX += minX; absY += minY;
                w = Math.Max(1, maxX - minX); h = Math.Max(1, maxY - minY);
            }

            _selectionIndicator.Width = w + 4; _selectionIndicator.Height = h + 4;
            Canvas.SetLeft(_selectionIndicator, absX - 2); Canvas.SetTop(_selectionIndicator, absY - 2);
            
            // Corner Handles (Always show for all shapes)
            _resizeHandles[0].IsVisible = true; Canvas.SetLeft(_resizeHandles[0], absX - 7); Canvas.SetTop(_resizeHandles[0], absY - 7);
            _resizeHandles[1].IsVisible = true; Canvas.SetLeft(_resizeHandles[1], absX + w - 7); Canvas.SetTop(_resizeHandles[1], absY - 7);
            _resizeHandles[2].IsVisible = true; Canvas.SetLeft(_resizeHandles[2], absX + w - 7); Canvas.SetTop(_resizeHandles[2], absY + h - 7);
            _resizeHandles[3].IsVisible = true; Canvas.SetLeft(_resizeHandles[3], absX - 7); Canvas.SetTop(_resizeHandles[3], absY + h - 7);

            // Vector Endpoint Handles (Only for Line and Arrow)
            bool isVector = (_selectedControl is Avalonia.Controls.Shapes.Line) || 
                           (_selectedControl is Canvas group && group.Tag?.ToString() == "Arrow");
            if (isVector)
            {
                _startHandle.IsVisible = true;
                _endHandle.IsVisible = true;
                
                AvaloniaPoint startP, endP;
                double originX = Canvas.GetLeft(_selectedControl); if (double.IsNaN(originX)) originX = 0;
                double originY = Canvas.GetTop(_selectedControl); if (double.IsNaN(originY)) originY = 0;

                if (_selectedControl is Avalonia.Controls.Shapes.Line line) { startP = line.StartPoint; endP = line.EndPoint; }
                else {
                    var arrowLine = ((Canvas)_selectedControl).Children.OfType<Avalonia.Controls.Shapes.Line>().FirstOrDefault();
                    if (arrowLine == null) { _startHandle.IsVisible = false; _endHandle.IsVisible = false; goto PostHandles; }
                    startP = arrowLine.StartPoint; endP = arrowLine.EndPoint;
                }
                
                Canvas.SetLeft(_startHandle, originX + startP.X - 7); Canvas.SetTop(_startHandle, originY + startP.Y - 7);
                Canvas.SetLeft(_endHandle, originX + endP.X - 7); Canvas.SetTop(_endHandle, originY + endP.Y - 7);
            }
            else
            {
                _startHandle.IsVisible = false; 
                _endHandle.IsVisible = false;
            }

        PostHandles:
            // Pixelate Slider
            if (_selectedControl.Tag?.ToString() == "Pixelate")
            {
                if (_pixelateSlider != null)
                {
                    _pixelateSlider.IsVisible = true;
                    Canvas.SetLeft(_pixelateSlider, absX + w + 15);
                    Canvas.SetTop(_pixelateSlider, absY);
                    _pixelateSlider.Height = Math.Max(100, h);
                    
                    var rect = new Rect(absX, absY, w, h);
                    var area = ClampImageRectangle(rect);
                    if (area.Width >= 5 && area.Height >= 5)
                    {
                        if (_undoStack.Count > 0)
                        {
                            var last = _undoStack.Last.Value;
                            _image?.Dispose();
                            _image = last.Image.Clone(x => { });
                        }
                        _image.Mutate(x => x.Pixelate((int)_pixelateSlider.Value, area));
                        UpdateDisplay();
                    }
                }
            }
            else
            {
                if (_pixelateSlider != null) _pixelateSlider.IsVisible = false;
            }

            if (_contextToolbar != null)
            {
                bool transformingSelection = _isDraggingSelected || _isResizing || _isDraggingStartPoint || _isDraggingEndPoint;
                _contextToolbar.IsVisible = !transformingSelection;
                if (!transformingSelection) PositionContextToolbar(absX, absY, w, h);
            }

            UpdateModeStatus();
        }

        private void PositionContextToolbar(double absX, double absY, double width, double height)
        {
            if (_contextToolbar == null || _canvas == null) return;

            const double gap = 10;
            double canvasW = _canvas.Bounds.Width > 0 ? _canvas.Bounds.Width : _canvas.Width;
            double canvasH = _canvas.Bounds.Height > 0 ? _canvas.Bounds.Height : _canvas.Height;
            if (double.IsNaN(canvasW) || canvasW <= 0) canvasW = _image?.Width ?? 0;
            if (double.IsNaN(canvasH) || canvasH <= 0) canvasH = _image?.Height ?? 0;

            _contextToolbar.Measure(new AvaloniaSize(double.PositiveInfinity, double.PositiveInfinity));
            double barW = Math.Max(89, _contextToolbar.DesiredSize.Width);
            double barH = Math.Max(40, _contextToolbar.DesiredSize.Height);

            var sideSpace = new[]
            {
                ("Right", canvasW - (absX + width)),
                ("Left", absX),
                ("Bottom", canvasH - (absY + height)),
                ("Top", absY)
            };

            string side = sideSpace
                .Where(item =>
                    ((item.Item1 == "Right" || item.Item1 == "Left") && item.Item2 >= barW + gap) ||
                    ((item.Item1 == "Bottom" || item.Item1 == "Top") && item.Item2 >= barH + gap))
                .OrderByDescending(item => item.Item2)
                .Select(item => item.Item1)
                .FirstOrDefault();

            if (side == null)
            {
                side = sideSpace.OrderByDescending(item => item.Item2).First().Item1;
            }

            double toolbarL;
            double toolbarT;
            switch (side)
            {
                case "Left":
                    toolbarL = absX - barW - gap;
                    toolbarT = absY + (height - barH) / 2;
                    break;
                case "Bottom":
                    toolbarL = absX + (width - barW) / 2;
                    toolbarT = absY + height + gap;
                    break;
                case "Top":
                    toolbarL = absX + (width - barW) / 2;
                    toolbarT = absY - barH - gap;
                    break;
                default:
                    toolbarL = absX + width + gap;
                    toolbarT = absY + (height - barH) / 2;
                    break;
            }

            Canvas.SetLeft(_contextToolbar, Math.Clamp(toolbarL, 0, Math.Max(0, canvasW - barW)));
            Canvas.SetTop(_contextToolbar, Math.Clamp(toolbarT, 0, Math.Max(0, canvasH - barH)));
        }

        private void OnDuplicateClick(object sender, RoutedEventArgs e)
        {
            if (_selectedControl == null) return;
            SaveUndoState();
            var clone = CloneAnnotationControl(_selectedControl);
            if (clone != null)
            {
                Canvas.SetLeft(clone, Canvas.GetLeft(clone) + 20);
                Canvas.SetTop(clone, Canvas.GetTop(clone) + 20);
                _canvas.Children.Add(clone);
                _selectedControl = clone;
                UpdateSelectionIndicator();
                OverlayHelper.ShowLightToast("Duplicated. Press Ctrl+Z to undo", this);
            }
        }

        private void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            if (_selectedControl == null) return;

            SaveUndoState();
            _canvas.Children.Remove(_selectedControl);
            _selectedControl = null;
            UpdateSelectionIndicator();
            UpdateThicknessPanelVisibility();
            OverlayHelper.ShowLightToast("Deleted. Press Ctrl+Z to undo", this);
            Log.Info("Object deleted via toolbar button.");
        }

        private bool TryBeginEditorOperation()
        {
            if (_isBusy) return false;
            _isBusy = true;
            return true;
        }

        private void EndEditorOperation()
        {
            _isBusy = false;
        }

        private async void OnResizeClick(object sender, RoutedEventArgs e)
        {
            if (_image == null || !TryBeginEditorOperation()) return;
            try
            {
                var resizeWin = new ResizeWindow(_image.Width, _image.Height);
                await resizeWin.ShowDialog(this);
                if (resizeWin.IsConfirmed)
                {
                    Log.Info($"Resizing image to {resizeWin.ResultWidth}x{resizeWin.ResultHeight}");
                    SaveUndoState();
                    FlattenAnnotations();
                    await Task.Run(() => _image.Mutate(x => x.Resize(resizeWin.ResultWidth, resizeWin.ResultHeight))).ConfigureAwait(true);
                    UpdateDisplay();
                    OverlayHelper.ShowLightToast("IMAGE RESIZED", this);
                    ShowUndoAvailableHint();
                }
            }
            finally
            {
                EndEditorOperation();
            }
        }

        private async Task<string> GetEffectiveDownloadPathAsync()
        {
            var config = IniConfig.GetIniSection<CoreConfiguration>();
            if (!string.IsNullOrEmpty(config.UserDownloadPath) && Directory.Exists(config.UserDownloadPath)) return config.UserDownloadPath;

            string defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (Directory.Exists(defaultPath)) return defaultPath;

            // Soft-fail: Use modern StorageProvider API (Mandate 7)
            if (this.StorageProvider.CanPickFolder)
            {
                var folders = await this.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "SnapVox: Select Default Download Folder",
                    AllowMultiple = false
                }).ConfigureAwait(true);

                var selected = folders?.FirstOrDefault()?.TryGetLocalPath();
                if (!string.IsNullOrEmpty(selected))
                {
                    config.UserDownloadPath = selected;
                    IniConfig.Save();
                    return selected;
                }
            }
            return Path.Combine(Path.GetTempPath(), "SnapVox");
        }

        private async void OnDownloadClick(object sender, RoutedEventArgs e)
        {
            if (_image == null || !TryBeginEditorOperation()) return;
            try
            {
                using var tempImage = GetFlattenedImage();
                if (tempImage == null) return;

                string fileName = $"Capture_{DateTime.Now:yyyy-MM-dd_HH-mm-ss_fff}.jpg";
                
                // 1. Save to Downloads (zero prompt)
                string downloadsPath = await GetEffectiveDownloadPathAsync().ConfigureAwait(true);
                Directory.CreateDirectory(downloadsPath);
                await SaveJpegAsync(tempImage, Path.Combine(downloadsPath, fileName)).ConfigureAwait(true);

                // 2. Save to %TMP%\SnapVox (Mandate 2)
                await SaveToHistoryBackupAsync(fileName, tempImage).ConfigureAwait(true);

                await UiClipboard.SetImageAsync(tempImage).ConfigureAwait(true);
                OverlayHelper.ShowNotification("IMAGE SAVED TO DOWNLOADS", this);
                await Task.Delay(1000);
                _forceClose = true;
                Close();
            }
            catch (Exception ex)
            {
                Log.Fatal("[DOWNLOAD_CRITICAL_ERROR]", ex);
                OverlayHelper.ShowNotification("Save Failed", this);
            }
            finally
            {
                EndEditorOperation();
            }
        }

        private async void OnCopyClick(object sender, RoutedEventArgs e)
        {
            if (_image == null || !TryBeginEditorOperation()) return;
            try
            {
                Log.Info("STAGED COPY INITIATED");
                using var tempImage = GetFlattenedImage();
                if (tempImage == null) return;

                // 1. Save to %TMP%\SnapVox (Mandate 3)
                await SaveToHistoryBackupAsync($"Capture_{DateTime.Now:yyyy-MM-dd HH_mm_ss_fff}.jpg", tempImage).ConfigureAwait(true);

                // 2. Copy CF_DIB (Mandate 3)
                await UiClipboard.SetImageAsync(tempImage).ConfigureAwait(true);
                
                OverlayHelper.ShowNotification("IMAGE SAVED & COPIED", this);
                await Task.Delay(1000);
                _forceClose = true;
                Close();
            }
            catch (Exception ex)
            {
                Log.Fatal("[COPY_CRITICAL_ERROR]", ex);
                OverlayHelper.ShowNotification("Copy Failed", this);
            }
            finally
            {
                EndEditorOperation();
            }
        }

        private static Task SaveJpegAsync(ImageSharpImage img, string path) => Task.Run(() => img.Save(path, new JpegEncoder { Quality = 100 }));

        private static async Task SaveToHistoryBackupAsync(string fileName, ImageSharpImage img)
        {
            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "SnapVox");
                Directory.CreateDirectory(tempDir);
                // Force 100% Quality Always (Mandate 1, 2, 3)
                await SaveJpegAsync(img, Path.Combine(tempDir, fileName)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Error($"[BACKUP_FAILURE] Could not write to temp folder.", ex);
            }
        }

        private async void OnRotateCwClick(object sender, RoutedEventArgs e) { await RotateImageAsync(RotateMode.Rotate90); }
        private async void OnRotateCcwClick(object sender, RoutedEventArgs e) { await RotateImageAsync(RotateMode.Rotate270); }

        private async void OnOcrClick(object sender, RoutedEventArgs e)
        {
            if (_image == null || !TryBeginEditorOperation()) return;
            var ocrButton = this.FindControl<Button>("OcrButton");
            var ocrButtonText = this.FindControl<TextBlock>("OcrButtonText");
            try
            {
                if (ocrButton != null) ocrButton.IsEnabled = false;
                if (ocrButtonText != null) ocrButtonText.Text = "OCR Working...";

                Log.Info("STAGED OCR INITIATED");
                using var tempImage = GetFlattenedImage();
                if (tempImage == null) return;
                
                var config = IniConfig.GetIniSection<CoreConfiguration>();
                var providers = SimpleServiceProvider.Current.GetAllInstances<IOcrProvider>();
                var ocrProvider = OcrProviderSelector.Select(providers, config.OcrEngine);
                if (ocrProvider == null)
                {
                    OverlayHelper.ShowNotification("OCR UNAVAILABLE", this);
                    return;
                }

                var ocrInfo = await ocrProvider.DoOcrAsync(tempImage).ConfigureAwait(true);
                if (ocrInfo == null)
                {
                    OverlayHelper.ShowNotification("OCR UNAVAILABLE", this);
                    return;
                }

                string text = ocrInfo?.Text ?? "";
                
                // Mandate 4: Copy to clipboard zero prompt
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel?.Clipboard != null) await topLevel.Clipboard.SetTextAsync(text);
                
                // Mandate 4: Save to %TMP%\SnapVox and open in notepad
                string tempDir = Path.Combine(Path.GetTempPath(), "SnapVox");
                Directory.CreateDirectory(tempDir);
                string fileName = $"OCR_{DateTime.Now:yyyy-MM-dd_HH-mm-ss_fff}.txt";
                string fullPath = Path.Combine(tempDir, fileName);
                
                await File.WriteAllTextAsync(fullPath, text).ConfigureAwait(true);
                Process.Start(new ProcessStartInfo("notepad.exe", fullPath) { UseShellExecute = true });
                
                OverlayHelper.ShowNotification("TEXT COPIED & SAVED", this);
                
                if (config.CloseEditorOnAction)
                {
                    await Task.Delay(1000); // Unified 1.0s delay
                    _forceClose = true;
                    Close();
                }
            }
            catch (Exception ex) { Log.Error("OCR failed", ex); OverlayHelper.ShowNotification("OCR FAILED", this); }
            finally
            {
                if (ocrButtonText != null) ocrButtonText.Text = "OCR Text Extraction";
                if (ocrButton != null) ocrButton.IsEnabled = true;
                EndEditorOperation();
            }
        }

        private void OnUndoClick(object sender, RoutedEventArgs e)
        {
            if (_undoStack.Count > 0)
            {
                var current = CreateSnapshot();
                var previous = _undoStack.Last.Value;
                _undoStack.RemoveLast();
                _redoStack.AddLast(current);
                if (_redoStack.Count > MaxStackSize)
                {
                    var oldest = _redoStack.First.Value;
                    _redoStack.RemoveFirst();
                    DisposeSnapshot(oldest);
                }
                RestoreSnapshot(previous);
                UpdateUndoRedoState();
            }
        }

        private void OnRedoClick(object sender, RoutedEventArgs e)
        {
            if (_redoStack.Count > 0)
            {
                var current = CreateSnapshot();
                var next = _redoStack.Last.Value;
                _redoStack.RemoveLast();
                _undoStack.AddLast(current);
                if (_undoStack.Count > MaxStackSize)
                {
                    var oldest = _undoStack.First.Value;
                    _undoStack.RemoveFirst();
                    DisposeSnapshot(oldest);
                }
                RestoreSnapshot(next);
                UpdateUndoRedoState();
            }
        }

        private void OnToolClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string toolStr || !Enum.TryParse<EditorTool>(toolStr, out var tool)) return;

            if (_currentTool == tool)
            {
                _currentTool = EditorTool.None;
                Cursor = new Avalonia.Input.Cursor(StandardCursorType.Arrow);
                btn.Classes.Remove("selected");
            }
            else
            {
                _currentTool = tool;
                Cursor = new Avalonia.Input.Cursor(StandardCursorType.Cross);

                var toolBar = this.FindControl<StackPanel>("LeftToolBar");
                if (toolBar != null)
                {
                    foreach (var child in toolBar.Children)
                    {
                        if (child is Button b) b.Classes.Remove("selected");
                    }
                }

                this.FindControl<Button>("CropTool")?.Classes.Remove("selected");
                btn.Classes.Add("selected");
            }

            _currentThickness = _toolThicknesses.TryGetValue(_currentTool, out var thickness) ? thickness : 3.0;
            _isFillMode = _toolFillModes.TryGetValue(_currentTool, out var fillMode) && fillMode;
            UpdateFillToggleVisual();
            UpdateThicknessPanelVisibility();
            UpdateModeStatus();

            if (_toolBrushes.TryGetValue(_currentTool, out var brush) && brush is SolidColorBrush scb)
            {
                UpdateCurrentColorDisplay(scb.Color);
                UpdatePresetSelectionVisuals(scb.Color);
            }
        }
        private void OnColorSelectClick(object sender, RoutedEventArgs e) { if (sender is Button btn && btn.Tag is string colorHex) { var color = AvaloniaColor.Parse(colorHex); ApplyColor(color); UpdateCurrentColorDisplay(color); } }

        private void OnColorConfirmClick(object sender, RoutedEventArgs e)
        {
            var picker = this.FindControl<ColorView>("ColorPickerView");
            if (picker != null)
            {
                var color = picker.Color;
                UpdateCurrentColorDisplay(color);
                ApplyColor(color);
                PushColorToHistory(color);
            }
            this.FindControl<Button>("CustomColorBtn")?.Flyout?.Hide();
        }

        private void OnColorCancelClick(object sender, RoutedEventArgs e)
        {
            this.FindControl<Button>("CustomColorBtn")?.Flyout?.Hide();
        }

        private async void OnSamplerClick(object sender, RoutedEventArgs e)
        {
            this.FindControl<Button>("CustomColorBtn")?.Flyout?.Hide();
            OverlayHelper.ShowLightToast("COLOR SAMPLER ACTIVE (Esc cancels)", this);
            var color = await ScreenColorSampler.PickColorAsync();
            if (color.HasValue)
            {
                var picker = this.FindControl<ColorView>("ColorPickerView");
                if (picker != null) picker.Color = color.Value;
                UpdateCurrentColorDisplay(color.Value);
                this.FindControl<Button>("CustomColorBtn")?.Flyout?.ShowAt(this.FindControl<Button>("CustomColorBtn"));
            }
        }

        private static string ToRgbHex(AvaloniaColor color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

        private void UpdateCurrentColorDisplay(AvaloniaColor color)
        {
            var brush = new SolidColorBrush(color);
            var preview = this.FindControl<Border>("CurrentColorPreview");
            if (preview != null) preview.Background = brush;
            var hexText = this.FindControl<TextBlock>("CurrentColorHex");
            if (hexText != null) hexText.Text = ToRgbHex(color);
            var button = this.FindControl<Button>("CustomColorBtn");
            if (button != null) ToolTip.SetTip(button, $"Current color {ToRgbHex(color)}");
        }

        private void PushColorToHistory(AvaloniaColor color)
        {
            string hex = color.ToString().ToUpperInvariant();
            if (hex.Length == 9 && hex.StartsWith("#FF")) hex = "#" + hex.Substring(3);
            
            var config = IniConfig.GetIniSection<CoreConfiguration>();
            var colors = new List<string>((config.CustomColors ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries));
            
            if (colors.Count > 0 && colors.Last() == hex) return;
            if (colors.Contains(hex)) colors.Remove(hex);
            
            colors.Add(hex);
            while (colors.Count > 6) colors.RemoveAt(0);
            
            config.CustomColors = string.Join(",", colors);
            IniConfig.Save();
            
            RefreshColorPresetsPanel();
        }

        private void RefreshColorPresetsPanel()
        {
            var config = IniConfig.GetIniSection<CoreConfiguration>();
            var colors = new List<string>((config.CustomColors ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries));
            var panel = this.FindControl<StackPanel>("ColorPresetsPanel");
            if (panel == null) return;
            
            var buttons = panel.Children.OfType<Button>().Where(b => b.Classes.Contains("color-preset")).ToList();
            for (int i = 0; i < buttons.Count; i++)
            {
                if (i < colors.Count)
                {
                    if (AvaloniaColor.TryParse(colors[i], out var parsedColor))
                    {
                        buttons[i].Background = new SolidColorBrush(parsedColor);
                        buttons[i].Tag = colors[i];
                        buttons[i].IsVisible = true;
                    }
                }
                else
                {
                    buttons[i].IsVisible = false;
                }
            }
        }

        private void ApplyColor(AvaloniaColor color)
        {
            _lastSelectedColor = color;
            var brush = new SolidColorBrush(color);
            if (_selectedControl != null)
            {
                SaveUndoState();
                UpdateControlColor(_selectedControl, color);

                var inferredTool = GetToolFromControl(_selectedControl);
                if (inferredTool != EditorTool.None)
                {
                    if (inferredTool == EditorTool.Highlight) {
                        _toolBrushes[EditorTool.Highlight] = new SolidColorBrush(AvaloniaColor.FromArgb(80, color.R, color.G, color.B));
                    } else {
                        _toolBrushes[inferredTool] = brush;
                    }
                }
                OverlayHelper.ShowLightToast("COLOR UPDATED", this);
                ShowUndoAvailableHint();
            }
            else if (_currentTool != EditorTool.None)
            {
                if (_currentTool == EditorTool.Highlight) {
                    _toolBrushes[EditorTool.Highlight] = new SolidColorBrush(AvaloniaColor.FromArgb(80, color.R, color.G, color.B));
                } else {
                    _toolBrushes[_currentTool] = brush;
                }
            }
            else if (_currentTool == EditorTool.None)
            {
                foreach (EditorTool tool in Enum.GetValues<EditorTool>())
                {
                    if (tool == EditorTool.None) continue;
                    if (tool == EditorTool.Highlight)
                        _toolBrushes[tool] = new SolidColorBrush(AvaloniaColor.FromArgb(80, color.R, color.G, color.B));
                    else
                        _toolBrushes[tool] = brush;
                }
            }
            UpdatePresetSelectionVisuals(color);
        }

        private EditorTool GetToolFromControl(AvaloniaControl control)
        {
            if (control is Avalonia.Controls.Shapes.Line) return EditorTool.Line;
            if (control is Avalonia.Controls.Shapes.Rectangle r) {
                if (r.Classes.Contains("highlight")) return EditorTool.Highlight;
                if (r.Tag?.ToString() == "Pixelate") return EditorTool.Blur;
                return EditorTool.Rectangle;
            }
            if (control is Avalonia.Controls.Shapes.Ellipse) return EditorTool.Ellipse;
            if (control is Avalonia.Controls.Shapes.Polyline) return EditorTool.FreeDraw;
            if (control is Border b) {
                if (b.Child is TextBox) return EditorTool.Text;
                if (b.Child is TextBlock) return EditorTool.Counter;
            }
            if (control is TextBlock) return EditorTool.Emoji;
            if (control is Canvas) return EditorTool.Arrow;
            return EditorTool.None;
        }

        private void UpdateControlColor(AvaloniaControl control, AvaloniaColor color)
        {
            var brush = new SolidColorBrush(color); 
            var isHighlight = control.Classes.Contains("highlight"); 
            var targetBrush = isHighlight ? new SolidColorBrush(AvaloniaColor.FromArgb(80, color.R, color.G, color.B)) : brush;
            if (control is Avalonia.Controls.Shapes.Shape s) { 
                if (s is Avalonia.Controls.Shapes.Rectangle r && r.Fill != null && !(r.Tag?.ToString() == "Pixelate")) r.Fill = targetBrush; 
                else s.Stroke = brush; 
            }
            else if (control is Border b) { 
                if (b.Child is TextBlock) b.Background = brush; 
                else b.BorderBrush = brush; 
                if (b.Child is TextBox tb) { tb.Foreground = brush; tb.CaretBrush = brush; }
            }
            else if (control is TextBlock t) t.Foreground = brush;
            else if (control is Canvas c) foreach (var child in c.Children) { if (child is Avalonia.Controls.Shapes.Shape cs) cs.Stroke = brush; }
        }

        private void UpdatePresetSelectionVisuals(AvaloniaColor color) { var panel = this.FindControl<StackPanel>("ColorPresetsPanel"); if (panel != null) { string hex = color.ToString().ToUpperInvariant(); if (hex.Length == 9 && hex.StartsWith("#FF")) hex = "#" + hex.Substring(3); foreach (var child in panel.Children.OfType<Button>().Where(b => b.Classes.Contains("color-preset"))) { if (child.Tag?.ToString() == hex) child.Classes.Add("selected"); else child.Classes.Remove("selected"); } } }

        private void OnEmojiSelectClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string emoji) return;

            _currentTool = EditorTool.Emoji;
            _pendingEmoji = emoji;
            Cursor = new Avalonia.Input.Cursor(StandardCursorType.Hand);

            var emojiBtn = this.FindControl<Button>("EmojiTool");
            if (emojiBtn != null) emojiBtn.Content = emoji;

            var toolBar = this.FindControl<StackPanel>("LeftToolBar");
            if (toolBar != null)
            {
                foreach (var child in toolBar.Children)
                {
                    if (child is Button b) b.Classes.Remove("selected");
                }
            }

            this.FindControl<Button>("CropTool")?.Classes.Remove("selected");
            emojiBtn?.Classes.Add("selected");
            UpdateThicknessPanelVisibility();
            UpdateModeStatus();
        }

        private void SyncThicknessUI(double val) 
        { 
            var panel = this.FindControl<StackPanel>("ThicknessPanel"); 
            if (panel != null) 
            { 
                string tag = ((int)val).ToString(); 
                foreach (var b in panel.Children.OfType<Button>().Where(x => x.Classes.Contains("thickness"))) 
                { 
                    if (b.Tag?.ToString() == tag) {
                        b.Classes.Add("selected");
                    } else {
                        b.Classes.Remove("selected");
                    }
                } 
            } 
        }

        private static bool IsThicknessRelevantForTool(EditorTool tool) => tool is EditorTool.Arrow
            or EditorTool.Line
            or EditorTool.FreeDraw
            or EditorTool.Rectangle
            or EditorTool.Ellipse;

        private static bool IsThicknessRelevantForControl(AvaloniaControl control)
        {
            if (control is Avalonia.Controls.Shapes.Shape) return true;
            if (control is Canvas canvas && canvas.Tag?.ToString() == "Arrow") return true;
            return control is Border border && border.Child is TextBox;
        }

        private string GetThicknessLabelText()
        {
            var tool = _selectedControl != null ? GetToolFromControl(_selectedControl) : _currentTool;
            return tool switch
            {
                EditorTool.Rectangle or EditorTool.Ellipse => "Border size",
                EditorTool.FreeDraw => "Pen size",
                EditorTool.Text => "Frame size",
                _ => "Line size"
            };
        }

        private void UpdateThicknessPanelVisibility()
        {
            var panel = this.FindControl<StackPanel>("ThicknessPanel");
            if (panel == null) return;

            bool relevant = _selectedControl != null
                ? IsThicknessRelevantForControl(_selectedControl)
                : IsThicknessRelevantForTool(_currentTool);

            panel.IsVisible = relevant;
            if (!relevant) return;

            var label = this.FindControl<TextBlock>("ThicknessLabel");
            if (label != null) label.Text = GetThicknessLabelText();
            SyncThicknessUI(_currentThickness);
        }
        private void OnThicknessClick(object sender, RoutedEventArgs e) { if (sender is Button btn && btn.Tag is string tStr && double.TryParse(tStr, out var val)) { _currentThickness = val; SyncThicknessUI(val); if (_selectedControl != null) { SaveUndoState(); UpdateControlThickness(_selectedControl, val); var inferred = GetToolFromControl(_selectedControl); if (inferred != EditorTool.None) _toolThicknesses[inferred] = val; OverlayHelper.ShowLightToast("THICKNESS UPDATED", this); ShowUndoAvailableHint(); } else if (_currentTool != EditorTool.None) { _toolThicknesses[_currentTool] = val; } else if (_currentTool == EditorTool.None) { foreach (EditorTool tool in Enum.GetValues<EditorTool>()) { if (tool != EditorTool.None) _toolThicknesses[tool] = val; } } Log.Info($"Thickness set to: {val}"); } }

        private void OnFillToggleClick(object sender, RoutedEventArgs e)
        {
            _isFillMode = !_isFillMode;
            var config = IniConfig.GetIniSection<CoreConfiguration>();
            if (_selectedControl != null) SaveUndoState();

            if (_currentTool == EditorTool.Text || (_selectedControl is Border b && b.Child is TextBox)) {
                config.UseTextFrame = _isFillMode;
                if (_selectedControl is Border textBorder) {
                    textBorder.BorderThickness = new Thickness(config.UseTextFrame ? 2 : 0);
                    textBorder.Background = _isFillMode ? new SolidColorBrush(AvaloniaColor.FromArgb(100, 30, 30, 30)) : new SolidColorBrush(AvaloniaColor.FromArgb(30, 0, 0, 0));
                }
            }

            UpdateFillToggleVisual();
            OverlayHelper.ShowLightToast(_isFillMode ? "FILL ON" : "FILL OFF", this);

            if (_selectedControl != null) {
                UpdateControlFill(_selectedControl, _isFillMode);
                var inferred = GetToolFromControl(_selectedControl);
                if (inferred != EditorTool.None) _toolFillModes[inferred] = _isFillMode;
                ShowUndoAvailableHint();
            } else if (_currentTool != EditorTool.None) {
                _toolFillModes[_currentTool] = _isFillMode;
            } else if (_currentTool == EditorTool.None) {
                foreach (EditorTool tool in Enum.GetValues<EditorTool>()) {
                    if (tool != EditorTool.None) _toolFillModes[tool] = _isFillMode;
                }
            }
        }

        private void UpdateFillToggleVisual()
        {
            var fillBtn = this.FindControl<Button>("FillToggleBtn");
            var fillText = this.FindControl<TextBlock>("FillToggleText");
            if (_isFillMode) fillBtn?.Classes.Add("selected"); else fillBtn?.Classes.Remove("selected");
            if (fillText != null) fillText.Text = _isFillMode ? "Fill: On" : "Fill: Off";
        }

        private void UpdateControlThickness(AvaloniaControl control, double val)
        {
            if (control is Avalonia.Controls.Shapes.Shape s)
            {
                s.StrokeThickness = val;
            }
            else if (control is Canvas c)
            {
                foreach (var child in c.Children.OfType<Avalonia.Controls.Shapes.Shape>())
                {
                    child.StrokeThickness = val;
                }
            }
            else if (control is Border b && b.Child is TextBox)
            {
                b.BorderThickness = new Thickness(val);
            }
        }
        private void UpdateControlFill(AvaloniaControl control, bool fill) { 
            if (control is Avalonia.Controls.Shapes.Shape s && (s is Avalonia.Controls.Shapes.Rectangle || s is Avalonia.Controls.Shapes.Ellipse)) { 
                if (fill) {
                    s.Fill = s.Stroke ?? _toolBrushes[_currentTool] ?? Brushes.Blue;
                } else {
                    if (s.Stroke == null) s.Stroke = s.Fill ?? _toolBrushes[_currentTool] ?? Brushes.Blue;
                    s.Fill = null; 
                }
                s.InvalidateVisual();
            } 
        }

        private IBrush GetContrastColor(IBrush backgroundBrush)
        {
            if (backgroundBrush is SolidColorBrush solidColorBrush)
            {
                var color = solidColorBrush.Color;
                double r = color.R / 255.0;
                double g = color.G / 255.0;
                double b = color.B / 255.0;
                r = r <= 0.03928 ? r / 12.92 : Math.Pow((r + 0.055) / 1.055, 2.4);
                g = g <= 0.03928 ? g / 12.92 : Math.Pow((g + 0.055) / 1.055, 2.4);
                b = b <= 0.03928 ? b / 12.92 : Math.Pow((b + 0.055) / 1.055, 2.4);
                double luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                return luminance > 0.179 ? Brushes.Black : Brushes.White;
            }
            return Brushes.White;
        }

        private void PlaceCounter(AvaloniaPoint pos, IBrush brush) { 
            var config = IniConfig.GetIniSection<CoreConfiguration>(); 
            int val = _counterValue++; 
            IBrush textBrush = GetContrastColor(brush);
            var border = new Border { Width = config.LastCounterSize, Height = config.LastCounterSize, Background = brush, CornerRadius = new CornerRadius(config.LastCounterSize/2), Child = new TextBlock { Text = val.ToString(), Foreground = textBrush, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, FontSize = config.LastCounterSize * 0.6, FontWeight = FontWeight.Bold } }; 
            Canvas.SetLeft(border, pos.X - (border.Width/2)); Canvas.SetTop(border, pos.Y - (border.Height/2)); AddAnnotation(border); OverlayHelper.ShowLightToast("COUNTER PLACED", this); 
        }
        private void PlaceEmoji(AvaloniaPoint pos, string emoji) { var config = IniConfig.GetIniSection<CoreConfiguration>(); var text = new TextBlock { Text = emoji, FontSize = config.LastEmojiSize }; Canvas.SetLeft(text, pos.X - 16); Canvas.SetTop(text, pos.Y - 16); AddAnnotation(text); OverlayHelper.ShowLightToast("EMOJI PLACED", this); }
        private void PlaceText(AvaloniaPoint pos, IBrush brush) 
        { 
            var config = IniConfig.GetIniSection<CoreConfiguration>(); 
            bool fill = _toolFillModes[EditorTool.Text];
            var textBox = new TextBox { 
                MinWidth = 50, 
                Watermark = "Type here...", 
                Foreground = brush, 
                CaretBrush = brush, 
                Background = Brushes.Transparent, 
                BorderThickness = new Thickness(0), 
                FontSize = config.LastTextSize, 
                FontWeight = FontWeight.SemiBold, 
                Padding = new Thickness(4), 
                AcceptsReturn = true,
                TextAlignment = Avalonia.Media.TextAlignment.Center,
                VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center
            }; 
            var border = new Border { 
                BorderBrush = brush, 
                BorderThickness = new Thickness(config.UseTextFrame ? 2 : 0), 
                Background = fill ? new SolidColorBrush(AvaloniaColor.FromArgb(100, 30, 30, 30)) : new SolidColorBrush(AvaloniaColor.FromArgb(30, 0, 0, 0)), 
                Padding = new Thickness(2), 
                Child = textBox 
            }; 
            Canvas.SetLeft(border, pos.X); Canvas.SetTop(border, pos.Y); 
            AttachTextBoxBehavior(border, textBox, config);
            AddAnnotation(border); Dispatcher.UIThread.Post(() => textBox.Focus()); 
        }

        private void CommitShape(AvaloniaPoint start, AvaloniaPoint end)
        {
            AvaloniaControl shape = null; var brush = _toolBrushes[_currentTool]; double w = Math.Abs(end.X - start.X); double h = Math.Abs(end.Y - start.Y); if (w < 2 && h < 2) return;
            switch (_currentTool)
            {
                case EditorTool.Line: shape = new Avalonia.Controls.Shapes.Line { StartPoint = new AvaloniaPoint(start.X - Math.Min(start.X, end.X), start.Y - Math.Min(start.Y, end.Y)), EndPoint = new AvaloniaPoint(end.X - Math.Min(start.X, end.X), end.Y - Math.Min(start.Y, end.Y)), Stroke = brush, StrokeThickness = _currentThickness }; Canvas.SetLeft(shape, Math.Min(start.X, end.X)); Canvas.SetTop(shape, Math.Min(start.Y, end.Y)); break;
                case EditorTool.Arrow: DrawArrow(start, end, brush); return;
                case EditorTool.Highlight: shape = new Avalonia.Controls.Shapes.Rectangle { Fill = brush, Width = w, Height = h }; shape.Classes.Add("highlight"); Canvas.SetLeft(shape, Math.Min(start.X, end.X)); Canvas.SetTop(shape, Math.Min(start.Y, end.Y)); break;
                case EditorTool.Rectangle: shape = new Avalonia.Controls.Shapes.Rectangle { Stroke = brush, StrokeThickness = _currentThickness, Width = w, Height = h, Fill = _toolFillModes[EditorTool.Rectangle] ? brush : null }; Canvas.SetLeft(shape, Math.Min(start.X, end.X)); Canvas.SetTop(shape, Math.Min(start.Y, end.Y)); break;
                case EditorTool.Ellipse: shape = new Avalonia.Controls.Shapes.Ellipse { Stroke = brush, StrokeThickness = _currentThickness, Width = w, Height = h, Fill = _toolFillModes[EditorTool.Ellipse] ? brush : null }; Canvas.SetLeft(shape, Math.Min(start.X, end.X)); Canvas.SetTop(shape, Math.Min(start.Y, end.Y)); break;
                case EditorTool.Blur: shape = new Avalonia.Controls.Shapes.Rectangle { Fill = new SolidColorBrush(AvaloniaColor.FromArgb(120, 100, 100, 100)), Width = w, Height = h, Tag = "Pixelate" }; Canvas.SetLeft(shape, Math.Min(start.X, end.X)); Canvas.SetTop(shape, Math.Min(start.Y, end.Y)); break;
                case EditorTool.Crop: ApplyCropRect(new Rect(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y), w, h)); return;
            }
            if (shape != null) AddAnnotation(shape);
        }

        private async void ApplyCropRect(Rect rect)
        {
            if (_image == null || rect.Width < 5 || rect.Height < 5 || !TryBeginEditorOperation()) return;
            try
            {
                var cropRect = ClampImageRectangle(rect);
                if (cropRect.Width < 5 || cropRect.Height < 5) return;
                SaveUndoState();
                
                foreach (var child in GetUserAnnotations())
                {
                    Canvas.SetLeft(child, Canvas.GetLeft(child) - cropRect.X);
                    Canvas.SetTop(child, Canvas.GetTop(child) - cropRect.Y);
                }

                await Task.Run(() => _image.Mutate(x => x.Crop(cropRect))).ConfigureAwait(true);
                UpdateDisplay();
                ShowUndoAvailableHint();
            }
            catch (Exception ex)
            {
                Log.Error("Crop failed", ex);
                OverlayHelper.ShowLightToast("CROP FAILED", this);
            }
            finally
            {
                EndEditorOperation();
            }
        }

        private async Task RotateImageAsync(RotateMode mode)
        {
            if (_image == null || !TryBeginEditorOperation()) return;
            try
            {
                SaveUndoState();
                int oldW = _image.Width;
                int oldH = _image.Height;
                await Task.Run(() => _image.Mutate(x => x.Rotate(mode))).ConfigureAwait(true);
                
                foreach (var child in GetUserAnnotations())
                {
                    double l = Canvas.GetLeft(child);
                    double t = Canvas.GetTop(child);
                    double w = child.Width;
                    double h = child.Height;
                    
                    if (mode == RotateMode.Rotate90)
                    {
                        Canvas.SetLeft(child, oldH - t - h);
                        Canvas.SetTop(child, l);
                    }
                    else if (mode == RotateMode.Rotate270)
                    {
                        Canvas.SetLeft(child, t);
                        Canvas.SetTop(child, oldW - l - w);
                    }
                    else if (mode == RotateMode.Rotate180)
                    {
                        Canvas.SetLeft(child, oldW - l - w);
                        Canvas.SetTop(child, oldH - t - h);
                    }
                    
                    if (mode == RotateMode.Rotate90 || mode == RotateMode.Rotate270)
                    {
                        child.Width = h;
                        child.Height = w;
                        if (child is Avalonia.Controls.Shapes.Line line)
                        {
                            var s = line.StartPoint; var e = line.EndPoint;
                            if (mode == RotateMode.Rotate90) { line.StartPoint = new AvaloniaPoint(h - s.Y, s.X); line.EndPoint = new AvaloniaPoint(h - e.Y, e.X); }
                            else { line.StartPoint = new AvaloniaPoint(s.Y, w - s.X); line.EndPoint = new AvaloniaPoint(e.Y, w - e.X); }
                        }
                    }
                }
                UpdateDisplay();
                ShowUndoAvailableHint();
            }
            finally
            {
                EndEditorOperation();
            }
        }

        private void AddAnnotation(AvaloniaControl control)
        {
            SaveUndoState();
            _canvas.Children.Add(control);
            _selectedControl = control;
            UpdateSelectionIndicator();
            UpdateUndoRedoState();
            ShowUndoAvailableHint();
        }

        private void ApplyPixelate(Rect rect)
        {
            if (_image == null) return;
            var area = ClampImageRectangle(rect);
            if (area.Width < 5 || area.Height < 5) return;
            SaveUndoState();
            _image.Mutate(x => x.Pixelate(_lastPixelateStrength, area));
            UpdateDisplay();
            ShowUndoAvailableHint();
        }

        private void DrawArrow(AvaloniaPoint start, AvaloniaPoint end, IBrush brush)
        {
            double minX = Math.Min(start.X, end.X);
            double minY = Math.Min(start.Y, end.Y);
            double maxX = Math.Max(start.X, end.X);
            double maxY = Math.Max(start.Y, end.Y);
            
            double padding = 20 + _currentThickness * 2;
            minX -= padding; minY -= padding; maxX += padding; maxY += padding;

            var group = new Canvas { 
                Width = maxX - minX, 
                Height = maxY - minY, 
                Tag = new ArrowProperties { Start = start, End = end } 
            };
            Canvas.SetLeft(group, minX);
            Canvas.SetTop(group, minY);

            group.Children.Add(new Avalonia.Controls.Shapes.Line { Stroke = brush, StrokeThickness = _currentThickness, StrokeJoin = PenLineJoin.Round, StrokeLineCap = PenLineCap.Round });
            group.Children.Add(new Avalonia.Controls.Shapes.Polygon { Fill = brush });
            
            UpdateArrowVisuals(group, start, end);
            AddAnnotation(group);
        }

        private ImageSharpImage GetFlattenedImage()
        {
            if (_image == null) return null;
            var chromeState = SetEditorChromeVisible(false);
            try
            {
                int w = _image.Width;
                int h = _image.Height;
                var renderRoot = new Grid { Width = w, Height = h };
                var imgClone = new Avalonia.Controls.Image { Source = _displayBitmap, Width = w, Height = h, Stretch = Stretch.None };
                renderRoot.Children.Add(imgClone);

                var originalParent = _canvas.Parent as Panel;
                int originalIndex = originalParent?.Children.IndexOf(_canvas) ?? -1;
                originalParent?.Children.Remove(_canvas);
                
                double oldZoom = _zoomFactor;
                var oldTransform = _canvas.RenderTransform;
                _zoomFactor = 1.0;
                _canvas.RenderTransform = null;
                _canvas.Width = w;
                _canvas.Height = h;
                
                renderRoot.Children.Add(_canvas);

                var rtb = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
                renderRoot.Measure(new AvaloniaSize(w, h));
                renderRoot.Arrange(new Rect(0, 0, w, h));
                rtb.Render(renderRoot);

                renderRoot.Children.Remove(_canvas);
                _zoomFactor = oldZoom;
                _canvas.RenderTransform = oldTransform;
                if (originalParent != null && originalIndex != -1) originalParent.Children.Insert(originalIndex, _canvas);
                
                ApplyZoom();

                using var ms = new MemoryStream();
                rtb.Save(ms);
                ms.Position = 0;
                var sharpImg = ImageSharpImage.Load(ms);
                
                var config = IniConfig.GetIniSection<CoreConfiguration>();
                if (config.AddFrameBorders)
                {
                    sharpImg.Mutate(x => x.Draw(SixLabors.ImageSharp.Color.Black, 3, new SixLabors.ImageSharp.Rectangle(0, 0, sharpImg.Width, sharpImg.Height)));
                }
                return sharpImg;
            }
            catch (Exception ex)
            {
                Log.Error("Flattening failed", ex);
                return _image.Clone();
            }
            finally
            {
                RestoreVisibility(chromeState);
            }
        }

        private SixLabors.ImageSharp.Rectangle ClampImageRectangle(Rect rect)
        {
            return ClampImageRectangle((int)Math.Floor(rect.X), (int)Math.Floor(rect.Y), (int)Math.Ceiling(rect.Width), (int)Math.Ceiling(rect.Height));
        }

        private SixLabors.ImageSharp.Rectangle ClampImageRectangle(int x, int y, int width, int height)
        {
            if (_image == null) return new SixLabors.ImageSharp.Rectangle(0, 0, 0, 0);
            int left = Math.Clamp(x, 0, _image.Width);
            int top = Math.Clamp(y, 0, _image.Height);
            int right = Math.Clamp(x + width, 0, _image.Width);
            int bottom = Math.Clamp(y + height, 0, _image.Height);
            return new SixLabors.ImageSharp.Rectangle(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
        }
        
        private void FlattenAnnotations()
        {
            if (GetUserAnnotations().Count == 0) return;
            var flattened = GetFlattenedImage(); 
            if (flattened == null) return;
            _image?.Dispose(); 
            _image = flattened; 
            RemoveUserAnnotations(); 
            _selectedControl = null; 
            UpdateSelectionIndicator(); 
            UpdateThicknessPanelVisibility(); 
            UpdateDisplay();
        }

        private async void OnClearAllClick(object sender, RoutedEventArgs e)
        {
            if (GetUserAnnotations().Count == 0) return;
            var prompt = new Window
            {
                Title = "Clear All",
                Width = 300, Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = new SolidColorBrush(AvaloniaColor.Parse("#2D2D30")),
                Content = new StackPanel
                {
                    Spacing = 20, Margin = new Thickness(20),
                    Children = {
                        new TextBlock { Text = "Erase all shapes and start fresh?", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Foreground = Brushes.White },
                        new StackPanel {
                            Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 10, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                            Children = {
                                new Button { Content = "Yes", Width = 80, Cursor = new Avalonia.Input.Cursor(StandardCursorType.Hand), Background = Brushes.Crimson, Foreground = Brushes.White, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center },
                                new Button { Content = "Cancel", Width = 80, Cursor = new Avalonia.Input.Cursor(StandardCursorType.Hand), HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center }
                            }
                        }
                    }
                }
            };
            var buttons = ((StackPanel)((StackPanel)prompt.Content).Children[1]).Children.OfType<Button>().ToList();
            bool confirmed = false;
            buttons[0].Click += (s, ev) => { confirmed = true; prompt.Close(); };
            buttons[1].Click += (s, ev) => prompt.Close();
            await prompt.ShowDialog(this);
            if (confirmed) { SaveUndoState(); RemoveUserAnnotations(); _selectedControl = null; UpdateSelectionIndicator(); ShowUndoAvailableHint(); }
        }
    }
}
