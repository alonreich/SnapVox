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
using snapvox.helpers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
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
        private static readonly Avalonia.Input.Cursor HandCursor = new(StandardCursorType.Hand);
        private static readonly Avalonia.Input.Cursor ArrowCursor = new(StandardCursorType.Arrow);
        private static readonly Avalonia.Input.Cursor CrossCursor = new(StandardCursorType.Cross);
        private static readonly Avalonia.Input.Cursor SizeAllCursor = new(StandardCursorType.SizeAll);
        // FEATURE (hand pan): cursor shown while actively dragging the picture around with
        // the hand (no-tool mode) or with the middle mouse button. Windows has no standard
        // closed-hand cursor, so the 4-way "move" arrow acts as the "holding hand" state.
        private static readonly Avalonia.Input.Cursor PanHeldCursor = new(StandardCursorType.SizeAll);
        private static readonly Avalonia.Input.Cursor TopLeftCursor = new(StandardCursorType.TopLeftCorner);
        private static readonly Avalonia.Input.Cursor TopRightCursor = new(StandardCursorType.TopRightCorner);
        private static readonly Avalonia.Input.Cursor IbeamCursor = new(StandardCursorType.Ibeam);
        private static readonly IBrush SnapDotDimBrush = new SolidColorBrush(AvaloniaColor.FromArgb(120, 200, 200, 200));
        private static readonly IBrush OcrSelectedFillBrush = new SolidColorBrush(AvaloniaColor.FromArgb(140, 0, 191, 255));
        private static readonly IBrush OcrSelectedStrokeBrush = new SolidColorBrush(AvaloniaColor.FromArgb(200, 0, 191, 255));
        private static readonly IBrush OcrUnselectedFillBrush = new SolidColorBrush(AvaloniaColor.FromArgb(90, 0, 0, 0));
        private static readonly IBrush OcrUnselectedStrokeBrush = new SolidColorBrush(AvaloniaColor.FromArgb(140, 255, 255, 255));
        private static long _pixelateCounter = 0;
        private static long _highlightCounter = 0;

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
        
        private static readonly IBrush HighlightBrushBase = new SolidColorBrush(AvaloniaColor.Parse("#60FFFF00"));
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
        // ISSUE_001: hard memory ceiling shared by the undo AND redo history. Each snapshot stores a
        // full-resolution clone (about width * height * 4 bytes), so a count-only cap of 40 + 40
        // entries can retain gigabytes of memory on high-resolution captures.
        private const long MaxUndoHistoryBytes = 384L * 1024 * 1024;
        private Avalonia.Controls.Shapes.Polyline _activePolyline;
        private double _zoomFactor = 1.0;

        private Avalonia.Controls.Shapes.Rectangle _selectionIndicator;
        private List<Avalonia.Controls.Shapes.Rectangle> _resizeHandles = new List<Avalonia.Controls.Shapes.Rectangle>();
        private AvaloniaControl _selectedControl;
        private AvaloniaPoint _dragLastPoint;
        private double _dragUnsnappedLeft;
        private double _dragUnsnappedTop;
        private bool _isDraggingSelected;
        private bool _isResizing;
        private int _resizeHandleIndex = -1;
        private bool _dragUndoCaptured;
        private bool _resizeUndoCaptured;
        private bool _isBusy;
        private AvaloniaControl _previewControl;
        private bool _showFirstRunHints;

        private Border _contextToolbar;
        private Canvas _startHandle;
        private Canvas _endHandle;
        private bool _isDraggingStartPoint;
        private bool _isDraggingEndPoint;

        private bool _disableSnappingForCurrentDrag;
        private AvaloniaControl _lastDraggedVectorControl;
        private int _lastDraggedHandleIndex = -1;
        private bool _isPanning;
        private AvaloniaPoint _panStartPoint;
        private Avalonia.Vector _panStartOffset;

        private sealed class EditorSnapshot
        {
            public ImageSharpImage Image { get; init; }
            public List<AvaloniaControl> Annotations { get; init; } = new List<AvaloniaControl>();
        }

        private Slider _inlinePixelateSlider;
        private TextBlock _pixelatePercentText;
        private StackPanel _pixelateToolbarSection;
        private Canvas _pixelateStrengthHandle;
        private bool _isDraggingStrengthHandle;

        private int _lastPixelateStrength = PixelateStrengthDefault;
        private int _currentZIndex = 5000;
        
        private void BringToFront(AvaloniaControl control)
        {
            if (control != null)
            {
                control.ZIndex = _currentZIndex++;
            }
        }
        private Avalonia.Controls.Shapes.Rectangle _hoverIndicator;
        private Canvas _snapDotsLayer;
        private readonly List<AvaloniaPoint> _snapDotTargets = new List<AvaloniaPoint>();
        private Border _vectorInfoPopup;
        private TextBlock _vectorInfoText;
        private AvaloniaControl _hoveredControl;
        private const int PixelateStrengthMin = 2;
        private const int PixelateStrengthMax = 29;
        private const int PixelateStrengthDefault = 25;
        private const double VectorHitTolerance = 12.0;
        private const double VectorSnapThreshold = 12.0;
        // FEATURE (magnet escape): resize handles use a tighter capture zone than shape moves -
        // while enlarging, the dragged edge sweeps across many other shapes' edge targets, and a
        // wide zone there chains from one capture straight into the next, feeling unreleasable.
        private const double ResizeSnapThreshold = 6.0;
        private const double VectorSnapGap = 12.0;
        private const double PastedSnapThreshold = 3.0;
        private const int SnapVoxFrameThickness = 3;

        // FEATURE (tool ghosts): a translucent preview of the active tool's item floats under the
        // pointer instead of a bare crosshair (the selected emoji travels with the mouse, a pencil
        // is shown for the pen, ...). Esc / right-click exits the tool and drops the ghost.
        private static readonly Avalonia.Input.Cursor NoneCursor = new(StandardCursorType.None);
        private Canvas _toolGhostLayer;
        private AvaloniaControl _toolGhost;
        private bool _ghostSettingsDirty = true;
        private readonly ScaleTransform _ghostIconScale = new ScaleTransform(1, 1);

        // FEATURE (shape snap guides): dashed guide lines stretching between the shape being
        // drawn/dragged/placed and the shape it magnetically aligns with, shown before release.
        private Canvas _snapGuideLayer;
        private readonly List<Avalonia.Controls.Shapes.Line> _snapGuideLines = new List<Avalonia.Controls.Shapes.Line>();
        private readonly List<SnapGuideInfo> _activeSnapGuides = new List<SnapGuideInfo>();
        private AvaloniaPoint _previewSnapDelta;

        private readonly struct SnapGuideInfo
        {
            public readonly bool Horizontal; // true: the line runs left-right at a fixed Y
            public readonly double Coordinate;
            public readonly double Start;
            public readonly double End;

            public SnapGuideInfo(bool horizontal, double coordinate, double start, double end)
            {
                Horizontal = horizontal;
                Coordinate = coordinate;
                Start = start;
                End = end;
            }
        }

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
            _contextToolbar = this.FindControl<Border>("ContextToolbar");
            _startHandle = this.FindControl<Canvas>("StartPointHandle");
            _endHandle = this.FindControl<Canvas>("EndPointHandle");
            
            _inlinePixelateSlider = this.FindControl<Slider>("InlinePixelateSlider");
            _pixelatePercentText = this.FindControl<TextBlock>("PixelatePercentText");
            _pixelateToolbarSection = this.FindControl<StackPanel>("PixelateToolbarSection");
            _pixelateStrengthHandle = this.FindControl<Canvas>("PixelateStrengthHandle");

            var config = IniConfig.GetIniSection<CoreConfiguration>();
            _showFirstRunHints = config.IsFirstLaunch;
            if (_showFirstRunHints)
            {
                config.IsFirstLaunch = false;
                IniConfig.Save();
            }

            _lastPixelateStrength = NormalizePixelateStrength(config.LastPixelateStrength);
            config.LastPixelateStrength = _lastPixelateStrength;
            
            if (_inlinePixelateSlider != null) 
            {
                _inlinePixelateSlider.Minimum = PixelateStrengthMin;
                _inlinePixelateSlider.Maximum = PixelateStrengthMax;
                _inlinePixelateSlider.Value = _lastPixelateStrength;
                _inlinePixelateSlider.PropertyChanged += OnPixelateSliderChanged;
            }

            InitializeToolDefaults();
            ApplyFirstRunHints(_showFirstRunHints);
            UpdateModeStatus();
            UpdateContextToolbarHotkeyTooltips();
            RefreshColorPresetsPanel();
            InitializeCustomColorFlyout();
            UpdateSnapToggleVisual();
            PopulateEmojiGrid();

            Closed += OnWindowClosed;
            Opened += OnWindowOpened;
            Closing += OnWindowClosing;
        }

        private static readonly string[] EmojiPalette =
        {
            "😀","😁","😂","🤣","😉","😊","😍","😘","😜","😎","😏","😒","😔","😕","😖","😫",
            "🥺","😢","😭","😤","😡","🤬","🤯","😳","🥵","🥶","😱","😨","😰","😥","😓","🤔",
            "🤗","🤭","🤫","🤥","😶","😐","😑","😬","🙄","😯","😦","😧","😮","😲","🥱","😴",
            "🤤","😪","😵","🤐","🥴","🤢","🤮","🤧","😷","🤒","🤕","🤑","🤠","😈","👿","👹",
            "👺","🤡","💩","👻","💀","👽","👾","🤖","🎃","😺","😸","😹","😻","😼","😽","🙀",
            "😿","😾","👍","👎","💯","🔥","⭐","📍","✅","❌","⚠️","❗","➡️","⬅️","⬆️","⬇️",
            "💡","🔒","👀","🎯"
        };

        private void PopulateEmojiGrid()
        {
            var grid = this.FindControl<ItemsControl>("EmojiGrid");
            if (grid != null) grid.ItemsSource = EmojiPalette;
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

            _hoverIndicator = new Avalonia.Controls.Shapes.Rectangle
            {
                Stroke = Brushes.DeepSkyBlue,
                StrokeThickness = 2,
                StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 3, 2 },
                IsVisible = false,
                IsHitTestVisible = false,
                ZIndex = 9997
            };
            _canvas.Children.Add(_hoverIndicator);

            _vectorInfoText = new TextBlock
            {
                Foreground = Brushes.White,
                FontSize = 11,
                FontWeight = FontWeight.SemiBold
            };
            _vectorInfoPopup = new Border
            {
                Background = new SolidColorBrush(AvaloniaColor.FromArgb(215, 45, 45, 48)),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 2),
                Child = _vectorInfoText,
                IsVisible = false,
                IsHitTestVisible = false,
                ZIndex = 10003
            };
            _canvas.Children.Add(_vectorInfoPopup);

            // FEATURE (tool ghosts + shape snap guides): two hit-test transparent overlay layers.
            _toolGhostLayer = new Canvas { IsHitTestVisible = false, IsVisible = false, ZIndex = 10004 };
            _canvas.Children.Add(_toolGhostLayer);
            _snapGuideLayer = new Canvas { IsHitTestVisible = false, IsVisible = false, ZIndex = 9996 };
            _canvas.Children.Add(_snapGuideLayer);
            _canvas.PointerExited += OnCanvasPointerExited;

            for (int i = 0; i < 4; i++)
            {
                var handle = new Avalonia.Controls.Shapes.Rectangle {
                    Width = 14, Height = 14, Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1, 
                    IsVisible = false, ZIndex = 10000, Tag = i
                };
                
                if (i == 0 || i == 2) handle.Cursor = TopLeftCursor;
                else handle.Cursor = TopRightCursor; 
                
                handle.PointerPressed += (s, e) => { 
                    _isResizing = true; 
                    _resizeUndoCaptured = false; 
                    int idx = (int)((AvaloniaControl)s).Tag;
                    if (_selectedControl == _lastDraggedVectorControl && _lastDraggedHandleIndex == idx + 10) _disableSnappingForCurrentDrag = !_disableSnappingForCurrentDrag;
                    else _disableSnappingForCurrentDrag = false;
                    _lastDraggedVectorControl = _selectedControl; _lastDraggedHandleIndex = idx + 10;
                    _resizeHandleIndex = idx; 
                    _dragLastPoint = e.GetPosition(_canvas); 
                    e.Pointer.Capture(_canvas);
                    e.Handled = true; 
                };
                _resizeHandles.Add(handle); _canvas.Children.Add(handle);
            }

            _startHandle.PointerPressed += (s, e) => { 
                if (_selectedControl == _lastDraggedVectorControl && _lastDraggedHandleIndex == 1) _disableSnappingForCurrentDrag = !_disableSnappingForCurrentDrag;
                else _disableSnappingForCurrentDrag = false;
                _lastDraggedVectorControl = _selectedControl; _lastDraggedHandleIndex = 1;
                
                _isDraggingStartPoint = true; 
                _dragUndoCaptured = false; 
                _dragLastPoint = e.GetPosition(_canvas); 
                e.Pointer.Capture(_canvas);
                e.Handled = true; 
            };
            _endHandle.PointerPressed += (s, e) => { 
                if (_selectedControl == _lastDraggedVectorControl && _lastDraggedHandleIndex == 2) _disableSnappingForCurrentDrag = !_disableSnappingForCurrentDrag;
                else _disableSnappingForCurrentDrag = false;
                _lastDraggedVectorControl = _selectedControl; _lastDraggedHandleIndex = 2;

                _isDraggingEndPoint = true; 
                _dragUndoCaptured = false; 
                _dragLastPoint = e.GetPosition(_canvas); 
                e.Pointer.Capture(_canvas);
                e.Handled = true; 
            };

            if (_pixelateStrengthHandle != null)
            {
                _pixelateStrengthHandle.PointerPressed += (s, e) => {
                    _isDraggingStrengthHandle = true;
                    _dragUndoCaptured = false;
                    _dragLastPoint = e.GetPosition(_canvas);
                    e.Pointer.Capture(_canvas);
                    e.Handled = true;
                };
            }
        }

        private void OnSnapToggleClick(object sender, RoutedEventArgs e)
        {
            var config = IniConfig.GetIniSection<CoreConfiguration>();
            config.MagneticSnappingEnabled = !config.MagneticSnappingEnabled;
            IniConfig.Save();
            UpdateSnapToggleVisual();
            OverlayHelper.ShowLightToast(config.MagneticSnappingEnabled ? "MAGNETIC SNAPPING ON" : "MAGNETIC SNAPPING OFF", this);
        }

        private void UpdateSnapToggleVisual()
        {
            var snapBtn = this.FindControl<Button>("SnapToggleBtn");
            var config = IniConfig.GetIniSection<CoreConfiguration>();
            if (config != null && snapBtn != null) 
            {
                if (config.MagneticSnappingEnabled) snapBtn.Classes.Add("selected"); 
                else snapBtn.Classes.Remove("selected");
            }
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
            
            if (AvaloniaColor.TryParse(config.LastCounterColor ?? "#FF0000", out var counterColor))
                _toolBrushes[EditorTool.Counter] = new SolidColorBrush(counterColor);
            else
                _toolBrushes[EditorTool.Counter] = redBrush;
                
            if (config.LastCounterSize == 35.0) { config.LastCounterSize = 42.0; IniConfig.Save(); }
            _toolFontSizes[EditorTool.Counter] = config.LastCounterSize > 0 ? config.LastCounterSize : 42.0;

            _toolBrushes[EditorTool.Text] = new SolidColorBrush(AvaloniaColor.Parse("#0055AA"));
            _toolThicknesses[EditorTool.Text] = config.TextToolThickness;

            _toolBrushes[EditorTool.Highlight] = HighlightBrushBase;
            _toolThicknesses[EditorTool.Highlight] = 1.0;
            _toolThicknesses[EditorTool.Arrow] = 8.0;
            _toolThicknesses[EditorTool.Line] = 8.0;
            
            _toolFontSizes[EditorTool.Text] = Math.Max(20, config.LastTextSize);
            _toolFontSizes[EditorTool.Emoji] = Math.Max(60, config.LastEmojiSize);
            _toolFontSizes[EditorTool.Counter] = config.LastCounterSize > 0 ? config.LastCounterSize : 42.0;

            if (_toolBrushes.TryGetValue(EditorTool.Rectangle, out var b) && b is SolidColorBrush initialBrush)
            {
                UpdateCurrentColorDisplay(initialBrush.Color);
            }

            LoadToolPreferences(config);

            UpdateFillToggleVisual();
        }

        private void LoadToolPreferences(CoreConfiguration config)
        {
            if (!string.IsNullOrEmpty(config.LastToolColors))
            {
                var parts = config.LastToolColors.Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    var kv = part.Split(':');
                    if (kv.Length == 2 && Enum.TryParse(kv[0], out EditorTool tool))
                    {
                        if (AvaloniaColor.TryParse(kv[1], out var color))
                        {
                            _toolBrushes[tool] = new SolidColorBrush(color);
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(config.LastToolThicknesses))
            {
                var parts = config.LastToolThicknesses.Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    var kv = part.Split(':');
                    if (kv.Length == 2 && Enum.TryParse(kv[0], out EditorTool tool))
                    {
                        if (double.TryParse(kv[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double thickness))
                        {
                            _toolThicknesses[tool] = thickness;
                        }
                    }
                }
            }
        }

        private void SaveToolPreferences()
        {
            var config = IniConfig.GetIniSection<CoreConfiguration>();
            var colorsList = new System.Collections.Generic.List<string>();
            foreach (var kvp in _toolBrushes)
            {
                if (kvp.Value is SolidColorBrush scb)
                {
                    colorsList.Add($"{kvp.Key}:{scb.Color.ToString()}");
                }
            }
            config.LastToolColors = string.Join(",", colorsList);

            var thicknessList = new System.Collections.Generic.List<string>();
            foreach (var kvp in _toolThicknesses)
            {
                thicknessList.Add($"{kvp.Key}:{kvp.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            }
            config.LastToolThicknesses = string.Join(",", thicknessList);

            IniConfig.Save();
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
            if (e.Property.Name == "Value" && _inlinePixelateSlider != null)
            {
                int val = NormalizePixelateStrength((int)Math.Round(_inlinePixelateSlider.Value));
                if (Math.Abs(_inlinePixelateSlider.Value - val) > 0.01)
                {
                    _inlinePixelateSlider.Value = val;
                    return;
                }

                _lastPixelateStrength = val;
                var config = IniConfig.GetIniSection<CoreConfiguration>();
                config.LastPixelateStrength = val;

                if (_pixelatePercentText != null)
                {
                    double pct = (double)(val - PixelateStrengthMin) / (PixelateStrengthMax - PixelateStrengthMin);
                    _pixelatePercentText.Text = $"{(int)(pct * 100)}%";
                }
                
                if (IsPixelateControl(_selectedControl))
                {
                    if (_selectedControl.Tag is PixelateProperties props) props.Strength = val;
                    RefreshPixelateAnnotation(_selectedControl);
                    UpdateSelectionIndicator();
                }
            }
        }

        private void SyncPixelateSlider(int strength)
        {
            int val = NormalizePixelateStrength(strength);
            if (_inlinePixelateSlider != null && Math.Abs(_inlinePixelateSlider.Value - val) > 0.01)
            {
                _inlinePixelateSlider.Value = val;
            }

            if (_pixelatePercentText != null)
            {
                double pct = (double)(val - PixelateStrengthMin) / (PixelateStrengthMax - PixelateStrengthMin);
                _pixelatePercentText.Text = $"{(int)Math.Round(pct * 100)}%";
            }
        }

        private static int NormalizePixelateStrength(int value)
        {
            return Math.Clamp(value, PixelateStrengthMin, PixelateStrengthMax);
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
            return string.Equals(eString.Trim(), configHotkey.Trim(), StringComparison.OrdinalIgnoreCase);
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
                if (e.Key == Key.V) { OnPasteClick(null, null); e.Handled = true; return; }
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

            if ((e.Key == Key.Enter || e.Key == Key.Return) && _selectedControl != null)
            {
                var tag = _selectedControl.Tag?.ToString();
                if (tag == "CropObject" || tag == "PasteObject")
                {
                    OnContextApplyClick(null, null);
                    e.Handled = true;
                    return;
                }
            }

            if (e.Key == Key.Escape) { ResetToolsAndSelection(); e.Handled = true; return; }
            if (_selectedControl != null && MatchesHotkey(e, config.DeleteObjectHotkey)) { OnDeleteClick(null, null); e.Handled = true; return; }
            if (_selectedControl != null && MatchesHotkey(e, config.DuplicateObjectHotkey)) { OnDuplicateClick(null, null); e.Handled = true; return; }

            if (_selectedControl != null && (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Left || e.Key == Key.Right))
            {
                double moveBy = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 50.0 : 1.0;
                double dx = 0, dy = 0;
                if (e.Key == Key.Up) dy = -moveBy;
                if (e.Key == Key.Down) dy = moveBy;
                if (e.Key == Key.Left) dx = -moveBy;
                if (e.Key == Key.Right) dx = moveBy;
                NudgeSelectedControl(dx, dy);
                e.Handled = true;
                return;
            }

            if (MatchesHotkey(e, config.CounterHotkey)) { TriggerToolButton("CounterTool"); e.Handled = true; return; }
            if (MatchesHotkey(e, config.ArrowHotkey)) { TriggerToolButton("ArrowTool"); e.Handled = true; return; }
            if (MatchesHotkey(e, config.LineHotkey)) { TriggerToolButton("LineTool"); e.Handled = true; return; }
            if (MatchesHotkey(e, config.FreehandHotkey)) { TriggerToolButton("FreeDrawTool"); e.Handled = true; return; }
            if (MatchesHotkey(e, config.HighlightHotkey)) { TriggerToolButton("HighlightTool"); e.Handled = true; return; }

            if (MatchesHotkey(e, config.RotateCcwHotkey)) { OnRotateCcwClick(null, null); e.Handled = true; return; }
            else if (MatchesHotkey(e, config.RotateCwHotkey)) { OnRotateCwClick(null, null); e.Handled = true; return; }
            else if (MatchesHotkey(e, config.TextHotkey)) { TriggerToolButton("TextTool"); e.Handled = true; return; }
            else if (MatchesHotkey(e, config.ResizeHotkey)) { OnResizeClick(null, null); e.Handled = true; return; }
            else if (MatchesHotkey(e, config.EmojiHotkey)) { TriggerToolButton("EmojiTool"); e.Handled = true; return; }
            else if (MatchesHotkey(e, config.PixelateHotkey1) || MatchesHotkey(e, config.PixelateHotkey2)) { TriggerToolButton("BlurTool"); e.Handled = true; return; }
            else if (MatchesHotkey(e, config.CropHotkey)) 
            { 
                if (_selectedControl != null && _selectedControl.Tag as string == "CropObject")
                {
                    OnContextApplyClick(null, null);
                }
                else
                {
                    TriggerToolButton("CropTool"); 
                }
                e.Handled = true; 
                return; 
            }
        }

        private void ResetToolsAndSelection()
        {
            _currentTool = EditorTool.None;
            _selectedControl = null;
            RemovePreviewShape();
            UpdateSelectionIndicator();
            UpdateThicknessPanelVisibility();
            Cursor = HandCursor;
            var toolBar = this.FindControl<StackPanel>("LeftToolBar");
            if (toolBar != null) { foreach (var child in toolBar.Children) { if (child is Button b) b.Classes.Remove("selected"); } }
            this.FindControl<Button>("CropTool")?.Classes.Remove("selected");
            HideToolGhost();
            HideSnapGuides();
            _isFillMode = false;
            UpdateFillToggleVisual();
            UpdateModeStatus();
        }
        
        private void TriggerToolButton(string name)
        {
            var btn = this.FindControl<Button>(name);
            if (btn != null && btn.IsEnabled) OnToolClick(btn, new RoutedEventArgs());
        }

        private CancellationTokenSource _zoomHintCts;

        private async void ShowZoomHintDebounced()
        {
            _zoomHintCts?.Cancel();
            _zoomHintCts = new CancellationTokenSource();
            var ct = _zoomHintCts.Token;

            try
            {
                await Task.Delay(500, ct);
                OverlayHelper.ShowLightToast($"Zoom: {(int)(_zoomFactor * 100)}% (Middle-drag to pan)", this);
            }
            catch (TaskCanceledException) { }
        }

        // IDEA_005 (Scroll-Cycle): Alt+scroll cycles tools in place without moving the cursor.
        private static readonly EditorTool[] ScrollCycleTools =
        {
            EditorTool.Arrow, EditorTool.Line, EditorTool.Rectangle, EditorTool.Ellipse,
            EditorTool.FreeDraw, EditorTool.Highlight, EditorTool.Blur,
            EditorTool.Text, EditorTool.Counter, EditorTool.Emoji
        };

        private void OnPointerWheelChanged(object sender, PointerWheelEventArgs e)
        {
            // IDEA_005: Alt+scroll = tool cycling express lane.
            if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) && !e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                CycleToolInPlace(e.Delta.Y > 0);
                e.Handled = true;
                return;
            }

            // FEATURE (wheel zoom): the middle wheel ALWAYS zooms in/out, no matter which tool
            // is active or which modifier is held (Alt+scroll tool cycling was handled above).
            // The ScrollViewer therefore never receives plain wheel events for up/down
            // scrolling anymore; panning stays available via middle-drag.
            {
                var scrollViewer = this.FindControl<ScrollViewer>("EditorScrollViewer");
                if (scrollViewer == null) return;

                // PRODUCT DECISION (top-left zoom anchor): the wheel anchors the TOP-LEFT corner
                // exactly like the toolbar buttons. Cursor-pinned zooming dug toward wherever the
                // pointer happened to rest (usually the bottom-right of the viewport), visibly
                // detaching the picture from the top-left corner on every step.
                ZoomTowards(_zoomFactor + (e.Delta.Y > 0 ? 0.1 : -0.1));

                ShowZoomHintDebounced();
                e.Handled = true;
            }
        }

        private void CycleToolInPlace(bool forward)
        {
            var current = _currentTool == EditorTool.None ? EditorTool.Arrow : _currentTool;
            int idx = Array.IndexOf(ScrollCycleTools, current);
            if (idx < 0) idx = 0;

            idx = forward
                ? (idx + 1) % ScrollCycleTools.Length
                : (idx - 1 + ScrollCycleTools.Length) % ScrollCycleTools.Length;

            var next = ScrollCycleTools[idx];
            var buttonName = GetToolButtonName(next);
            if (!string.IsNullOrEmpty(buttonName))
            {
                TriggerToolButton(buttonName);
                OverlayHelper.ShowLightToast($"⚡ {GetToolDisplayName(next).ToUpper()}", this);
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

                var zoomContainer = this.FindControl<Panel>("ZoomContainer");
                if (zoomContainer != null)
                {
                    zoomContainer.Width = _image.Width * _zoomFactor;
                    zoomContainer.Height = _image.Height * _zoomFactor;
                    zoomContainer.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
                    zoomContainer.RenderTransform = new ScaleTransform(_zoomFactor, _zoomFactor);
                }

                var zoomText = this.FindControl<Button>("ZoomText");
                if (zoomText != null) zoomText.Content = $"{(int)(_zoomFactor * 100)}%";
                // Icon ghosts counter-scale with zoom so they stay a constant size on screen.
                _ghostIconScale.ScaleX = 1.0 / _zoomFactor;
                _ghostIconScale.ScaleY = 1.0 / _zoomFactor;
            }
        }

        // FEATURE (top-left anchored zoom): every zoom path - wheel, toolbar +/-, reset - anchors
        // the TOP-LEFT corner. The content point currently sitting at the viewport's top-left
        // stays exactly where it is: the offset is simply rescaled (offset * ratio), so zooming
        // IN always expands the picture toward the bottom-right and zooming OUT shrinks it back
        // toward the top-left. Combined with the ZoomContainer's Left/Top alignment and the
        // [0, content - viewport] clamp this makes voids at the top-left structurally
        // impossible - leftover space can only ever appear on the right/bottom sides, and only
        // when the zoomed content is smaller than the viewport.
        private void ZoomTowards(double requestedZoom)
        {
            double oldZoom = _zoomFactor;
            _zoomFactor = Math.Clamp(requestedZoom, 0.1, 4.0);
            if (Math.Abs(oldZoom - _zoomFactor) < 0.001) return;

            var scrollViewer = this.FindControl<ScrollViewer>("EditorScrollViewer");
            if (scrollViewer == null)
            {
                ApplyZoom();
                return;
            }

            var oldOffset = scrollViewer.Offset;
            ApplyZoom();

            // Top-left anchor: rescale the existing offset around the viewport's (0,0) corner.
            double ratio = _zoomFactor / oldZoom;
            double desiredX = oldOffset.X * ratio;
            double desiredY = oldOffset.Y * ratio;
            ApplyTopLeftAnchoredOffset(scrollViewer, desiredX, desiredY);

            // The new extent is only measured on the NEXT layout pass; the ScrollViewer may
            // re-clamp the offset against the stale extent in between and drift the picture
            // toward the bottom-right. Re-assert the desired offset once the new extent is real.
            Dispatcher.UIThread.Post(
                () => ApplyTopLeftAnchoredOffset(scrollViewer, desiredX, desiredY),
                DispatcherPriority.Loaded);
        }

        private void ApplyTopLeftAnchoredOffset(ScrollViewer scrollViewer, double desiredX, double desiredY)
        {
            if (_image == null) return;

            double contentWidth = _image.Width * _zoomFactor;
            double contentHeight = _image.Height * _zoomFactor;

            // Once the zoomed content fits inside the viewport on an axis there is nothing left
            // to scroll to - snap that axis to 0 so the picture hugs the top-left corner instead
            // of floating on a phantom scroll offset.
            double x = contentWidth <= scrollViewer.Viewport.Width + 0.5
                ? 0
                : Math.Clamp(desiredX, 0, Math.Max(0, contentWidth - scrollViewer.Viewport.Width));
            double y = contentHeight <= scrollViewer.Viewport.Height + 0.5
                ? 0
                : Math.Clamp(desiredY, 0, Math.Max(0, contentHeight - scrollViewer.Viewport.Height));
            scrollViewer.Offset = new Avalonia.Vector(x, y);
        }

        private bool _forceClose = false;
        
        private bool _isOcrInteractiveMode = false;
        private OcrInformation _interactiveOcrInfo;
        private List<OcrWord> _selectedOcrWords = new List<OcrWord>();
        private List<Avalonia.Controls.Control> _ocrVisuals = new List<Avalonia.Controls.Control>();
        private OcrWordSpatialIndex _interactiveOcrIndex;
        private int _ocrSelectionStartIndex = -1;
        private int _ocrSelectionEndIndex = -1;
        private int _lastOcrSelectionMin = -1;
        private int _lastOcrSelectionMax = -1;
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

        private void OnWindowOpened(object sender, EventArgs e)
        {
            UiClipboard.Register(this, text => Clipboard?.SetTextAsync(text) ?? Task.CompletedTask);

            // BUGFIX (oversized editor): PositionWindow() estimates the chrome before the
            // first layout pass. Once the window is open, measure the REAL viewport and
            // shrink the window to snip + actual chrome so small snips no longer open
            // inside the old oversized 850x650 shell.
            if (_autoFitPending)
            {
                Dispatcher.UIThread.Post(AutoFitWindowToContent, DispatcherPriority.Loaded);
            }
        }
        private void OnWindowClosed(object sender, EventArgs e) { UiClipboard.Unregister(this); ReleaseImageResources(); }

        public async void SetImage(ImageSharpImage image, RECT captureRect)
        {
            ReleaseImageResources();
            _image = image ?? throw new ArgumentNullException(nameof(image));
            await UpdateDisplayAsync().ConfigureAwait(true);
            PositionWindow(captureRect);
        }

        private async Task UpdateDisplayAsync()
        {
            if (_image == null) return;
            _displayBitmap?.Dispose();
            _displayBitmap = await _image.ToAvaloniaBitmapAsync().ConfigureAwait(true);
            if (_imageControl != null) { _imageControl.Source = _displayBitmap; _imageControl.Width = _image.Width; _imageControl.Height = _image.Height; }
            if (_canvas != null) { _canvas.Width = _image.Width; _canvas.Height = _image.Height; }
            ApplyZoom();
            RefreshAllPixelateAnnotations();
            RefreshAllHighlightAnnotations();
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
            SyncToolButtonSelection();

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

        private static string GetToolButtonName(EditorTool tool) => tool switch
        {
            EditorTool.Arrow => "ArrowTool",
            EditorTool.Line => "LineTool",
            EditorTool.Rectangle => "RectTool",
            EditorTool.Ellipse => "EllipseTool",
            EditorTool.FreeDraw => "FreeDrawTool",
            EditorTool.Highlight => "HighlightTool",
            EditorTool.Blur => "BlurTool",
            EditorTool.Text => "TextTool",
            EditorTool.Counter => "CounterTool",
            EditorTool.Emoji => "EmojiTool",
            EditorTool.Crop => "CropTool",
            _ => null
        };

        private void SyncToolButtonSelection()
        {
            var toolBar = this.FindControl<StackPanel>("LeftToolBar");
            if (toolBar != null)
            {
                foreach (var child in toolBar.Children)
                {
                    if (child is Button button) button.Classes.Remove("selected");
                }
            }

            this.FindControl<Button>("CropTool")?.Classes.Remove("selected");

            var activeTool = _currentTool != EditorTool.None
                ? _currentTool
                : _selectedControl != null ? GetToolFromControl(_selectedControl) : EditorTool.None;
            var buttonName = GetToolButtonName(activeTool);
            if (!string.IsNullOrEmpty(buttonName))
            {
                this.FindControl<Button>(buttonName)?.Classes.Add("selected");
            }
        }

        private bool IsEditorChrome(AvaloniaControl control)
        {
            return ReferenceEquals(control, _selectionIndicator)
                || ReferenceEquals(control, _hoverIndicator)
                || ReferenceEquals(control, _vectorInfoPopup)
                || _resizeHandles.Contains(control)
                || ReferenceEquals(control, _previewControl)
                || ReferenceEquals(control, _contextToolbar)
                || ReferenceEquals(control, _startHandle)
                || ReferenceEquals(control, _endHandle)
                || ReferenceEquals(control, _toolGhostLayer)
                || ReferenceEquals(control, _snapGuideLayer);
        }

        private List<AvaloniaControl> GetUserAnnotations()
        {
            return _canvas.Children.OfType<AvaloniaControl>().Where(c => !IsEditorChrome(c)).ToList();
        }

        private List<(AvaloniaControl Control, bool WasVisible)> SetEditorChromeVisible(bool visible)
        {
            var chrome = new List<AvaloniaControl>();
            if (_selectionIndicator != null) chrome.Add(_selectionIndicator);
            if (_hoverIndicator != null) chrome.Add(_hoverIndicator);
            if (_vectorInfoPopup != null) chrome.Add(_vectorInfoPopup);
            chrome.AddRange(_resizeHandles);
            if (_contextToolbar != null) chrome.Add(_contextToolbar);
            if (_startHandle != null) chrome.Add(_startHandle);
            if (_endHandle != null) chrome.Add(_endHandle);
            if (_toolGhostLayer != null) chrome.Add(_toolGhostLayer);
            if (_snapGuideLayer != null) chrome.Add(_snapGuideLayer);

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
            foreach (var child in GetUserAnnotations())
            {
                DisposeAnnotationResources(child);
                _canvas.Children.Remove(child);
            }
        }

        private static void DisposeAnnotationResources(AvaloniaControl control)
        {
            if (control == null) return;

            if (control is Avalonia.Controls.Image image)
            {
                if (image.Source is IDisposable source) source.Dispose();
                image.Source = null;
            }

            if (control is Avalonia.Controls.Shapes.Shape shape)
            {
                if (shape is Avalonia.Controls.Shapes.Rectangle rectangle && rectangle.Tag is PixelateProperties)
                {
                    rectangle.Resources["PixelateRequest"] = Interlocked.Increment(ref _pixelateCounter);
                }

                if (shape is Avalonia.Controls.Shapes.Rectangle highlightRect && highlightRect.Tag is HighlightProperties)
                {
                    highlightRect.Resources["HighlightRequest"] = Interlocked.Increment(ref _highlightCounter);
                }

                if (shape.Fill is ImageBrush fillBrush && fillBrush.Source is IDisposable fillSource) fillSource.Dispose();
                if (shape.Stroke is ImageBrush strokeBrush && strokeBrush.Source is IDisposable strokeSource) strokeSource.Dispose();
                if (shape.Fill is ImageBrush) shape.Fill = null;
                if (shape.Stroke is ImageBrush) shape.Stroke = null;
            }

            if (control is Border border && border.Child is AvaloniaControl child)
            {
                DisposeAnnotationResources(child);
            }

            if (control is Panel panel)
            {
                foreach (var panelChild in panel.Children.OfType<AvaloniaControl>().ToList())
                {
                    DisposeAnnotationResources(panelChild);
                }
            }
        }

        private void SaveUndoState(bool cloneImage = false)
        {
            _undoStack.AddLast(CreateSnapshot(cloneImage));
            if (_undoStack.Count > MaxStackSize)
            {
                var oldest = _undoStack.First.Value;
                _undoStack.RemoveFirst();
                DisposeSnapshot(oldest);
            }
            EnforceUndoMemoryBudget();
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

        private EditorSnapshot CreateSnapshot(bool cloneImage)
        {
            return new EditorSnapshot
            {
                Image = cloneImage ? _image?.Clone(x => { }) : null,
                Annotations = GetUserAnnotations().Select(CloneAnnotationControl).Where(c => c != null).ToList()
            };
        }

        private async void RestoreSnapshot(EditorSnapshot snapshot)
        {
            if (snapshot == null) return;

            RemoveUserAnnotations();
            _selectedControl = null;
            _isDraggingSelected = false;
            _isResizing = false;
            _activePolyline = null;

            if (snapshot.Image != null)
            {
                _image?.Dispose();
                _image = snapshot.Image.Clone(x => { });
                snapshot.Image.Dispose();
            }

            foreach (var annotation in snapshot.Annotations) _canvas.Children.Add(annotation);
            snapshot.Annotations.Clear();

            await UpdateDisplayAsync().ConfigureAwait(true);
            RefreshAllPixelateAnnotations();
            RefreshAllHighlightAnnotations();
            UpdateSelectionIndicator();
            UpdateThicknessPanelVisibility();
        }

        private void ClearSnapshotStack(LinkedList<EditorSnapshot> stack)
        {
            foreach (var snapshot in stack) DisposeSnapshot(snapshot);
            stack.Clear();
        }

        private static long EstimateSnapshotBytes(EditorSnapshot snapshot)
        {
            var image = snapshot?.Image;
            return image != null ? (long)image.Width * image.Height * 4 : 0;
        }

        private long TotalUndoHistoryBytes()
        {
            long total = 0;
            foreach (var snapshot in _undoStack) total += EstimateSnapshotBytes(snapshot);
            foreach (var snapshot in _redoStack) total += EstimateSnapshotBytes(snapshot);
            return total;
        }

        /// <summary>
        /// ISSUE_001: evicts the oldest undo entries while the combined undo/redo history exceeds
        /// the memory budget. The newest undo entry is always kept so undo still works.
        /// </summary>
        private void EnforceUndoMemoryBudget()
        {
            while (_undoStack.Count > 1 && TotalUndoHistoryBytes() > MaxUndoHistoryBytes)
            {
                var oldest = _undoStack.First.Value;
                _undoStack.RemoveFirst();
                DisposeSnapshot(oldest);
            }
        }

        private void DisposeSnapshot(EditorSnapshot snapshot)
        {
            snapshot?.Image?.Dispose();
            if (snapshot?.Annotations == null) return;
            foreach (var annotation in snapshot.Annotations)
            {
                DisposeAnnotationResources(annotation);
            }
            snapshot.Annotations.Clear();
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
                    StrokeJoin = line.StrokeJoin,
                    StrokeLineCap = line.StrokeLineCap,
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
                    Fill = CloneAnnotationBrush(rectangle.Fill),
                    Stroke = CloneAnnotationBrush(rectangle.Stroke),
                    StrokeThickness = rectangle.StrokeThickness,
                    Tag = rectangle.Tag,
                    IsHitTestVisible = rectangle.IsHitTestVisible
                },
                Avalonia.Controls.Shapes.Ellipse ellipse => new Avalonia.Controls.Shapes.Ellipse
                {
                    Width = ellipse.Width,
                    Height = ellipse.Height,
                    Fill = CloneAnnotationBrush(ellipse.Fill),
                    Stroke = CloneAnnotationBrush(ellipse.Stroke),
                    StrokeThickness = ellipse.StrokeThickness,
                    Tag = ellipse.Tag,
                    IsHitTestVisible = ellipse.IsHitTestVisible
                },
                Avalonia.Controls.Shapes.Polygon polygon => new Avalonia.Controls.Shapes.Polygon
                {
                    Points = new List<AvaloniaPoint>(polygon.Points),
                    Fill = CloneAnnotationBrush(polygon.Fill),
                    Stroke = CloneAnnotationBrush(polygon.Stroke),
                    StrokeThickness = polygon.StrokeThickness,
                    IsHitTestVisible = polygon.IsHitTestVisible
                },
                Canvas canvas => CloneAnnotationCanvas(canvas),
                Border border => CloneAnnotationBorder(border),
                Avalonia.Controls.Image image => new Avalonia.Controls.Image
                {
                    Source = CloneImageSource(image.Source),
                    Width = image.Width,
                    Height = image.Height,
                    Stretch = image.Stretch,
                    IsHitTestVisible = image.IsHitTestVisible
                },
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

        private static IBrush CloneAnnotationBrush(IBrush brush)
        {
            return brush is ImageBrush ? null : brush;
        }

        private static Avalonia.Media.IImage CloneImageSource(Avalonia.Media.IImage source)
        {
            if (source is not Bitmap bitmap) return null;

            using var ms = new MemoryStream();
            bitmap.Save(ms);
            ms.Position = 0;
            return new Bitmap(ms);
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
                TextWrapping = source.TextWrapping,
                Tag = CloneControlTag(source.Tag),
                IsHitTestVisible = source.IsHitTestVisible,
                TextAlignment = source.TextAlignment,
                VerticalContentAlignment = source.VerticalContentAlignment
            };
        }

        private void AttachTextBoxBehavior(Border border, TextBox textBox, CoreConfiguration config)
        {
            textBox.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(textBox.Text))
                {
                    DisposeAnnotationResources(border);
                    _canvas.Children.Remove(border);
                }
                else {
                    FitTextBoxToBorder(border, textBox);
                    textBox.IsReadOnly = true;
                    textBox.CaretBrush = Brushes.Transparent;
                    textBox.Cursor = SizeAllCursor;
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
                FitTextBoxToBorder(border, textBox);
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

        private static void SetTextBoxBoundsToBorder(Border border, TextBox textBox)
        {
            if (border == null || textBox == null) return;
            if (double.IsNaN(border.Width) || border.Width <= 0 || double.IsNaN(border.Height) || border.Height <= 0) return;

            double horizontalPadding = border.Padding.Left + border.Padding.Right + textBox.Padding.Left + textBox.Padding.Right + 4;
            double verticalPadding = border.Padding.Top + border.Padding.Bottom + textBox.Padding.Top + textBox.Padding.Bottom + 4;
            textBox.Width = Math.Max(20, border.Width - horizontalPadding);
            textBox.Height = Math.Max(20, border.Height - verticalPadding);
        }

        private static void FitTextBoxToBorder(Border border, TextBox textBox)
        {
            if (border == null || textBox == null) return;
            if (double.IsNaN(border.Width) || border.Width <= 0 || double.IsNaN(border.Height) || border.Height <= 0) return;

            SetTextBoxBoundsToBorder(border, textBox);
            double availableWidth = Math.Max(20, textBox.Width - textBox.Padding.Left - textBox.Padding.Right);
            double availableHeight = Math.Max(16, textBox.Height - textBox.Padding.Top - textBox.Padding.Bottom);
            double preferred = textBox.Tag is TextAnnotationProperties props && props.PreferredFontSize > 0
                ? props.PreferredFontSize
                : Math.Max(8, textBox.FontSize);
            double min = 8;
            double low = min;
            double high = Math.Max(200, preferred);

            for (int i = 0; i < 12; i++)
            {
                double mid = (low + high) / 2;
                if (DoesTextFit(textBox, mid, availableWidth, availableHeight)) low = mid;
                else high = mid;
            }

            textBox.FontSize = Math.Round(low, 1);
        }

        private static bool DoesTextFit(TextBox source, double fontSize, double availableWidth, double availableHeight)
        {
            var probe = new TextBlock
            {
                Text = string.IsNullOrEmpty(source.Text) ? " " : source.Text,
                FontSize = fontSize,
                FontWeight = source.FontWeight,
                FontFamily = source.FontFamily,
                TextWrapping = TextWrapping.Wrap,
                FlowDirection = source.FlowDirection,
                MaxWidth = availableWidth
            };

            probe.Measure(new AvaloniaSize(availableWidth, double.PositiveInfinity));
            return probe.DesiredSize.Width <= availableWidth + 0.5 && probe.DesiredSize.Height <= availableHeight + 0.5;
        }

        private void CopyCommonState(AvaloniaControl source, AvaloniaControl clone)
        {
            Canvas.SetLeft(clone, Canvas.GetLeft(source));
            Canvas.SetTop(clone, Canvas.GetTop(source));
            clone.Width = source.Width;
            clone.Height = source.Height;
            clone.Tag = CloneControlTag(source.Tag);
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

        private static object CloneControlTag(object tag)
        {
            if (tag is ArrowProperties arrow)
            {
                return new ArrowProperties { Start = arrow.Start, End = arrow.End };
            }

            if (tag is PixelateProperties pixelate)
            {
                return new PixelateProperties { Strength = pixelate.Strength };
            }

            if (tag is HighlightProperties highlight)
            {
                return new HighlightProperties { Color = highlight.Color };
            }

            if (tag is TextAnnotationProperties text)
            {
                return new TextAnnotationProperties { PreferredFontSize = text.PreferredFontSize };
            }

            return tag;
        }

        // BUGFIX (oversized editor): the real minimum usable shell - the left tool rail plus
        // a compact snip still fit, but a tiny snip no longer inflates the window to the
        // old 850x650 frame (spec item 13 updated accordingly).
        private const double MinEditorWindowWidth = 620;
        private const double MinEditorWindowHeight = 400;

        private bool _autoFitPending;
        private int _autoFitRetries;
        private PixelRect _autoFitWorkingArea;
        private double _autoFitScaling = 1.0;
        private double _autoFitMaxWidth;
        private double _autoFitMaxHeight;

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
            // BUGFIX (auto-size): the old formula divided the image's DIP size by the screen
            // scaling, shrinking the window on any high-DPI monitor (125%/150%), so a snip smaller
            // than the screen still needed manual enlarging before the whole picture was visible.
            // The picture is laid out 1:1 in DIPs and the working-area budget is converted to DIPs
            // exactly once. The chrome budget now accounts for the real editor furniture:
            // left tool rail (60) + snip frame (2 x 3) + scrollbar allowance (~18) + breathing
            // room on each axis; top = title bar (32) + main toolbar (60); bottom bar = 60.
            const double ChromeLeft = 60 + SnapVoxFrameThickness * 2 + 18 + 12;
            const double ChromeRight = SnapVoxFrameThickness * 2 + 18 + 12;
            const double ChromeTop = 32 + 60 + SnapVoxFrameThickness * 2 + 18 + 12;
            const double ChromeBottom = 60 + SnapVoxFrameThickness * 2 + 18 + 12;

            double maxAllowedWidth = (targetScreen.WorkingArea.Width - 24) / scaling;
            double maxAllowedHeight = (targetScreen.WorkingArea.Height - 24) / scaling;

            double targetWidth = _image.Width * _zoomFactor + ChromeLeft + ChromeRight;
            double targetHeight = _image.Height * _zoomFactor + ChromeTop + ChromeBottom;

            if (targetWidth < MinEditorWindowWidth) targetWidth = MinEditorWindowWidth;
            if (targetHeight < MinEditorWindowHeight) targetHeight = MinEditorWindowHeight;
            
            if (targetWidth > maxAllowedWidth) targetWidth = maxAllowedWidth;
            if (targetHeight > maxAllowedHeight) targetHeight = maxAllowedHeight;
            
            Width = targetWidth; Height = targetHeight;
            
            double centerPhysicalX = targetScreen.WorkingArea.X + (targetScreen.WorkingArea.Width - (Width * scaling)) / 2;
            double centerPhysicalY = targetScreen.WorkingArea.Y + (targetScreen.WorkingArea.Height - (Height * scaling)) / 2;
            
            int cascade = (_cascadeOffset++ % 5) * 20;
            Position = new PixelPoint((int)(centerPhysicalX + cascade), (int)(centerPhysicalY + cascade));

            // BUGFIX (oversized editor): remember the budget so AutoFitWindowToContent can
            // correct the estimated chrome with the real viewport after the first layout.
            _autoFitPending = true;
            _autoFitRetries = 0;
            _autoFitWorkingArea = targetScreen.WorkingArea;
            _autoFitScaling = scaling;
            _autoFitMaxWidth = maxAllowedWidth;
            _autoFitMaxHeight = maxAllowedHeight;
        }

        private void AutoFitWindowToContent()
        {
            var scroller = this.FindControl<ScrollViewer>("EditorScrollViewer");
            if (_image == null || scroller == null || scroller.Viewport.Width <= 1 || scroller.Viewport.Height <= 1)
            {
                // Layout not measured yet - retry briefly on subsequent passes, then give up.
                if (++_autoFitRetries < 10) Dispatcher.UIThread.Post(AutoFitWindowToContent, DispatcherPriority.Loaded);
                return;
            }

            double chromeWidth = Width - scroller.Viewport.Width;
            double chromeHeight = Height - scroller.Viewport.Height;
            double desiredWidth = Math.Clamp(chromeWidth + _image.Width * _zoomFactor, MinEditorWindowWidth, _autoFitMaxWidth);
            double desiredHeight = Math.Clamp(chromeHeight + _image.Height * _zoomFactor, MinEditorWindowHeight, _autoFitMaxHeight);
            if (Math.Abs(desiredWidth - Width) <= 2 && Math.Abs(desiredHeight - Height) <= 2) return;

            Width = desiredWidth;
            Height = desiredHeight;

            // Keep the corrected window centred on the screen that owns the snip.
            double centerX = _autoFitWorkingArea.X + (_autoFitWorkingArea.Width - Width * _autoFitScaling) / 2;
            double centerY = _autoFitWorkingArea.Y + (_autoFitWorkingArea.Height - Height * _autoFitScaling) / 2;
            Position = new PixelPoint((int)centerX, (int)centerY);
        }

        private void ReleaseImageResources()
        {
            RemovePreviewShape();
            RemoveUserAnnotations();
            _image?.Dispose();
            _image = null;
            _displayBitmap?.Dispose();
            _displayBitmap = null;
            ClearSnapshotStack(_undoStack);
            ClearSnapshotStack(_redoStack);
        }
        private void InitializeComponent() { AvaloniaXamlLoader.Load(this); }
        // PRODUCT DECISION (top-left zoom anchor): toolbar buttons, wheel and reset all anchor
        // the TOP-LEFT corner - zooming in expands toward the bottom-right and zooming out
        // shrinks back toward the top-left, so voids can only ever open on the right/bottom.
        private void OnZoomInClick(object sender, RoutedEventArgs e) { ZoomTowards(_zoomFactor + 0.1); ShowZoomHintDebounced(); }
        private void OnZoomOutClick(object sender, RoutedEventArgs e) { ZoomTowards(_zoomFactor - 0.1); ShowZoomHintDebounced(); }
        private void OnZoomResetButtonClick(object sender, RoutedEventArgs e) { ZoomTowards(1.0); ShowZoomHintDebounced(); }
        private void OnCounterDoubleTapped(object sender, TappedEventArgs e) { _counterValue = 1; }
        private void OnCounterResetClick(object sender, RoutedEventArgs e) { _counterValue = 1; OverlayHelper.ShowLightToast("COUNTER RESET TO 1", this); }
        private void OnCounterUpClick(object sender, RoutedEventArgs e)
        {
            if (_selectedControl is Border border && border.Child is TextBlock tb && GetToolFromControl(_selectedControl) == EditorTool.Counter)
            {
                if (int.TryParse(tb.Text, out int val))
                {
                    val++;
                    tb.Text = val.ToString();
                    _counterValue = val + 1;
                    OverlayHelper.ShowLightToast($"COUNTER: {val}", this);
                }
            }
            else
            {
                _counterValue++;
                OverlayHelper.ShowLightToast($"NEXT COUNTER: {_counterValue}", this);
            }
        }
        private void OnCounterDownClick(object sender, RoutedEventArgs e)
        {
            if (_selectedControl is Border border && border.Child is TextBlock tb && GetToolFromControl(_selectedControl) == EditorTool.Counter)
            {
                if (int.TryParse(tb.Text, out int val))
                {
                    val--;
                    tb.Text = val.ToString();
                    _counterValue = val + 1;
                    OverlayHelper.ShowLightToast($"COUNTER: {val}", this);
                }
            }
            else
            {
                _counterValue = Math.Max(1, _counterValue - 1);
                OverlayHelper.ShowLightToast($"NEXT COUNTER: {_counterValue}", this);
            }
        }

        private void OnCanvasPointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (_isBusy) return;

            var pos = e.GetPosition(_canvas);
            
            if (_isOcrInteractiveMode)
            {
                if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                {
                    _ocrSelectionStartIndex = FindClosestOcrWordIndex(pos);
                    _ocrSelectionEndIndex = _ocrSelectionStartIndex;
                    UpdateOcrSelectionVisuals(true);
                    e.Handled = true;
                }
                return;
            }

            if (e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed)
            {
                // FEATURE (hand pan): middle-click no longer resets the zoom; it immediately
                // becomes the moving hand (same behaviour as dragging in no-tool mode) so an
                // image larger than the window can be scrolled in any direction by dragging.
                var panScrollViewer = this.FindControl<ScrollViewer>("EditorScrollViewer");
                if (panScrollViewer != null)
                {
                    _isPanning = true;
                    _panStartPoint = e.GetPosition(this);
                    _panStartOffset = panScrollViewer.Offset;
                    e.Pointer.Capture(_canvas);
                    Cursor = PanHeldCursor;
                    HideToolGhost();
                    HideSnapGuides();
                }
                e.Handled = true;
                return;
            }

            if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed) { OnWindowKeyDown(this, new KeyEventArgs { Key = Key.Escape }); e.Handled = true; return; }

            var clicked = FindSelectableControlAt(pos);
            if (_currentTool != EditorTool.None && IsPastedImageControl(clicked))
            {
                clicked = null;
            }

            if (clicked != null)
            {
                if (!ReferenceEquals(clicked, _selectedControl))
                {
                    FinalizeSelectedPasteObject();
                    _disableSnappingForCurrentDrag = false;
                }
                else if (clicked == _lastDraggedVectorControl && _lastDraggedHandleIndex == 0)
                {
                    _disableSnappingForCurrentDrag = !_disableSnappingForCurrentDrag;
                }
                else
                {
                    _disableSnappingForCurrentDrag = false;
                }
                _lastDraggedVectorControl = clicked; _lastDraggedHandleIndex = 0;

                _selectedControl = clicked; 
                BringToFront(_selectedControl); 
                _isDraggingSelected = true; 
                _dragUndoCaptured = false; 
                _dragLastPoint = pos; 
                _dragUnsnappedLeft = Canvas.GetLeft(_selectedControl);
                if (double.IsNaN(_dragUnsnappedLeft)) _dragUnsnappedLeft = _selectedControl.Bounds.X;
                _dragUnsnappedTop = Canvas.GetTop(_selectedControl);
                if (double.IsNaN(_dragUnsnappedTop)) _dragUnsnappedTop = _selectedControl.Bounds.Y;
                UpdateSelectionIndicator(); 
                UpdateHoverIndicator(null);
                
                IBrush existingBrush = null;
                if (_selectedControl is Avalonia.Controls.Shapes.Shape s) { 
                    _currentThickness = s.StrokeThickness; 
                    existingBrush = s.Stroke ?? s.Fill;
                }
                else if (_selectedControl is Canvas group && group.Tag is ArrowProperties)
                {
                    var arrowLine = group.Children.OfType<Avalonia.Controls.Shapes.Line>().FirstOrDefault();
                    var arrowHead = group.Children.OfType<Avalonia.Controls.Shapes.Polygon>().FirstOrDefault();
                    if (arrowLine != null)
                    {
                        _currentThickness = arrowLine.StrokeThickness;
                        existingBrush = arrowLine.Stroke ?? arrowHead?.Fill;
                    }
                }
                else if (_selectedControl is Border b) existingBrush = b.BorderBrush ?? b.Background;
                else if (_selectedControl is TextBlock tb) existingBrush = tb.Foreground;

                if (existingBrush is SolidColorBrush scb)
                {
                    UpdateCurrentColorDisplay(scb.Color);
                }

                Cursor = HandCursor;
                UpdateThicknessPanelVisibility();
                e.Handled = true;
                return;
            }

            if (_currentTool != EditorTool.None)
            {
                FinalizeSelectedPasteObject();
                _selectedControl = null;
                UpdateSelectionIndicator();
                UpdateHoverIndicator(null);
                HideVectorInfo();
                
                _isDrawing = true; _startPoint = pos; var brush = _toolBrushes[_currentTool];
                _previewSnapDelta = default;
                _disableSnappingForCurrentDrag = false;
                HideToolGhost(); // the rubberband/annotation itself replaces the ghost while drawing
                Cursor = CrossCursor;
                if (_currentTool == EditorTool.Counter) { PlaceCounter(ApplyStampSnap(EditorTool.Counter, _startPoint), brush); _isDrawing = false; _ghostSettingsDirty = true; }
                else if (_currentTool == EditorTool.Emoji) { PlaceEmoji(ApplyStampSnap(EditorTool.Emoji, _startPoint), _pendingEmoji); _isDrawing = false; _ghostSettingsDirty = true; }
                else if (_currentTool == EditorTool.Text) { PlaceText(ApplyStampSnap(EditorTool.Text, _startPoint), brush); _isDrawing = false; }
                else if (_currentTool == EditorTool.FreeDraw) { _activePolyline = new Avalonia.Controls.Shapes.Polyline { Stroke = brush, StrokeThickness = _currentThickness, IsHitTestVisible = true, ZIndex = _currentZIndex++ }; Canvas.SetLeft(_activePolyline, _startPoint.X); Canvas.SetTop(_activePolyline, _startPoint.Y); _activePolyline.Points.Add(new AvaloniaPoint(0, 0)); AddAnnotation(_activePolyline); }
                else BeginPreviewShape(_startPoint, brush);
                e.Pointer.Capture(_canvas);
                return;
            }
            
            FinalizeSelectedPasteObject();
            _selectedControl = null; 
            UpdateSelectionIndicator(); 
            UpdateHoverIndicator(null);

            var scrollViewer = this.FindControl<ScrollViewer>("EditorScrollViewer");
            if (scrollViewer != null)
            {
                _isPanning = true;
                _panStartPoint = e.GetPosition(this);
                _panStartOffset = scrollViewer.Offset;
                e.Pointer.Capture(_canvas);
            }

            // FEATURE (hand pan): no-tool mode is the "hold the picture" mode - a regular
            // hand while hovering, and a holding/moving hand while the pan drag is active.
            Cursor = _isPanning ? PanHeldCursor : HandCursor;
            UpdateThicknessPanelVisibility();
        }

        private double PointToLineDistance(AvaloniaPoint p, AvaloniaPoint a, AvaloniaPoint b)
        {
            double l2 = Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2);
            if (l2 == 0) return Distance(p, a);
            double t = ((p.X - a.X) * (b.X - a.X) + (p.Y - a.Y) * (b.Y - a.Y)) / l2;
            t = Math.Max(0, Math.Min(1, t));
            return Distance(p, new AvaloniaPoint(a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y)));
        }

        private static double Distance(AvaloniaPoint a, AvaloniaPoint b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static double CanvasLeft(AvaloniaControl control)
        {
            double value = Canvas.GetLeft(control);
            return double.IsNaN(value) ? 0 : value;
        }

        private static double CanvasTop(AvaloniaControl control)
        {
            double value = Canvas.GetTop(control);
            return double.IsNaN(value) ? 0 : value;
        }

        private static bool IsArrowControl(AvaloniaControl control)
        {
            return control is Canvas canvas && canvas.Tag is ArrowProperties;
        }

        private static bool IsVectorControl(AvaloniaControl control)
        {
            return control is Avalonia.Controls.Shapes.Line || IsArrowControl(control);
        }

        private static bool IsPixelateControl(AvaloniaControl control)
        {
            return control is Avalonia.Controls.Shapes.Rectangle rectangle && rectangle.Tag is PixelateProperties;
        }

        private static bool IsHighlightControl(AvaloniaControl control)
        {
            return control is Avalonia.Controls.Shapes.Rectangle rectangle && rectangle.Tag is HighlightProperties;
        }

        private static bool IsPastedImageControl(AvaloniaControl control)
        {
            string tag = control?.Tag?.ToString();
            return control is Avalonia.Controls.Image && (tag == "PasteObject" || tag == "PastedImage");
        }

        private void FinalizeSelectedPasteObject()
        {
            if (_selectedControl?.Tag?.ToString() == "PasteObject")
            {
                _selectedControl.Tag = "PastedImage";
            }
        }

        private AvaloniaPoint ApplyMagneticSnap(AvaloniaControl control, double left, double top, bool altSnapBypass = false)
        {
            var config = IniConfig.GetIniSection<CoreConfiguration>();
            // FEATURE (magnet escape): holding ALT is a live override - the shape follows the raw
            // un-snapped drag position for as long as it is held and snapping re-engages on release.
            if (altSnapBypass || !config.MagneticSnappingEnabled || _disableSnappingForCurrentDrag || !TryGetControlBounds(control, out var bounds))
            {
                HideSnapGuides();
                return new AvaloniaPoint(left, top);
            }

            // FEATURE (shape snap guides): any edge or the center of the dragged shape aligns with
            // any edge or center of every other shape (left/center/right and top/middle/bottom),
            // with dashed guide lines stretched between the two shapes until the mouse is released.
            var moving = new Rect(left, top, bounds.Width, bounds.Height);
            if (TrySnapRectToBoundsTargets(control, moving, out var snapDx, out var snapDy, _activeSnapGuides))
            {
                ShowSnapGuides();
                return new AvaloniaPoint(left + snapDx, top + snapDy);
            }

            HideSnapGuides();
            return new AvaloniaPoint(left, top);
        }

        private IEnumerable<double> GetMagneticSnapTargets(AvaloniaControl moving, bool horizontal)
        {
            if (_image != null)
            {
                yield return 0;
                yield return horizontal ? _image.Width : _image.Height;
            }

            foreach (var control in GetUserAnnotations())
            {
                if (ReferenceEquals(control, moving) || !TryGetControlBounds(control, out var bounds))
                {
                    continue;
                }

                if (horizontal)
                {
                    yield return bounds.Left;
                    yield return bounds.Right;
                }
                else
                {
                    yield return bounds.Top;
                    yield return bounds.Bottom;
                }
            }
        }

        private static double SnapAxisEdge(double position, IEnumerable<double> targets, double threshold)
        {
            double best = position;
            double bestDistance = threshold + 0.001;

            foreach (double target in targets)
            {
                double distance = Math.Abs(position - target);
                if (distance <= threshold && distance < bestDistance)
                {
                    bestDistance = distance;
                    best = target;
                }
            }

            return best;
        }

        // ===== FEATURE (shape snap guides) =================================================
        // Magnetic alignment between shape bounds: the moving shape's left/center/right and
        // top/middle/bottom can each align with any of those anchors on every other shape, plus
        // the image borders and the image's exact vertical/horizontal middle. When an alignment
        // is within threshold the shape shifts onto it and guide lines appear. Guides always
        // span the full image (like every classic design tool) instead of stretching from shape
        // A to shape B, so a line can never read as something glued to the moving shape itself.
        // Locking onto the image middle additionally pops a gentle full-length cross through
        // the exact center of the picture until the snap is released.

        private readonly struct ShapeSnapTarget
        {
            public readonly Rect Bounds;
            // True for the image's vertical-middle / horizontal-middle rails: matching one of
            // these triggers the center-cross guide.
            public readonly bool IsImageMiddle;
            public ShapeSnapTarget(Rect bounds, bool isImageMiddle = false)
            {
                Bounds = bounds;
                IsImageMiddle = isImageMiddle;
            }
        }

        private static bool IsStampTool(EditorTool tool) => tool is EditorTool.Text or EditorTool.Counter or EditorTool.Emoji;

        private static bool IsRectSnappableTool(EditorTool tool) => tool is EditorTool.Rectangle or EditorTool.Ellipse or EditorTool.Highlight or EditorTool.Blur;

        private IEnumerable<ShapeSnapTarget> GetShapeSnapTargets(AvaloniaControl moving)
        {
            if (_image != null)
            {
                double w = _image.Width;
                double h = _image.Height;
                // The full-image target supplies the left/right/top/bottom frame rails.
                yield return new ShapeSnapTarget(new Rect(0, 0, w, h));
                // Vertical-middle rail: a shape's left/center/right can lock to x = w/2 anywhere
                // along the line. Degenerate (zero-width) rect: Left == Center.X == Right == w/2.
                yield return new ShapeSnapTarget(new Rect(w / 2, 0, 0, h), isImageMiddle: true);
                // Horizontal-middle rail: a shape's top/middle/bottom can lock to y = h/2.
                yield return new ShapeSnapTarget(new Rect(0, h / 2, w, 0), isImageMiddle: true);
            }

            foreach (var control in GetUserAnnotations())
            {
                if (ReferenceEquals(control, moving) || !TryGetControlBounds(control, out var bounds)) continue;
                yield return new ShapeSnapTarget(bounds);
            }
        }

        private bool TrySnapRectToBoundsTargets(AvaloniaControl moving, Rect movingBounds, out double snapDx, out double snapDy, List<SnapGuideInfo> guides)
        {
            snapDx = 0;
            snapDy = 0;
            guides?.Clear();
            if (_image == null) return false;

            var targets = GetShapeSnapTargets(moving).ToList();
            bool snappedX = TrySnapAxis(vertical: false, movingBounds, targets, out snapDx, out var snapX);
            bool snappedY = TrySnapAxis(vertical: true, movingBounds, targets, out snapDy, out var snapY);

            if (guides == null) return snappedX || snappedY;

            // BUGFIX (guide geometry): guides used to stretch from shape A to shape B, which read
            // as a stray line attached to the moving shape whenever the alignment partner was the
            // image frame. Classic design tools draw rail guides across the whole canvas instead:
            // an X-axis alignment draws a vertical rail spanning the full image height, a Y-axis
            // alignment draws a horizontal rail spanning the full image width.
            if (snappedX) guides.Add(new SnapGuideInfo(false, snapX.Coordinate, 0, _image.Height));
            if (snappedY) guides.Add(new SnapGuideInfo(true, snapY.Coordinate, 0, _image.Width));

            // FEATURE (center cross): locking onto the image's vertical or horizontal middle pops
            // a gentle full-length cross through the exact center of the picture until release.
            if ((snappedX && snapX.IsMiddle) || (snappedY && snapY.IsMiddle))
            {
                AddGuideOnce(guides, new SnapGuideInfo(false, _image.Width / 2, 0, _image.Height));
                AddGuideOnce(guides, new SnapGuideInfo(true, _image.Height / 2, 0, _image.Width));
            }

            return snappedX || snappedY;
        }

        private static void AddGuideOnce(List<SnapGuideInfo> guides, SnapGuideInfo guide)
        {
            for (int i = 0; i < guides.Count; i++)
            {
                if (guides[i].Horizontal == guide.Horizontal && Math.Abs(guides[i].Coordinate - guide.Coordinate) < 0.5) return;
            }
            guides.Add(guide);
        }

        private readonly struct AxisSnapResult
        {
            public readonly double Coordinate;
            public readonly bool IsMiddle;
            public AxisSnapResult(double coordinate, bool isMiddle)
            {
                Coordinate = coordinate;
                IsMiddle = isMiddle;
            }
        }

        private static bool TrySnapAxis(bool vertical, Rect moving, List<ShapeSnapTarget> targets, out double delta, out AxisSnapResult snapped)
        {
            delta = 0;
            snapped = default;

            double[] mine =
            {
                vertical ? moving.Top : moving.Left,
                vertical ? moving.Center.Y : moving.Center.X,
                vertical ? moving.Bottom : moving.Right
            };

            double bestDistance = VectorSnapThreshold + 0.001;
            double bestDelta = 0;
            double bestCoordinate = 0;
            bool bestIsMiddle = false;
            bool found = false;

            foreach (var target in targets)
            {
                double[] theirs =
                {
                    vertical ? target.Bounds.Top : target.Bounds.Left,
                    vertical ? target.Bounds.Center.Y : target.Bounds.Center.X,
                    vertical ? target.Bounds.Bottom : target.Bounds.Right
                };

                // The middle coordinate this target can offer on the axis being tested (for the
                // degenerate middle rails this IS the rail coordinate; for ordinary shapes and the
                // image frame it is the bounds center, which is not treated as a center rail).
                double targetMiddle = vertical
                    ? target.Bounds.Top + target.Bounds.Height / 2
                    : target.Bounds.Left + target.Bounds.Width / 2;

                foreach (double m in mine)
                {
                    foreach (double t in theirs)
                    {
                        double distance = Math.Abs(m - t);
                        if (distance <= VectorSnapThreshold && distance < bestDistance)
                        {
                            bestDistance = distance;
                            bestDelta = t - m;
                            bestCoordinate = t;
                            bestIsMiddle = target.IsImageMiddle && Math.Abs(t - targetMiddle) < 0.5;
                            found = true;
                        }
                    }
                }
            }

            if (!found) return false;

            delta = bestDelta;
            // A Y-axis alignment draws a horizontal guide; an X-axis alignment draws a vertical one.
            snapped = new AxisSnapResult(bestCoordinate, bestIsMiddle);
            return true;
        }

        private void ApplyPreviewShapeSnap(AvaloniaPoint rawEnd, bool altSnapBypass = false)
        {
            var config = IniConfig.GetIniSection<CoreConfiguration>();
            if (altSnapBypass || !config.MagneticSnappingEnabled || _disableSnappingForCurrentDrag || _previewControl == null)
            {
                _previewSnapDelta = default;
                HideSnapGuides();
                return;
            }

            double minX = Math.Min(_startPoint.X, rawEnd.X);
            double minY = Math.Min(_startPoint.Y, rawEnd.Y);
            double width = Math.Max(1, Math.Abs(rawEnd.X - _startPoint.X));
            double height = Math.Max(1, Math.Abs(rawEnd.Y - _startPoint.Y));
            var raw = new Rect(minX, minY, width, height);

            if (TrySnapRectToBoundsTargets(null, raw, out var dx, out var dy, _activeSnapGuides))
            {
                _previewSnapDelta = new AvaloniaPoint(dx, dy);
                Canvas.SetLeft(_previewControl, minX + dx);
                Canvas.SetTop(_previewControl, minY + dy);
                ShowSnapGuides();
            }
            else
            {
                _previewSnapDelta = default;
                HideSnapGuides();
            }
        }

        private AvaloniaPoint ApplyStampSnap(EditorTool tool, AvaloniaPoint pos)
        {
            var config = IniConfig.GetIniSection<CoreConfiguration>();
            if (!config.MagneticSnappingEnabled || !TryGetStampSnapRect(tool, pos, out var rect))
            {
                HideSnapGuides();
                return pos;
            }

            if (TrySnapRectToBoundsTargets(null, rect, out var dx, out var dy, _activeSnapGuides))
            {
                ShowSnapGuides();
                return new AvaloniaPoint(pos.X + dx, pos.Y + dy);
            }

            HideSnapGuides();
            return pos;
        }

        private bool TryGetStampSnapRect(EditorTool tool, AvaloniaPoint pos, out Rect rect)
        {
            rect = default;
            var config = IniConfig.GetIniSection<CoreConfiguration>();
            switch (tool)
            {
                case EditorTool.Counter:
                {
                    double size = config.LastCounterSize > 0 ? config.LastCounterSize : 42;
                    rect = new Rect(pos.X - size / 2, pos.Y - size / 2, size, size);
                    return true;
                }
                case EditorTool.Emoji:
                {
                    // PlaceEmoji anchors the glyph's top-left 16 DIPs above-left of the click point.
                    double size = Math.Max(20, config.LastEmojiSize);
                    rect = new Rect(pos.X - 16, pos.Y - 16, size, size);
                    return true;
                }
                case EditorTool.Text:
                    rect = new Rect(pos.X, pos.Y, 140, 36);
                    return true;
                default:
                    return false;
            }
        }

        private void ShowSnapGuides()
        {
            if (_snapGuideLayer == null) return;

            while (_snapGuideLines.Count < _activeSnapGuides.Count)
            {
                var line = new Avalonia.Controls.Shapes.Line
                {
                    Stroke = Brushes.DeepSkyBlue,
                    StrokeLineCap = PenLineCap.Round,
                    IsHitTestVisible = false
                };
                _snapGuideLines.Add(line);
                _snapGuideLayer.Children.Add(line);
            }
            while (_snapGuideLines.Count > _activeSnapGuides.Count)
            {
                var surplus = _snapGuideLines[_snapGuideLines.Count - 1];
                _snapGuideLines.RemoveAt(_snapGuideLines.Count - 1);
                _snapGuideLayer.Children.Remove(surplus);
            }

            double zoom = Math.Max(0.1, _zoomFactor);
            double thickness = 1.0 / zoom; // one on-screen pixel regardless of zoom

            for (int i = 0; i < _activeSnapGuides.Count; i++)
            {
                var info = _activeSnapGuides[i];
                var line = _snapGuideLines[i];
                line.StrokeThickness = thickness;
                line.StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 5.0 / zoom, 4.0 / zoom };
                line.Opacity = 0.85;
                if (info.Horizontal)
                {
                    line.StartPoint = new AvaloniaPoint(info.Start, info.Coordinate);
                    line.EndPoint = new AvaloniaPoint(info.End, info.Coordinate);
                }
                else
                {
                    line.StartPoint = new AvaloniaPoint(info.Coordinate, info.Start);
                    line.EndPoint = new AvaloniaPoint(info.Coordinate, info.End);
                }
            }

            _snapGuideLayer.IsVisible = _activeSnapGuides.Count > 0;
        }

        private void HideSnapGuides()
        {
            _activeSnapGuides.Clear();
            if (_snapGuideLayer != null) _snapGuideLayer.IsVisible = false;
        }

        private static bool IsVectorTool(EditorTool tool)
        {
            return tool is EditorTool.Line or EditorTool.Arrow;
        }

        private static double GetVectorThickness(AvaloniaControl control)
        {
            if (control is Avalonia.Controls.Shapes.Line line) return Math.Max(1, line.StrokeThickness);
            if (control is Canvas group)
            {
                var arrowLine = group.Children.OfType<Avalonia.Controls.Shapes.Line>().FirstOrDefault();
                if (arrowLine != null) return Math.Max(1, arrowLine.StrokeThickness);
            }

            return 1;
        }

        private static double GetArrowPadding(double thickness)
        {
            return 18 + thickness * 2.4;
        }

        private static double HalfControlSize(AvaloniaControl control)
        {
            if (control == null || double.IsNaN(control.Width) || control.Width <= 0) return 7;
            return control.Width / 2;
        }

        private static bool TryGetVectorAbsolutePoints(AvaloniaControl control, out AvaloniaPoint start, out AvaloniaPoint end)
        {
            start = default;
            end = default;
            double left = CanvasLeft(control);
            double top = CanvasTop(control);

            if (control is Avalonia.Controls.Shapes.Line line)
            {
                start = new AvaloniaPoint(left + line.StartPoint.X, top + line.StartPoint.Y);
                end = new AvaloniaPoint(left + line.EndPoint.X, top + line.EndPoint.Y);
                return true;
            }

            if (control is Canvas group && group.Tag is ArrowProperties props)
            {
                start = props.Start;
                end = props.End;
                return true;
            }

            return false;
        }

        private void SetVectorAbsolutePoints(AvaloniaControl control, AvaloniaPoint start, AvaloniaPoint end)
        {
            if (control is Avalonia.Controls.Shapes.Line line)
            {
                double minX = Math.Min(start.X, end.X);
                double minY = Math.Min(start.Y, end.Y);
                Canvas.SetLeft(line, minX);
                Canvas.SetTop(line, minY);
                line.StartPoint = new AvaloniaPoint(start.X - minX, start.Y - minY);
                line.EndPoint = new AvaloniaPoint(end.X - minX, end.Y - minY);
            }
            else if (control is Canvas group && group.Tag is ArrowProperties)
            {
                UpdateArrowVisuals(group, start, end);
            }
        }

        private AvaloniaPoint ApplyVectorConstraints(AvaloniaPoint proposed, AvaloniaPoint anchor, KeyModifiers modifiers)
        {
            var config = IniConfig.GetIniSection<CoreConfiguration>();
            if (!config.MagneticSnappingEnabled || _disableSnappingForCurrentDrag) return proposed;

            if (modifiers.HasFlag(KeyModifiers.Shift))
            {
                return SnapToEightDirectionsStrict(proposed, anchor);
            }

            // FEATURE (magnet escape): holding ALT is a live override - the endpoint follows the
            // pointer raw, with neither target magnets nor angle assistance.
            if (modifiers.HasFlag(KeyModifiers.Alt)) return proposed;

            AvaloniaPoint snapped = SnapToNearbyTarget(proposed, anchor);
            if (snapped != proposed) 
            {
                return PullBackFromTarget(anchor, snapped);
            }
            
            snapped = SoftSnapToEightDirections(proposed, anchor);
            return snapped;
        }

        private static AvaloniaPoint SnapToEightDirectionsStrict(AvaloniaPoint proposed, AvaloniaPoint anchor)
        {
            double dx = proposed.X - anchor.X;
            double dy = proposed.Y - anchor.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 1) return proposed;

            double angle = Math.Atan2(dy, dx);
            double snappedAngle = Math.Round(angle / (Math.PI / 4)) * (Math.PI / 4);
            return new AvaloniaPoint(anchor.X + Math.Cos(snappedAngle) * length, anchor.Y + Math.Sin(snappedAngle) * length);
        }

        private static AvaloniaPoint SoftSnapToEightDirections(AvaloniaPoint proposed, AvaloniaPoint anchor)
        {
            double dx = proposed.X - anchor.X;
            double dy = proposed.Y - anchor.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 10) return proposed;

            double angle = Math.Atan2(dy, dx);
            double snappedAngle = Math.Round(angle / (Math.PI / 4)) * (Math.PI / 4);
            
            if (Math.Abs(angle - snappedAngle) < 0.04)
            {
                return new AvaloniaPoint(anchor.X + Math.Cos(snappedAngle) * length, anchor.Y + Math.Sin(snappedAngle) * length);
            }
            return proposed;
        }

        private AvaloniaPoint SnapToNearbyTarget(AvaloniaPoint proposed, AvaloniaPoint anchor)
        {
            AvaloniaPoint? bestTarget = null;
            double bestDistance = 8.0;

            foreach (var target in GetVectorSnapTargets())
            {
                double distance = Distance(proposed, target);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestTarget = target;
                }
            }

            return bestTarget ?? proposed;
        }

        private AvaloniaPoint PullBackFromTarget(AvaloniaPoint anchor, AvaloniaPoint target)
        {
            double dx = target.X - anchor.X;
            double dy = target.Y - anchor.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length <= VectorSnapGap) return target;
            return new AvaloniaPoint(target.X - dx / length * VectorSnapGap, target.Y - dy / length * VectorSnapGap);
        }

        private IEnumerable<AvaloniaPoint> GetVectorSnapTargets()
        {
            if (_image != null)
            {
                double w = _image.Width;
                double h = _image.Height;
                yield return new AvaloniaPoint(0, 0);
                yield return new AvaloniaPoint(w / 2, 0);
                yield return new AvaloniaPoint(w, 0);
                yield return new AvaloniaPoint(w, h / 2);
                yield return new AvaloniaPoint(w, h);
                yield return new AvaloniaPoint(w / 2, h);
                yield return new AvaloniaPoint(0, h);
                yield return new AvaloniaPoint(0, h / 2);
            }

            foreach (var annotation in GetUserAnnotations())
            {
                if (ReferenceEquals(annotation, _selectedControl)) continue;
                if (!TryGetControlBounds(annotation, out var bounds)) continue;

                bool isRound = annotation is Avalonia.Controls.Shapes.Ellipse || GetToolFromControl(annotation) == EditorTool.Counter || GetToolFromControl(annotation) == EditorTool.Emoji;
                if (isRound)
                {
                    double cx = bounds.X + bounds.Width / 2;
                    double cy = bounds.Y + bounds.Height / 2;
                    double rx = bounds.Width / 2;
                    double ry = bounds.Height / 2;
                    double cos = 0.70710678;
                    double sin = 0.70710678;

                    yield return new AvaloniaPoint(cx, bounds.Y);
                    yield return new AvaloniaPoint(cx + rx * cos, cy - ry * sin);
                    yield return new AvaloniaPoint(bounds.Right, cy);
                    yield return new AvaloniaPoint(cx + rx * cos, cy + ry * sin);
                    yield return new AvaloniaPoint(cx, bounds.Bottom);
                    yield return new AvaloniaPoint(cx - rx * cos, cy + ry * sin);
                    yield return new AvaloniaPoint(bounds.X, cy);
                    yield return new AvaloniaPoint(cx - rx * cos, cy - ry * sin);
                }
                else
                {
                    yield return new AvaloniaPoint(bounds.X, bounds.Y);
                    yield return new AvaloniaPoint(bounds.X + bounds.Width / 2, bounds.Y);
                    yield return new AvaloniaPoint(bounds.Right, bounds.Y);
                    yield return new AvaloniaPoint(bounds.Right, bounds.Y + bounds.Height / 2);
                    yield return new AvaloniaPoint(bounds.Right, bounds.Bottom);
                    yield return new AvaloniaPoint(bounds.X + bounds.Width / 2, bounds.Bottom);
                    yield return new AvaloniaPoint(bounds.X, bounds.Bottom);
                    yield return new AvaloniaPoint(bounds.X, bounds.Y + bounds.Height / 2);
                }
            }
        }

        private void RefreshSnapTargetsList()
        {
            if (_snapDotsLayer == null)
            {
                _snapDotsLayer = new Canvas { IsHitTestVisible = false, ZIndex = 9998 };
                _canvas.Children.Add(_snapDotsLayer);
            }

            bool shouldShow = IsVectorTool(_currentTool) || IsVectorControl(_selectedControl);
            if (!shouldShow || _image == null)
            {
                _snapDotsLayer.IsVisible = false;
                return;
            }

            _snapDotsLayer.IsVisible = true;
            _snapDotTargets.Clear();
            _snapDotTargets.AddRange(GetVectorSnapTargets());

            while (_snapDotsLayer.Children.Count < _snapDotTargets.Count)
            {
                var dot = new Avalonia.Controls.Shapes.Ellipse { Width = 6, Height = 6, IsHitTestVisible = false };
                _snapDotsLayer.Children.Add(dot);
            }
            while (_snapDotsLayer.Children.Count > _snapDotTargets.Count)
            {
                _snapDotsLayer.Children.RemoveAt(_snapDotsLayer.Children.Count - 1);
            }

            for (int i = 0; i < _snapDotTargets.Count; i++)
            {
                var dot = (Avalonia.Controls.Shapes.Ellipse)_snapDotsLayer.Children[i];
                Canvas.SetLeft(dot, _snapDotTargets[i].X - 3);
                Canvas.SetTop(dot, _snapDotTargets[i].Y - 3);
                dot.Fill = SnapDotDimBrush;
                dot.Stroke = null;
                dot.StrokeThickness = 0;
                dot.Width = 6;
                dot.Height = 6;
                dot.ZIndex = 0;
            }
        }

        private void HighlightSnapDot(AvaloniaPoint? snappedPoint)
        {
            if (_snapDotsLayer == null || !_snapDotsLayer.IsVisible) return;
            
            for (int i = 0; i < _snapDotTargets.Count; i++)
            {
                var dot = (Avalonia.Controls.Shapes.Ellipse)_snapDotsLayer.Children[i];
                bool isSnapped = snappedPoint.HasValue && Distance(_snapDotTargets[i], snappedPoint.Value) < 1.0;
                
                if (isSnapped)
                {
                    dot.Fill = Brushes.DeepSkyBlue;
                    dot.Stroke = Brushes.White;
                    dot.StrokeThickness = 1;
                    dot.Width = 8;
                    dot.Height = 8;
                    Canvas.SetLeft(dot, _snapDotTargets[i].X - 4);
                    Canvas.SetTop(dot, _snapDotTargets[i].Y - 4);
                    dot.ZIndex = 1;
                }
                else
                {
                    dot.Fill = SnapDotDimBrush;
                    dot.Stroke = null;
                    dot.StrokeThickness = 0;
                    dot.Width = 6;
                    dot.Height = 6;
                    Canvas.SetLeft(dot, _snapDotTargets[i].X - 3);
                    Canvas.SetTop(dot, _snapDotTargets[i].Y - 3);
                    dot.ZIndex = 0;
                }
            }
        }

        private bool TryGetControlBounds(AvaloniaControl control, out Rect bounds)
        {
            bounds = default;
            if (control == null) return false;

            if (TryGetVectorAbsolutePoints(control, out var start, out var end))
            {
                double padding = Math.Max(4, GetVectorThickness(control) / 2);
                double minX = Math.Min(start.X, end.X) - padding;
                double minY = Math.Min(start.Y, end.Y) - padding;
                double maxX = Math.Max(start.X, end.X) + padding;
                double maxY = Math.Max(start.Y, end.Y) + padding;
                bounds = new Rect(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
                return true;
            }

            if (control is Avalonia.Controls.Shapes.Polyline pl && pl.Points.Count > 0)
            {
                double polyLeft = CanvasLeft(pl);
                double polyTop = CanvasTop(pl);
                double minX = pl.Points.Min(p => p.X);
                double minY = pl.Points.Min(p => p.Y);
                double maxX = pl.Points.Max(p => p.X);
                double maxY = pl.Points.Max(p => p.Y);
                bounds = new Rect(polyLeft + minX, polyTop + minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
                return true;
            }

            double left = CanvasLeft(control);
            double top = CanvasTop(control);
            double width = double.IsNaN(control.Width) || control.Width <= 0 ? control.Bounds.Width : control.Width;
            double height = double.IsNaN(control.Height) || control.Height <= 0 ? control.Bounds.Height : control.Height;
            if (width <= 0 || height <= 0)
            {
                control.Measure(new AvaloniaSize(double.PositiveInfinity, double.PositiveInfinity));
                if (width <= 0) width = control.DesiredSize.Width;
                if (height <= 0) height = control.DesiredSize.Height;
            }

            if (width <= 0 || height <= 0) return false;

            bounds = new Rect(left, top, width, height);
            return true;
        }

        private bool IsPointerNearControl(AvaloniaPoint pos, AvaloniaControl control)
        {
            if (TryGetVectorAbsolutePoints(control, out var start, out var end))
            {
                double tolerance = Math.Max(VectorHitTolerance, GetVectorThickness(control) + 6);
                return PointToLineDistance(pos, start, end) <= tolerance;
            }

            if (control is Avalonia.Controls.Shapes.Polyline pl)
            {
                double left = CanvasLeft(pl);
                double top = CanvasTop(pl);
                for (int i = 0; i < pl.Points.Count - 1; i++)
                {
                    var a = new AvaloniaPoint(pl.Points[i].X + left, pl.Points[i].Y + top);
                    var b = new AvaloniaPoint(pl.Points[i + 1].X + left, pl.Points[i + 1].Y + top);
                    if (PointToLineDistance(pos, a, b) <= VectorHitTolerance) return true;
                }

                return false;
            }

            return TryGetControlBounds(control, out var bounds) && bounds.Inflate(10).Contains(pos);
        }

        private AvaloniaControl FindSelectableControlAt(AvaloniaPoint pos)
        {
            return _canvas.Children
                .OfType<AvaloniaControl>()
                .LastOrDefault(c => !IsEditorChrome(c) && c.IsHitTestVisible && IsPointerNearControl(pos, c));
        }

        private void UpdateHoverIndicator(AvaloniaControl hovered)
        {
            _hoveredControl = hovered;
            if (_hoverIndicator == null) return;
            if (hovered == null || ReferenceEquals(hovered, _selectedControl) || !TryGetControlBounds(hovered, out var bounds))
            {
                _hoverIndicator.IsVisible = false;
                return;
            }

            _hoverIndicator.Width = bounds.Width + 8;
            _hoverIndicator.Height = bounds.Height + 8;
            Canvas.SetLeft(_hoverIndicator, bounds.X - 4);
            Canvas.SetTop(_hoverIndicator, bounds.Y - 4);
            _hoverIndicator.IsVisible = true;
        }

        private void HideVectorInfo()
        {
            if (_vectorInfoPopup != null) _vectorInfoPopup.IsVisible = false;
        }

        private void UpdateVectorInfo(AvaloniaPoint start, AvaloniaPoint end)
        {
            if (_vectorInfoPopup == null || _vectorInfoText == null) return;

            double length = Distance(start, end);
            double angle = Math.Atan2(end.Y - start.Y, end.X - start.X) * 180.0 / Math.PI;
            if (angle < 0) angle += 360;
            _vectorInfoText.Text = $"{length:0} px  {angle:0} deg";

            _vectorInfoPopup.Measure(new AvaloniaSize(double.PositiveInfinity, double.PositiveInfinity));
            double popupWidth = Math.Max(70, _vectorInfoPopup.DesiredSize.Width);
            double popupHeight = Math.Max(22, _vectorInfoPopup.DesiredSize.Height);
            double left = (start.X + end.X) / 2 + 12;
            double top = (start.Y + end.Y) / 2 - popupHeight - 8;

            if (_image != null)
            {
                left = Math.Clamp(left, 0, Math.Max(0, _image.Width - popupWidth));
                top = Math.Clamp(top, 0, Math.Max(0, _image.Height - popupHeight));
            }

            Canvas.SetLeft(_vectorInfoPopup, left);
            Canvas.SetTop(_vectorInfoPopup, top);
            _vectorInfoPopup.IsVisible = true;
        }

        private Avalonia.Controls.Shapes.Rectangle CreatePixelateAnnotation(AvaloniaPoint start, AvaloniaPoint end)
        {
            double left = Math.Min(start.X, end.X);
            double top = Math.Min(start.Y, end.Y);
            double width = Math.Abs(end.X - start.X);
            double height = Math.Abs(end.Y - start.Y);
            var rectangle = new Avalonia.Controls.Shapes.Rectangle
            {
                Width = width,
                Height = height,
                Tag = new PixelateProperties { Strength = NormalizePixelateStrength(_lastPixelateStrength) }
            };
            Canvas.SetLeft(rectangle, left);
            Canvas.SetTop(rectangle, top);
            RefreshPixelateAnnotation(rectangle);
            return rectangle;
        }

        private async void RefreshPixelateAnnotation(AvaloniaControl control)
        {
            if (_image == null || control is not Avalonia.Controls.Shapes.Rectangle rectangle || rectangle.Tag is not PixelateProperties props) return;
            double left = CanvasLeft(rectangle);
            double top = CanvasTop(rectangle);
            double width = double.IsNaN(rectangle.Width) || rectangle.Width <= 0 ? rectangle.Bounds.Width : rectangle.Width;
            double height = double.IsNaN(rectangle.Height) || rectangle.Height <= 0 ? rectangle.Bounds.Height : rectangle.Height;
            var area = ClampImageRectangle(new Rect(left, top, width, height));
            if (area.Width < 1 || area.Height < 1) { rectangle.Fill = new SolidColorBrush(AvaloniaColor.FromArgb(80, 100, 100, 100)); return; }
            int strength = NormalizePixelateStrength(props.Strength);
            props.Strength = strength;
            long requestId = System.Threading.Interlocked.Increment(ref _pixelateCounter);
            rectangle.Resources["PixelateRequest"] = requestId;
            var bitmap = await Task.Run(() => {
                using var patch = _image.Clone(ctx => ctx.Crop(area).BoxBlur((int)Math.Max(1, strength / 2.0f)).Pixelate(strength).BoxBlur((int)Math.Max(1, strength / 4.0f)));
                return patch.ToAvaloniaBitmap();
            });
            if (rectangle.Resources.TryGetValue("PixelateRequest", out var val) && val is long currentId && currentId != requestId) { bitmap.Dispose(); return; }
            if (rectangle.Fill is ImageBrush oldBrush && oldBrush.Source is IDisposable disp) disp.Dispose();
            rectangle.Fill = new ImageBrush { Source = bitmap, Stretch = Stretch.Fill };
        }

        private void RefreshAllPixelateAnnotations()
        {
            foreach (var annotation in GetUserAnnotations())
            {
                if (IsPixelateControl(annotation)) RefreshPixelateAnnotation(annotation);
            }
        }

        // ===== FEATURE (highlight filter) ==================================================
        // Ported from Greenshot's HighlightFilter (GPL): instead of painting a translucent
        // alpha overlay over the capture (which washes the text out into a milky haze),
        // every pixel inside the marker rectangle is re-coloured by taking the channel-
        // wise minimum of the marker colour and the pixel colour. Dark text stays fully
        // readable while the area still reads as "marked" - the classic marker-pen look.
        private Avalonia.Controls.Shapes.Rectangle CreateHighlightAnnotation(AvaloniaPoint start, AvaloniaPoint end)
        {
            double left = Math.Min(start.X, end.X);
            double top = Math.Min(start.Y, end.Y);
            double width = Math.Abs(end.X - start.X);
            double height = Math.Abs(end.Y - start.Y);
            var rectangle = new Avalonia.Controls.Shapes.Rectangle
            {
                Width = width,
                Height = height,
                Tag = new HighlightProperties { Color = HighlightMarkerColor() }
            };
            rectangle.Classes.Add("highlight");
            Canvas.SetLeft(rectangle, left);
            Canvas.SetTop(rectangle, top);
            RefreshHighlightAnnotation(rectangle);
            return rectangle;
        }

        private AvaloniaColor HighlightMarkerColor()
        {
            if (_toolBrushes.TryGetValue(EditorTool.Highlight, out var toolBrush) && toolBrush is SolidColorBrush solid)
            {
                return AvaloniaColor.FromArgb(255, solid.Color.R, solid.Color.G, solid.Color.B);
            }
            var fallback = ((SolidColorBrush)HighlightBrushBase).Color;
            return AvaloniaColor.FromArgb(255, fallback.R, fallback.G, fallback.B);
        }

        private async void RefreshHighlightAnnotation(AvaloniaControl control)
        {
            if (_image == null || control is not Avalonia.Controls.Shapes.Rectangle rectangle || rectangle.Tag is not HighlightProperties props) return;
            double left = CanvasLeft(rectangle);
            double top = CanvasTop(rectangle);
            double width = double.IsNaN(rectangle.Width) || rectangle.Width <= 0 ? rectangle.Bounds.Width : rectangle.Width;
            double height = double.IsNaN(rectangle.Height) || rectangle.Height <= 0 ? rectangle.Bounds.Height : rectangle.Height;
            var area = ClampImageRectangle(new Rect(left, top, width, height));
            if (area.Width < 1 || area.Height < 1) { rectangle.Fill = new SolidColorBrush(AvaloniaColor.FromArgb(80, 100, 100, 100)); return; }
            var marker = props.Color;
            float mr = marker.R / 255f, mg = marker.G / 255f, mb = marker.B / 255f;
            long requestId = System.Threading.Interlocked.Increment(ref _highlightCounter);
            rectangle.Resources["HighlightRequest"] = requestId;
            var bitmap = await Task.Run(() => {
                using var patch = _image.Clone(ctx => ctx.Crop(area)).CloneAs<Rgba32>();
                // Greenshot HighlightFilter math: Math.Min(marker, colour) per channel.
                patch.Mutate(x => x.ProcessPixelRowsAsVector4(row =>
                {
                    for (int x2 = 0; x2 < row.Length; x2++)
                    {
                        ref var pixel = ref row[x2];
                        pixel.X = Math.Min(pixel.X, mr);
                        pixel.Y = Math.Min(pixel.Y, mg);
                        pixel.Z = Math.Min(pixel.Z, mb);
                    }
                }));
                return patch.ToAvaloniaBitmap();
            });
            if (rectangle.Resources.TryGetValue("HighlightRequest", out var val) && val is long currentId && currentId != requestId) { bitmap.Dispose(); return; }
            if (rectangle.Fill is ImageBrush oldBrush && oldBrush.Source is IDisposable disp) disp.Dispose();
            rectangle.Fill = new ImageBrush { Source = bitmap, Stretch = Stretch.Fill };
        }

        private void RefreshAllHighlightAnnotations()
        {
            foreach (var annotation in GetUserAnnotations())
            {
                if (IsHighlightControl(annotation)) RefreshHighlightAnnotation(annotation);
            }
        }

        private void BeginPreviewShape(AvaloniaPoint start, IBrush brush)
        {
            RemovePreviewShape();
            _previewControl = _currentTool switch
            {
                EditorTool.Line => new Avalonia.Controls.Shapes.Line { Stroke = brush, StrokeThickness = _currentThickness, StrokeJoin = PenLineJoin.Round, StrokeLineCap = PenLineCap.Round, IsHitTestVisible = false, ZIndex = _currentZIndex++ },
                EditorTool.Arrow => CreateArrowPreview(brush),
                EditorTool.Highlight => new Avalonia.Controls.Shapes.Rectangle { Fill = brush, IsHitTestVisible = false, ZIndex = _currentZIndex++ },
                EditorTool.Rectangle => new Avalonia.Controls.Shapes.Rectangle { Stroke = brush, StrokeThickness = _currentThickness, Fill = _toolFillModes[EditorTool.Rectangle] ? brush : Brushes.Transparent, IsHitTestVisible = false, ZIndex = _currentZIndex++ },
                EditorTool.Ellipse => new Avalonia.Controls.Shapes.Ellipse { Stroke = brush, StrokeThickness = _currentThickness, Fill = _toolFillModes[EditorTool.Ellipse] ? brush : Brushes.Transparent, IsHitTestVisible = false, ZIndex = _currentZIndex++ },
                EditorTool.Blur => new Avalonia.Controls.Shapes.Rectangle { Fill = new SolidColorBrush(AvaloniaColor.FromArgb(120, 100, 100, 100)), Stroke = Brushes.DeepSkyBlue, StrokeThickness = Math.Max(1, _currentThickness), IsHitTestVisible = false, ZIndex = _currentZIndex++ },
                EditorTool.Crop => new Avalonia.Controls.Shapes.Rectangle { Stroke = brush, StrokeThickness = Math.Max(1, _currentThickness), Fill = Brushes.Transparent, IsHitTestVisible = false, ZIndex = _currentZIndex++ },
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
            var group = new Canvas { IsHitTestVisible = false, ZIndex = _currentZIndex++ };
            group.Children.Add(new Avalonia.Controls.Shapes.Line { Stroke = brush, StrokeThickness = _currentThickness, StrokeJoin = PenLineJoin.Round, StrokeLineCap = PenLineCap.Round });
            group.Children.Add(new Avalonia.Controls.Shapes.Polygon { Fill = brush });
            return group;
        }

        private void UpdatePreviewShape(AvaloniaPoint end)
        {
            if (_previewControl == null) return;
            if (_previewControl is Canvas arrowGroup)
            {
                UpdateArrowPreview(arrowGroup, end);
                return;
            }

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
        }

        private void UpdateArrowPreview(Canvas group, AvaloniaPoint end)
        {
            UpdateArrowVisuals(group, _startPoint, end);
        }

        private void UpdateArrowVisuals(Canvas group, AvaloniaPoint start, AvaloniaPoint end)
        {
            var line = group.Children.OfType<Avalonia.Controls.Shapes.Line>().FirstOrDefault();
            var head = group.Children.OfType<Avalonia.Controls.Shapes.Polygon>().FirstOrDefault();
            if (line == null || head == null) return;

            double thickness = Math.Max(1, line.StrokeThickness);
            double padding = GetArrowPadding(thickness);
            double minX = Math.Min(start.X, end.X) - padding;
            double minY = Math.Min(start.Y, end.Y) - padding;
            double maxX = Math.Max(start.X, end.X) + padding;
            double maxY = Math.Max(start.Y, end.Y) + padding;
            Canvas.SetLeft(group, minX);
            Canvas.SetTop(group, minY);
            group.Width = Math.Max(1, maxX - minX);
            group.Height = Math.Max(1, maxY - minY);

            if (group.Tag is ArrowProperties props)
            {
                props.Start = start;
                props.End = end;
            }

            double angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
            double vectorLength = Distance(start, end);
            double headLength = Math.Min(10 + thickness * 2.2, Math.Max(6, vectorLength * 0.45));
            double headWidth = Math.Min(7 + thickness * 1.35, Math.Max(4, headLength * 0.72));

            double cos = Math.Cos(angle);
            double sin = Math.Sin(angle);

            double pullBack = Math.Min(headLength * 0.4, vectorLength * 0.5);

            line.StartPoint = new AvaloniaPoint(start.X - minX, start.Y - minY);
            line.EndPoint = new AvaloniaPoint(end.X - pullBack * cos - minX, end.Y - pullBack * sin - minY);

            var tip = new AvaloniaPoint(end.X - minX, end.Y - minY);
            var p1 = new AvaloniaPoint(end.X - headLength * cos + headWidth * sin - minX, end.Y - headLength * sin - headWidth * cos - minY);
            var p2 = new AvaloniaPoint(end.X - (headLength * 0.7) * cos - minX, end.Y - (headLength * 0.7) * sin - minY);
            var p3 = new AvaloniaPoint(end.X - headLength * cos - headWidth * sin - minX, end.Y - headLength * sin + headWidth * cos - minY);

            head.Points = new List<AvaloniaPoint> { tip, p1, p2, p3 };
            head.Fill = line.Stroke;
        }

        private void RemovePreviewShape()
        {
            if (_previewControl != null)
            {
                DisposeAnnotationResources(_previewControl);
                _canvas.Children.Remove(_previewControl);
                _previewControl = null;
            }
        }

        private class ArrowProperties { public AvaloniaPoint Start; public AvaloniaPoint End; }
        private class PixelateProperties { public int Strength; }
        private class HighlightProperties { public AvaloniaColor Color; }
        private class TextAnnotationProperties { public double PreferredFontSize; }

        // ===== FEATURE (tool ghosts) =======================================================
        // A translucent preview of the active tool's item floats under the pointer instead of the
        // plain crosshair: the selected emoji travels with the mouse, a pencil is shown for the pen,
        // a mini arrow/rectangle/ellipse/etc. previews the shape about to be drawn, and stamps
        // (text/counter/emoji) render exactly where they will land. Esc or right-click leaves the
        // tool, which hides the ghost (see ResetToolsAndSelection).

        private void OnCanvasPointerExited(object sender, PointerEventArgs e)
        {
            HideToolGhost();
            HideSnapGuides();
            Cursor = _currentTool == EditorTool.None ? HandCursor : CrossCursor;
        }

        private void HideToolGhost()
        {
            if (_toolGhostLayer != null) _toolGhostLayer.IsVisible = false;
        }

        private void UpdateToolGhost(AvaloniaPoint pos)
        {
            if (_toolGhostLayer == null || _currentTool == EditorTool.None)
            {
                HideToolGhost();
                return;
            }

            if (_ghostSettingsDirty || _toolGhost == null) RebuildToolGhost();
            if (_toolGhost == null)
            {
                HideToolGhost();
                return;
            }

            // Stamps magnetically preview their snapped landing spot along with the guide lines.
            AvaloniaPoint effective = IsStampTool(_currentTool) ? ApplyStampSnap(_currentTool, pos) : pos;
            var config = IniConfig.GetIniSection<CoreConfiguration>();

            switch (_currentTool)
            {
                case EditorTool.Emoji: // same anchor as PlaceEmoji
                    Canvas.SetLeft(_toolGhost, effective.X - 16);
                    Canvas.SetTop(_toolGhost, effective.Y - 16);
                    break;
                case EditorTool.Counter: // same anchor as PlaceCounter (centered on the click)
                {
                    double size = config.LastCounterSize > 0 ? config.LastCounterSize : 42;
                    Canvas.SetLeft(_toolGhost, effective.X - size / 2);
                    Canvas.SetTop(_toolGhost, effective.Y - size / 2);
                    break;
                }
                default: // icon ghosts are drawn centered around (0,0) of their host
                    Canvas.SetLeft(_toolGhost, effective.X);
                    Canvas.SetTop(_toolGhost, effective.Y);
                    break;
            }

            _toolGhostLayer.IsVisible = true;
            Cursor = NoneCursor; // the ghost replaces the OS cursor while it is visible
        }

        private void RebuildToolGhost()
        {
            _ghostSettingsDirty = false;
            if (_toolGhostLayer == null) return;

            _toolGhostLayer.Children.Clear();
            _toolGhost = null;

            var config = IniConfig.GetIniSection<CoreConfiguration>();
            var brush = _toolBrushes.TryGetValue(_currentTool, out var toolBrush) ? toolBrush : Brushes.White;

            switch (_currentTool)
            {
                case EditorTool.Emoji:
                    _toolGhost = new TextBlock
                    {
                        Text = _pendingEmoji,
                        FontSize = config.LastEmojiSize,
                        FontFamily = new Avalonia.Media.FontFamily("avares://snapvox.editor/Drawing/Emoji/#Twemoji Mozilla"),
                        Opacity = 0.65
                    };
                    break;

                case EditorTool.Counter:
                {
                    double size = config.LastCounterSize > 0 ? config.LastCounterSize : 42;
                    _toolGhost = new Border
                    {
                        Width = size,
                        Height = size,
                        Background = brush,
                        CornerRadius = new CornerRadius(size / 2),
                        Opacity = 0.65,
                        Child = new TextBlock
                        {
                            Text = _counterValue.ToString(),
                            Foreground = GetContrastColor(brush),
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                            FontSize = size * 0.6,
                            FontWeight = FontWeight.Bold,
                            IsHitTestVisible = false
                        }
                    };
                    break;
                }

                case EditorTool.Text:
                    _toolGhost = new Border
                    {
                        Width = 140,
                        Height = 36,
                        BorderBrush = brush,
                        BorderThickness = new Thickness(_toolFillModes[EditorTool.Text] ? _currentThickness : 0),
                        Background = _toolFillModes[EditorTool.Text]
                            ? new SolidColorBrush(AvaloniaColor.FromArgb(100, 30, 30, 30))
                            : new SolidColorBrush(AvaloniaColor.FromArgb(30, 0, 0, 0)),
                        Opacity = 0.65,
                        Child = new TextBlock
                        {
                            Text = "T",
                            Foreground = brush,
                            FontSize = 16,
                            FontWeight = FontWeight.SemiBold,
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                            IsHitTestVisible = false
                        }
                    };
                    break;

                case EditorTool.FreeDraw:
                    _toolGhost = CreatePencilGhost(brush);
                    break;

                case EditorTool.Line:
                {
                    var host = CreateGhostIconHost();
                    host.Children.Add(new Avalonia.Controls.Shapes.Line
                    {
                        StartPoint = new AvaloniaPoint(-16, 16),
                        EndPoint = new AvaloniaPoint(12, -12),
                        Stroke = brush,
                        StrokeThickness = Math.Max(2, _currentThickness),
                        StrokeLineCap = PenLineCap.Round
                    });
                    _toolGhost = host;
                    break;
                }

                case EditorTool.Arrow:
                    _toolGhost = CreateArrowGhostIcon(brush);
                    break;

                case EditorTool.Rectangle:
                {
                    var host = CreateGhostIconHost();
                    host.Children.Add(CreateCenteredIconShape(new Avalonia.Controls.Shapes.Rectangle
                    {
                        Width = 44,
                        Height = 30,
                        Stroke = brush,
                        StrokeThickness = Math.Max(2, _currentThickness),
                        Fill = _toolFillModes[EditorTool.Rectangle] ? brush : null
                    }, 44, 30));
                    _toolGhost = host;
                    break;
                }

                case EditorTool.Ellipse:
                {
                    var host = CreateGhostIconHost();
                    host.Children.Add(CreateCenteredIconShape(new Avalonia.Controls.Shapes.Ellipse
                    {
                        Width = 44,
                        Height = 30,
                        Stroke = brush,
                        StrokeThickness = Math.Max(2, _currentThickness),
                        Fill = _toolFillModes[EditorTool.Ellipse] ? brush : null
                    }, 44, 30));
                    _toolGhost = host;
                    break;
                }

                case EditorTool.Highlight:
                {
                    var host = CreateGhostIconHost();
                    host.Children.Add(CreateCenteredIconShape(new Border
                    {
                        Width = 50,
                        Height = 24,
                        CornerRadius = new CornerRadius(6),
                        Background = brush,
                        BorderThickness = new Thickness(1),
                        BorderBrush = new SolidColorBrush(AvaloniaColor.FromArgb(140, 0, 0, 0))
                    }, 50, 24));
                    _toolGhost = host;
                    break;
                }

                case EditorTool.Blur:
                {
                    var host = CreateGhostIconHost();
                    host.Children.Add(CreateCenteredIconShape(new Avalonia.Controls.Shapes.Rectangle
                    {
                        Width = 44,
                        Height = 44,
                        Fill = new SolidColorBrush(AvaloniaColor.FromArgb(120, 100, 100, 100)),
                        Stroke = Brushes.DeepSkyBlue,
                        StrokeThickness = 1.5,
                        StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 4, 3 }
                    }, 44, 44));
                    _toolGhost = host;
                    break;
                }

                case EditorTool.Crop:
                {
                    var host = CreateGhostIconHost();
                    host.Children.Add(CreateCenteredIconShape(new Avalonia.Controls.Shapes.Rectangle
                    {
                        Width = 44,
                        Height = 44,
                        Stroke = Brushes.Yellow,
                        StrokeThickness = 1.5,
                        StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 4, 3 },
                        Fill = new SolidColorBrush(AvaloniaColor.FromArgb(40, 0, 0, 0))
                    }, 44, 44));
                    _toolGhost = host;
                    break;
                }

                default:
                    _toolGhost = null;
                    break;
            }

            if (_toolGhost != null) _toolGhostLayer.Children.Add(_toolGhost);
        }

        private Canvas CreateGhostIconHost()
        {
            // Icons counter-scale against the zoom container so they keep a constant on-screen
            // size at any zoom level; the transform origin (0,0) keeps them pinned to the pointer.
            return new Canvas
            {
                RenderTransform = _ghostIconScale,
                Opacity = 0.8,
                IsHitTestVisible = false
            };
        }

        private static AvaloniaControl CreateCenteredIconShape(AvaloniaControl shape, double width, double height)
        {
            Canvas.SetLeft(shape, -width / 2);
            Canvas.SetTop(shape, -height / 2);
            return shape;
        }

        private Canvas CreatePencilGhost(IBrush brush)
        {
            // Classic pencil angled at 45°, graphite tip exactly at the pointer (0,0), body in the
            // current pen color so the ghost also previews which color will be drawn.
            var host = CreateGhostIconHost();
            host.Opacity = 0.9;
            host.Children.Add(new Avalonia.Controls.Shapes.Polygon
            {
                Points = new List<AvaloniaPoint> { new(0, 0), new(5, -2), new(2, -5) },
                Fill = new SolidColorBrush(AvaloniaColor.FromRgb(70, 70, 70))
            });
            host.Children.Add(new Avalonia.Controls.Shapes.Polygon
            {
                Points = new List<AvaloniaPoint> { new(5, -2), new(2, -5), new(18, -21), new(21, -18) },
                Fill = brush,
                Stroke = new SolidColorBrush(AvaloniaColor.FromRgb(50, 50, 50)),
                StrokeThickness = 0.8
            });
            host.Children.Add(new Avalonia.Controls.Shapes.Polygon
            {
                Points = new List<AvaloniaPoint> { new(18, -21), new(21, -18), new(24, -21), new(21, -24) },
                Fill = new SolidColorBrush(AvaloniaColor.FromRgb(232, 147, 140))
            });
            return host;
        }

        private Canvas CreateArrowGhostIcon(IBrush brush)
        {
            // Mini 45° arrow whose tip sits at the pointer, mirroring the arrow tool visuals.
            var host = CreateGhostIconHost();
            host.Children.Add(new Avalonia.Controls.Shapes.Line
            {
                StartPoint = new AvaloniaPoint(-16, 16),
                EndPoint = new AvaloniaPoint(6, -6),
                Stroke = brush,
                StrokeThickness = Math.Max(2, _currentThickness),
                StrokeLineCap = PenLineCap.Round
            });
            host.Children.Add(new Avalonia.Controls.Shapes.Polygon
            {
                Points = new List<AvaloniaPoint> { new(14, -14), new(9, -2), new(10.5, -10.5), new(2, -9) },
                Fill = brush
            });
            return host;
        }

        private void OnCanvasPointerMoved(object sender, PointerEventArgs e)
        {
            if (_isBusy) return;

            if (_isPanning)
            {
                var sv = this.FindControl<ScrollViewer>("EditorScrollViewer");
                if (sv != null)
                {
                    var current = e.GetPosition(this);
                    double dx = current.X - _panStartPoint.X;
                    double dy = current.Y - _panStartPoint.Y;
                    sv.Offset = new Avalonia.Vector(
                        Math.Max(0, _panStartOffset.X - dx),
                        Math.Max(0, _panStartOffset.Y - dy));
                }
                return;
            }

            var pos = e.GetPosition(_canvas);

            if (_isOcrInteractiveMode)
            {
                HideToolGhost();
                if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && _ocrSelectionStartIndex != -1)
                {
                    int currentIndex = FindClosestOcrWordIndex(pos);
                    if (currentIndex != -1)
                    {
                        _ocrSelectionEndIndex = currentIndex;
                        UpdateOcrSelectionVisuals();
                    }
                    e.Handled = true;
                }
                else
                {
                    Cursor = IbeamCursor;
                }
                return;
            }

            if (_contextToolbar != null && (_isDraggingSelected || _isResizing || _isDraggingStartPoint || _isDraggingEndPoint))
            {
                _contextToolbar.IsVisible = false;
            }

            if (!_isDraggingSelected && !_isResizing && !_isDraggingStartPoint && !_isDraggingEndPoint && !_isDrawing)
            {
                if (e.Source is AvaloniaControl src && (_resizeHandles.Contains(src) || src == _startHandle || src == _endHandle)) return;
                var hovered = FindSelectableControlAt(pos);
                
                if (hovered != null)
                {
                    HideToolGhost();
                    HideSnapGuides();
                    Cursor = HandCursor;
                    UpdateHoverIndicator(hovered);
                }
                else
                {
                    Cursor = _currentTool == EditorTool.None ? HandCursor : CrossCursor;
                    UpdateHoverIndicator(null);
                    // FEATURE (tool ghosts): float the active tool's item under the pointer so it
                    // is obvious what will land where once clicked. Stamps also magnetically
                    // preview their snapped landing spot together with the guide lines.
                    if (_currentTool != EditorTool.None) UpdateToolGhost(pos);
                    else { HideToolGhost(); HideSnapGuides(); }
                }

                if (hovered != null)
                {
                    IBrush b = null;
                    if (hovered is Avalonia.Controls.Shapes.Shape s) b = s.Stroke ?? s.Fill;
                    else if (hovered is Canvas group && group.Tag is ArrowProperties)
                    {
                        var arrowLine = group.Children.OfType<Avalonia.Controls.Shapes.Line>().FirstOrDefault();
                        var arrowHead = group.Children.OfType<Avalonia.Controls.Shapes.Polygon>().FirstOrDefault();
                        b = arrowLine?.Stroke ?? arrowHead?.Fill;
                    }
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
                if ((Math.Abs(dx) > 0.01 || Math.Abs(dy) > 0.01) && !_dragUndoCaptured) { SaveUndoState(true); _dragUndoCaptured = true; }

                if (TryGetVectorAbsolutePoints(_selectedControl, out var start, out var end))
                {
                    if (_isDraggingStartPoint)
                    {
                        start = ApplyVectorConstraints(pos, end, e.KeyModifiers);
                    }
                    else
                    {
                        end = ApplyVectorConstraints(pos, start, e.KeyModifiers);
                    }

                    SetVectorAbsolutePoints(_selectedControl, start, end);
                    UpdateVectorInfo(start, end);
                }

                _dragLastPoint = pos;
                UpdateSelectionIndicator();
                
                RefreshSnapTargetsList();
                AvaloniaPoint? snappedPoint = null;
                if (!e.KeyModifiers.HasFlag(KeyModifiers.Alt))
                {
                    var t = SnapToNearbyTarget(pos, pos);
                    if (t != pos) snappedPoint = t;
                }
                HighlightSnapDot(snappedPoint);
                return;
            }
            if (_isResizing && _selectedControl != null) { double dx = pos.X - _dragLastPoint.X; double dy = pos.Y - _dragLastPoint.Y; if ((Math.Abs(dx) > 0.01 || Math.Abs(dy) > 0.01) && !_resizeUndoCaptured) { SaveUndoState(true); _resizeUndoCaptured = true; } ResizeSelectedControl(dx, dy, e.KeyModifiers.HasFlag(KeyModifiers.Alt)); if (IsPixelateControl(_selectedControl)) RefreshPixelateAnnotation(_selectedControl); if (IsHighlightControl(_selectedControl)) RefreshHighlightAnnotation(_selectedControl); _dragLastPoint = pos; UpdateSelectionIndicator(); return; }
            if (_isDraggingSelected && _selectedControl != null)
            {
                double dx = pos.X - _dragLastPoint.X;
                double dy = pos.Y - _dragLastPoint.Y;
                if ((Math.Abs(dx) > 0.01 || Math.Abs(dy) > 0.01) && !_dragUndoCaptured)
                {
                    SaveUndoState(true);
                    _dragUndoCaptured = true;
                }

                double curL = Canvas.GetLeft(_selectedControl);
                if (double.IsNaN(curL)) curL = _selectedControl.Bounds.X;
                double curT = Canvas.GetTop(_selectedControl);
                if (double.IsNaN(curT)) curT = _selectedControl.Bounds.Y;

                _dragUnsnappedLeft += dx;
                _dragUnsnappedTop += dy;

                var snapped = ApplyMagneticSnap(_selectedControl, _dragUnsnappedLeft, _dragUnsnappedTop, e.KeyModifiers.HasFlag(KeyModifiers.Alt));
                double appliedDx = snapped.X - curL;
                double appliedDy = snapped.Y - curT;
                Canvas.SetLeft(_selectedControl, snapped.X);
                Canvas.SetTop(_selectedControl, snapped.Y);

                if (_selectedControl is Canvas group && group.Tag is ArrowProperties props)
                {
                    props.Start = new AvaloniaPoint(props.Start.X + appliedDx, props.Start.Y + appliedDy);
                    props.End = new AvaloniaPoint(props.End.X + appliedDx, props.End.Y + appliedDy);
                }

                if (IsPixelateControl(_selectedControl)) RefreshPixelateAnnotation(_selectedControl);
                if (IsHighlightControl(_selectedControl)) RefreshHighlightAnnotation(_selectedControl);
                _dragLastPoint = pos;
                UpdateSelectionIndicator();
                return;
            }
            if (!_isDrawing) 
            {
                RefreshSnapTargetsList();
                HighlightSnapDot(null);
                return;
            }
            if (_currentTool == EditorTool.FreeDraw && _activePolyline != null) _activePolyline.Points.Add(new AvaloniaPoint(pos.X - _startPoint.X, pos.Y - _startPoint.Y));
            else
            {
                var previewEnd = IsVectorTool(_currentTool) ? ApplyVectorConstraints(pos, _startPoint, e.KeyModifiers) : pos;
                UpdatePreviewShape(previewEnd);
                // FEATURE (shape snap guides): while stretching a new shape, magnetically align its
                // edges/center with the other shapes and stretch gentle guide lines between them.
                if (IsRectSnappableTool(_currentTool)) ApplyPreviewShapeSnap(previewEnd, e.KeyModifiers.HasFlag(KeyModifiers.Alt));
                else { _previewSnapDelta = default; HideSnapGuides(); }
                if (IsVectorTool(_currentTool)) UpdateVectorInfo(_startPoint, previewEnd);
                
                RefreshSnapTargetsList();
                AvaloniaPoint? snappedPoint = null;
                if (IsVectorTool(_currentTool) && !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
                {
                    var t = SnapToNearbyTarget(pos, pos);
                    if (t != pos) snappedPoint = t;
                }
                HighlightSnapDot(snappedPoint);
            }
        }

        private void NudgeSelectedControl(double dx, double dy)
        {
            if (_selectedControl == null) return;
            SaveUndoState(true);
            double curL = Canvas.GetLeft(_selectedControl);
            if (double.IsNaN(curL)) curL = _selectedControl.Bounds.X;
            double curT = Canvas.GetTop(_selectedControl);
            if (double.IsNaN(curT)) curT = _selectedControl.Bounds.Y;
            Canvas.SetLeft(_selectedControl, curL + dx);
            Canvas.SetTop(_selectedControl, curT + dy);
            if (_selectedControl is Canvas group && group.Tag is ArrowProperties props)
            {
                props.Start = new AvaloniaPoint(props.Start.X + dx, props.Start.Y + dy);
                props.End = new AvaloniaPoint(props.End.X + dx, props.End.Y + dy);
            }
            if (IsPixelateControl(_selectedControl)) RefreshPixelateAnnotation(_selectedControl);
            if (IsHighlightControl(_selectedControl)) RefreshHighlightAnnotation(_selectedControl);
            UpdateSelectionIndicator();
        }

        private void ResizeSelectedControl(double dx, double dy, bool altSnapBypass = false)
        {
            double oldW = double.IsNaN(_selectedControl.Width) ? _selectedControl.Bounds.Width : _selectedControl.Width;
            double oldH = double.IsNaN(_selectedControl.Height) ? _selectedControl.Bounds.Height : _selectedControl.Height;
            double left = Canvas.GetLeft(_selectedControl); if (double.IsNaN(left)) left = _selectedControl.Bounds.X;
            double top = Canvas.GetTop(_selectedControl); if (double.IsNaN(top)) top = _selectedControl.Bounds.Y;
            bool keepRatio = (_selectedControl is TextBlock) || (_selectedControl is Border b && b.Child is TextBlock);
            double newW = oldW; double newH = oldH;

            double wDelta = 0, hDelta = 0;
            switch (_resizeHandleIndex)
            {
                case 0: wDelta = -dx; hDelta = -dy; break;
                case 1: wDelta = dx; hDelta = -dy; break;
                case 2: wDelta = dx; hDelta = dy; break;
                case 3: wDelta = -dx; hDelta = dy; break;
            }

            // FEATURE (magnet escape): holding ALT while dragging a handle is a live magnetic-snap
            // override - the edge follows the pointer raw. Resizing also uses a tighter capture
            // zone (ResizeSnapThreshold) than shape moves: while enlarging, the dragged edge
            // sweeps across many other shapes' edge targets, and a wide zone chained from one
            // capture straight into the next, which felt unreleasable.
            var config = IniConfig.GetIniSection<CoreConfiguration>();
            if (!altSnapBypass && config.MagneticSnappingEnabled && !_disableSnappingForCurrentDrag && !keepRatio)
            {
                if (_resizeHandleIndex == 0 || _resizeHandleIndex == 3)
                {
                    double snappedLeft = SnapAxisEdge(left - wDelta, GetMagneticSnapTargets(_selectedControl, true), ResizeSnapThreshold);
                    wDelta = left - snappedLeft;
                }
                else
                {
                    double snappedRight = SnapAxisEdge(left + oldW + wDelta, GetMagneticSnapTargets(_selectedControl, true), ResizeSnapThreshold);
                    wDelta = snappedRight - (left + oldW);
                }

                if (_resizeHandleIndex == 0 || _resizeHandleIndex == 1)
                {
                    double snappedTop = SnapAxisEdge(top - hDelta, GetMagneticSnapTargets(_selectedControl, false), ResizeSnapThreshold);
                    hDelta = top - snappedTop;
                }
                else
                {
                    double snappedBottom = SnapAxisEdge(top + oldH + hDelta, GetMagneticSnapTargets(_selectedControl, false), ResizeSnapThreshold);
                    hDelta = snappedBottom - (top + oldH);
                }
            }

            if (keepRatio)
            {
                double scale = ((oldW + wDelta) * oldW + (oldH + hDelta) * oldH) / (oldW * oldW + oldH * oldH);
                scale = Math.Max(10 / oldW, Math.Max(10 / oldH, scale));
                newW = oldW * scale; newH = oldH * scale;
                switch (_resizeHandleIndex) { case 0: Canvas.SetLeft(_selectedControl, left + (oldW - newW)); Canvas.SetTop(_selectedControl, top + (oldH - newH)); break; case 1: Canvas.SetTop(_selectedControl, top + (oldH - newH)); break; case 3: Canvas.SetLeft(_selectedControl, left + (oldW - newW)); break; }
            }
            else
            {
                newW = Math.Max(10, oldW + wDelta); newH = Math.Max(10, oldH + hDelta);
                switch (_resizeHandleIndex) { case 0: Canvas.SetLeft(_selectedControl, left + (oldW - newW)); Canvas.SetTop(_selectedControl, top + (oldH - newH)); break; case 1: Canvas.SetTop(_selectedControl, top + (oldH - newH)); break; case 3: Canvas.SetLeft(_selectedControl, left + (oldW - newW)); break; }
                if (_selectedControl is Border borderCtrl && borderCtrl.Child is TextBlock txt && GetToolFromControl(_selectedControl) == EditorTool.Counter)
                {
                    double avgSize = (newW + newH) / 2.0;
                    borderCtrl.CornerRadius = new CornerRadius(avgSize / 2);
                    txt.FontSize = avgSize * 0.6;
                }
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
            else if (_selectedControl is Border borderCtrl) { if (borderCtrl.Child is TextBox tb) { double preferredSize = Math.Max(8, tb.FontSize * scaleX); tb.FontSize = preferredSize; tb.Tag = new TextAnnotationProperties { PreferredFontSize = preferredSize }; SetTextBoxBoundsToBorder(borderCtrl, tb); FitTextBoxToBorder(borderCtrl, tb); UpdatePersistedSize(EditorTool.Text, tb.FontSize); } else if (borderCtrl.Child is TextBlock txt) { txt.FontSize *= scaleX; borderCtrl.CornerRadius = new CornerRadius(newW / 2); UpdatePersistedSize(EditorTool.Counter, newW); } }
        }

        private void UpdatePersistedSize(EditorTool tool, double size) { var config = IniConfig.GetIniSection<CoreConfiguration>(); if (tool == EditorTool.Text) config.LastTextSize = size; else if (tool == EditorTool.Emoji) config.LastEmojiSize = size; else if (tool == EditorTool.Counter) config.LastCounterSize = size; _toolFontSizes[tool] = size; _ghostSettingsDirty = true; }

        private void OnCanvasPointerReleased(object sender, PointerReleasedEventArgs e)
        {
            if (_isBusy) return;

            if (_isPanning)
            {
                _isPanning = false;
                e.Pointer.Capture(null);
                // Back to the regular hand in move mode, or to the active tool's crosshair
                // otherwise (middle-button panning works while any tool is active too).
                Cursor = _currentTool == EditorTool.None ? HandCursor : CrossCursor;
                return;
            }

            var pos = e.GetPosition(_canvas);
            
            if (_isOcrInteractiveMode)
            {
                if (_ocrSelectionStartIndex != -1)
                {
                    _selectedOcrWords.Clear();
                    int minIdx = Math.Min(_ocrSelectionStartIndex, _ocrSelectionEndIndex);
                    int maxIdx = Math.Max(_ocrSelectionStartIndex, _ocrSelectionEndIndex);
                    
                    if (minIdx != -1 && maxIdx != -1 && _interactiveOcrInfo?.Words != null)
                    {
                        for (int i = minIdx; i <= maxIdx; i++)
                        {
                            if (i >= 0 && i < _interactiveOcrInfo.Words.Count)
                                _selectedOcrWords.Add(_interactiveOcrInfo.Words[i]);
                        }
                    }
                    
                    UpdateOcrSelectionVisuals(true);
                    
                    var ocrToolbar = this.FindControl<Border>("OcrContextToolbar");
                    if (_selectedOcrWords.Count > 0 && ocrToolbar != null)
                    {
                        var lastWord = _selectedOcrWords.OrderByDescending(w => w.Bounds.X + w.Bounds.Width).ThenBy(w => w.Bounds.Y).Last();
                        Canvas.SetLeft(ocrToolbar, lastWord.Bounds.X + lastWord.Bounds.Width + 5);
                        Canvas.SetTop(ocrToolbar, lastWord.Bounds.Y);
                        ocrToolbar.IsVisible = true;
                    }
                    else if (_selectedOcrWords.Count == 0 && ocrToolbar != null)
                    {
                        ocrToolbar.IsVisible = false;
                    }
                    
                    _ocrSelectionStartIndex = -1;
                    _ocrSelectionEndIndex = -1;
                }
                e.Handled = true;
                return;
            }

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
                HideVectorInfo();
                HideSnapGuides();
            }

            if (_isDraggingSelected)
            {
                _isDraggingSelected = false;
                _dragUndoCaptured = false;
                
                UpdateSelectionIndicator();
                HideVectorInfo();
                return;
            }

            if (!_isDrawing) return;
            _isDrawing = false;
            var endPoint = e.GetPosition(_canvas);
            if (IsVectorTool(_currentTool)) endPoint = ApplyVectorConstraints(endPoint, _startPoint, e.KeyModifiers);
            RemovePreviewShape();
            HideVectorInfo();
            if (IsRectSnappableTool(_currentTool) && (_previewSnapDelta.X != 0 || _previewSnapDelta.Y != 0))
            {
                // FEATURE (shape snap guides): commit the shape exactly where the snapped preview was shown.
                _startPoint = new AvaloniaPoint(_startPoint.X + _previewSnapDelta.X, _startPoint.Y + _previewSnapDelta.Y);
                endPoint = new AvaloniaPoint(endPoint.X + _previewSnapDelta.X, endPoint.Y + _previewSnapDelta.Y);
            }
            _previewSnapDelta = default;
            HideSnapGuides();
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
                if (_hoverIndicator != null) _hoverIndicator.IsVisible = false;
                HideVectorInfo();
                if (_pixelateStrengthHandle != null) _pixelateStrengthHandle.IsVisible = false;
                UpdateModeStatus();
                return; 
            }
            
            _selectionIndicator.IsVisible = true; 
            if (_deleteBtn != null) _deleteBtn.IsEnabled = true;

            bool isCropOrPaste = _selectedControl?.Tag?.ToString() == "CropObject" || _selectedControl?.Tag?.ToString() == "PasteObject";
            var applyBtn = this.FindControl<Avalonia.Controls.Button>("ContextApplyButton");
            var dupBtn = this.FindControl<Avalonia.Controls.Button>("ContextDuplicateButton");
            var sep = this.FindControl<Avalonia.Controls.Shapes.Rectangle>("ContextApplySeparator");
            if (applyBtn != null) applyBtn.IsVisible = isCropOrPaste;
            if (dupBtn != null) dupBtn.IsVisible = !isCropOrPaste;
            if (sep != null) sep.IsVisible = isCropOrPaste;

            if (!TryGetControlBounds(_selectedControl, out var selectedBounds))
            {
                _selectionIndicator.IsVisible = false;
                return;
            }

            bool isVector = IsVectorControl(_selectedControl);
            double absX = selectedBounds.X;
            double absY = selectedBounds.Y;
            double w = selectedBounds.Width;
            double h = selectedBounds.Height;

            _selectionIndicator.Width = w + 4; _selectionIndicator.Height = h + 4;
            Canvas.SetLeft(_selectionIndicator, absX - 2); Canvas.SetTop(_selectionIndicator, absY - 2);
            
            if (isVector)
            {
                foreach (var handle in _resizeHandles) handle.IsVisible = false;
            }
            else
            {
                _resizeHandles[0].IsVisible = true; Canvas.SetLeft(_resizeHandles[0], absX - 7); Canvas.SetTop(_resizeHandles[0], absY - 7);
                _resizeHandles[1].IsVisible = true; Canvas.SetLeft(_resizeHandles[1], absX + w - 7); Canvas.SetTop(_resizeHandles[1], absY - 7);
                _resizeHandles[2].IsVisible = true; Canvas.SetLeft(_resizeHandles[2], absX + w - 7); Canvas.SetTop(_resizeHandles[2], absY + h - 7);
                _resizeHandles[3].IsVisible = true; Canvas.SetLeft(_resizeHandles[3], absX - 7); Canvas.SetTop(_resizeHandles[3], absY + h - 7);
            }

            if (isVector)
            {
                _startHandle.IsVisible = true;
                _endHandle.IsVisible = true;
                
                if (!TryGetVectorAbsolutePoints(_selectedControl, out var startP, out var endP))
                {
                    _startHandle.IsVisible = false;
                    _endHandle.IsVisible = false;
                    goto PostHandles;
                }
                
                double startOffset = HalfControlSize(_startHandle);
                double endOffset = HalfControlSize(_endHandle);
                Canvas.SetLeft(_startHandle, startP.X - startOffset); Canvas.SetTop(_startHandle, startP.Y - startOffset);
                Canvas.SetLeft(_endHandle, endP.X - endOffset); Canvas.SetTop(_endHandle, endP.Y - endOffset);
            }
            else
            {
                _startHandle.IsVisible = false; 
                _endHandle.IsVisible = false;
            }

        PostHandles:
            if (IsPixelateControl(_selectedControl))
            {
                if (_selectedControl.Tag is PixelateProperties props)
                {
                    SyncPixelateSlider(props.Strength);
                }
            }
            else
            {
                if (_pixelateStrengthHandle != null) _pixelateStrengthHandle.IsVisible = false;
            }

            if (_contextToolbar != null)
            {
                bool transformingSelection = _isDraggingSelected || _isResizing || _isDraggingStartPoint || _isDraggingEndPoint || _isDraggingStrengthHandle;
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
            SaveUndoState(true);
            
            if (!TryGetControlBounds(_selectedControl, out var origBounds)) return;

            var clone = CloneAnnotationControl(_selectedControl);
            if (clone != null)
            {
                clone.Width = origBounds.Width;
                clone.Height = origBounds.Height;

                double canvasW = _canvas.Bounds.Width;
                double canvasH = _canvas.Bounds.Height;
                
                bool isLeft = (origBounds.Center.X < canvasW / 2);
                bool isTop = (origBounds.Center.Y < canvasH / 2);
                
                double dirX = isLeft ? 1 : -1;
                double dirY = isTop ? 1 : -1;
                double offset = 85;
                double targetL = Canvas.GetLeft(_selectedControl);
                double targetT = Canvas.GetTop(_selectedControl);

                if (IsVectorControl(_selectedControl) && TryGetVectorAbsolutePoints(_selectedControl, out var start, out var end))
                {
                    var offsetVec = new AvaloniaPoint(dirX * offset, dirY * offset);
                    SetVectorAbsolutePoints(clone, start + offsetVec, end + offsetVec);
                }
                else
                {
                    targetL += dirX * offset;
                    targetT += dirY * offset;
                    Canvas.SetLeft(clone, targetL);
                    Canvas.SetTop(clone, targetT);
                }

                if (clone is Border border && border.Child is TextBlock textBlock && GetToolFromControl(clone) == EditorTool.Counter)
                {
                    textBlock.Text = (_counterValue++).ToString();
                }

                if (IsPixelateControl(clone)) RefreshPixelateAnnotation(clone);
                if (IsHighlightControl(clone)) RefreshHighlightAnnotation(clone);
                _canvas.Children.Add(clone);
                _selectedControl = clone;
                
                _currentTool = EditorTool.None;
                SyncToolButtonSelection();
                clone.Cursor = HandCursor;
                
                UpdateSelectionIndicator();
                Cursor = HandCursor;
                UpdateHoverIndicator(clone);
                
                OverlayHelper.ShowLightToast("Duplicated", this);
            }
        }

        private void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            if (_selectedControl == null) return;

            SaveUndoState(true);
            DisposeAnnotationResources(_selectedControl);
            _canvas.Children.Remove(_selectedControl);
            _selectedControl = null;
            UpdateSelectionIndicator();
            UpdateThicknessPanelVisibility();
            OverlayHelper.ShowLightToast("Deleted", this);
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

        // ISSUE (sluggish final action): Download/Copy/OCR waited a HARDCODED 1000ms
        // before closing the editor while the blink overlay honoured the user's
        // NotificationOverlayDurationMs setting. With a 500ms blink the editor idled
        // another 500ms with no overlay on screen; with a long blink the overlay
        // outlived the editor by seconds. The close wait now derives from the SAME
        // GetOverlayDurationMs() value the overlay uses, clamped to [250, 1000]:
        // a 250ms floor keeps the "blink first, then close" sequential finality
        // visible, and the 1000ms ceiling means the editor never closes slower
        // than the old fixed wait. The overlay finishes its full configured cycle
        // on its own (it owns its lifetime), so longer blink settings still
        // confirm the action after the editor is gone. Everything stays on the
        // async/await pipeline - no blocking waits, no fire-and-forget closes.
        private static int GetFinalActionCloseDelayMs()
        {
            return Math.Clamp(NotificationOverlayWindow.GetOverlayDurationMs(), 250, 1000);
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
                    SaveUndoState(true);
                    await FlattenAnnotationsAsync().ConfigureAwait(true);
                    await Task.Run(() => _image.Mutate(x => x.Resize(resizeWin.ResultWidth, resizeWin.ResultHeight))).ConfigureAwait(true);
                    Dispatcher.UIThread.Post(async () => {
                        await UpdateDisplayAsync().ConfigureAwait(true);
                        OverlayHelper.ShowLightToast("IMAGE RESIZED", this);
                        ShowUndoAvailableHint();
                    });
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
                using var tempImage = await GetFlattenedImageForOcrAsync().ConfigureAwait(true);
                if (tempImage == null) return;

                string fileName = $"Capture_{DateTime.Now:yyyy-MM-dd_HH-mm-ss_fff}.jpg";
                
                string downloadsPath = await GetEffectiveDownloadPathAsync().ConfigureAwait(true);
                Directory.CreateDirectory(downloadsPath);
                string downloadedFilePath = Path.Combine(downloadsPath, fileName);
                await SaveJpegAsync(tempImage, downloadedFilePath).ConfigureAwait(true);

                await SaveToHistoryBackupAsync(fileName, tempImage).ConfigureAwait(true);

                await UiClipboard.SetFilePathThenImageAsync(downloadedFilePath, tempImage, true).ConfigureAwait(true);
                
                Dispatcher.UIThread.Post(() => {
                    OverlayHelper.ShowNotification("IMAGE SAVED TO DOWNLOADS", this);
                });
                
                await Task.Delay(GetFinalActionCloseDelayMs()).ConfigureAwait(true);

                Dispatcher.UIThread.Post(() => {
                    _forceClose = true;
                    Close();
                });
            }
            catch (Exception ex)
            {
                Log.Fatal("[DOWNLOAD_CRITICAL_ERROR]", ex);
                Dispatcher.UIThread.Post(() => {
                    OverlayHelper.ShowNotification("Save Failed", this);
                });
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
                using var tempImage = await GetFlattenedImageAsync().ConfigureAwait(true);
                if (tempImage == null) return;

                await SaveToHistoryBackupAsync($"Capture_{DateTime.Now:yyyy-MM-dd HH_mm_ss_fff}.jpg", tempImage).ConfigureAwait(true);

                await UiClipboard.SetImageAsync(tempImage, true).ConfigureAwait(true);
                
                Dispatcher.UIThread.Post(() => {
                    OverlayHelper.ShowNotification("IMAGE SAVED TO CLIPBOARD", this);
                });
                
                await Task.Delay(GetFinalActionCloseDelayMs()).ConfigureAwait(true);

                Dispatcher.UIThread.Post(() => {
                    _forceClose = true;
                    Close();
                });
            }
            catch (Exception ex)
            {
                Log.Fatal("[COPY_CRITICAL_ERROR]", ex);
                Dispatcher.UIThread.Post(() => {
                    OverlayHelper.ShowNotification("Copy Failed", this);
                });
            }
            finally
            {
                EndEditorOperation();
            }
        }

        private static Task SaveJpegAsync(ImageSharpImage img, string path) => Task.Run(() => img.Save(path, new JpegEncoder { Quality = IniConfig.GetIniSection<CoreConfiguration>().OutputFileJpegQuality }));

        private static async Task SaveToHistoryBackupAsync(string fileName, ImageSharpImage img)
        {
            if (!IniConfig.GetIniSection<CoreConfiguration>().KeepBackup) return;

            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "SnapVox");
                Directory.CreateDirectory(tempDir);
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
            var config = IniConfig.GetIniSection<CoreConfiguration>();

            if (_isOcrInteractiveMode)
            {
                _isOcrInteractiveMode = false;
                if (ocrButtonText != null) ocrButtonText.Text = "OCR Text Extraction";
                if (ocrButton != null) ocrButton.Background = Brushes.Transparent;
                Cursor = Avalonia.Input.Cursor.Default;
                ToggleToolbarControls(false);
                ClearOcrVisuals();
                _interactiveOcrInfo = null;
                EndEditorOperation();
                return;
            }

            try
            {
                if (ocrButton != null) ocrButton.IsEnabled = false;
                if (ocrButtonText != null) ocrButtonText.Text = "OCR Working...";

                Log.Info("STAGED OCR INITIATED");
                using var tempImage = await GetFlattenedImageForOcrAsync().ConfigureAwait(true);
                if (tempImage == null) return;
                
                var providers = SimpleServiceProvider.Current.GetAllInstances<IOcrProvider>();
                var ocrProvider = OcrProviderSelector.Select(providers, config.OcrEngine);
                if (ocrProvider == null)
                {
                    Dispatcher.UIThread.Post(() => OverlayHelper.ShowNotification("OCR UNAVAILABLE", this));
                    return;
                }

                var ocrInfo = await ocrProvider.DoOcrAsync(tempImage).ConfigureAwait(true);
                if (ocrInfo == null)
                {
                    Dispatcher.UIThread.Post(() => OverlayHelper.ShowNotification("OCR UNAVAILABLE", this));
                    return;
                }

                if (config.LeavePictureAsIsDuringOcr)
                {
                    Dispatcher.UIThread.Post(() => {
                        _interactiveOcrInfo = ocrInfo;
                        _interactiveOcrIndex = OcrWordSpatialIndex.Create(_interactiveOcrInfo.Words);
                        if (_interactiveOcrInfo.Words == null || _interactiveOcrInfo.Words.Count == 0)
                        {
                            OverlayHelper.ShowNotification("No Selectable Text Found", this);
                            _isOcrInteractiveMode = false;
                        }
                        else
                        {
                            _isOcrInteractiveMode = true;
                            if (ocrButtonText != null) ocrButtonText.Text = "Exit OCR Mode";
                            if (ocrButton != null) ocrButton.Background = new SolidColorBrush(AvaloniaColor.Parse("#B23A3A"));
                            Cursor = new Avalonia.Input.Cursor(StandardCursorType.Ibeam);
                            ToggleToolbarControls(true);
                            PaintOcrWords();
                        }
                    });
                }
                else
                {
                    string text = ocrInfo?.Text ?? "";
                    
                    Dispatcher.UIThread.Post(async () => {
                        var topLevel = TopLevel.GetTopLevel(this);
                        if (topLevel?.Clipboard != null) await topLevel.Clipboard.SetTextAsync(text);
                    });

                    if (config.KeepBackup)
                    {
                        string tempDir = Path.Combine(Path.GetTempPath(), "SnapVox");
                        Directory.CreateDirectory(tempDir);
                        string fileName = $"OCR_{DateTime.Now:yyyy-MM-dd_HH-mm-ss_fff}.txt";
                        string fullPath = Path.Combine(tempDir, fileName);
                        
                        await File.WriteAllTextAsync(fullPath, text).ConfigureAwait(true);
                        _ = Task.Run(() => Process.Start(new ProcessStartInfo("notepad.exe", fullPath) { UseShellExecute = true }));
                        
                        Dispatcher.UIThread.Post(() => OverlayHelper.ShowNotification("TEXT COPIED & SAVED", this));
                    }
                    else
                    {
                        Dispatcher.UIThread.Post(() => OverlayHelper.ShowNotification("TEXT COPIED", this));
                    }
                    
                    if (config.CloseEditorOnAction)
                    {
                        await Task.Delay(GetFinalActionCloseDelayMs()).ConfigureAwait(true);
                        Dispatcher.UIThread.Post(() => {
                            _forceClose = true;
                            Close();
                        });
                    }
                }
            }
            catch (Exception ex) 
            { 
                Log.Error("OCR failed", ex); 
                Dispatcher.UIThread.Post(() => OverlayHelper.ShowNotification("OCR FAILED", this)); 
            }
            finally
            {
                Dispatcher.UIThread.Post(() => {
                    if (!_isOcrInteractiveMode)
                    {
                        if (ocrButtonText != null) ocrButtonText.Text = "OCR Text Extraction";
                        if (ocrButton != null) ocrButton.Background = Brushes.Transparent;
                        ToggleToolbarControls(false);
                    }
                    if (ocrButton != null) ocrButton.IsEnabled = true;
                    EndEditorOperation();
                });
            }
        }
        
        private void ToggleToolbarControls(bool isOcrMode)
        {
            var bottomToolbar = this.FindControl<Border>("BottomToolbarBorder");
            var leftToolbar = this.FindControl<StackPanel>("LeftToolBar");
            var topToolbarStackPanel = this.FindControl<StackPanel>("TopToolbarStackPanel");

            if (bottomToolbar != null) 
            {
                bottomToolbar.IsEnabled = !isOcrMode;
                bottomToolbar.Opacity = isOcrMode ? 0.0 : 1.0;
            }
            
            if (leftToolbar != null) 
            {
                leftToolbar.IsEnabled = !isOcrMode;
                leftToolbar.Opacity = isOcrMode ? 0.0 : 1.0;
            }

            if (topToolbarStackPanel != null)
            {
                foreach (var child in topToolbarStackPanel.Children)
                {
                    if (child is Avalonia.Controls.Control ctrl && ctrl.Name != "OcrButton")
                    {
                        ctrl.IsEnabled = !isOcrMode;
                        ctrl.Opacity = isOcrMode ? 0.0 : 1.0;
                    }
                }
            }
            
            if (_contextToolbar != null && isOcrMode)
            {
                _contextToolbar.IsVisible = false;
            }
        }
        
        private int FindClosestOcrWordIndex(AvaloniaPoint pos)
        {
            if (_interactiveOcrInfo?.Words == null || _interactiveOcrInfo.Words.Count == 0) return -1;
            _interactiveOcrIndex ??= OcrWordSpatialIndex.Create(_interactiveOcrInfo.Words);
            return _interactiveOcrIndex.FindClosestIndex((int)Math.Round(pos.X), (int)Math.Round(pos.Y), 10000);
        }

        private void UpdateOcrSelectionVisuals(bool refreshAll = false)
        {
            if (_interactiveOcrInfo?.Words == null) return;
            
            int minIdx = Math.Min(_ocrSelectionStartIndex, _ocrSelectionEndIndex);
            int maxIdx = Math.Max(_ocrSelectionStartIndex, _ocrSelectionEndIndex);

            if (refreshAll || _lastOcrSelectionMin == -2 || _lastOcrSelectionMax == -2)
            {
                UpdateOcrVisualRange(0, _ocrVisuals.Count - 1, minIdx, maxIdx);
            }
            else
            {
                UpdateOcrVisualRange(_lastOcrSelectionMin, _lastOcrSelectionMax, minIdx, maxIdx);
                UpdateOcrVisualRange(minIdx, maxIdx, minIdx, maxIdx);
            }

            _lastOcrSelectionMin = minIdx;
            _lastOcrSelectionMax = maxIdx;
        }

        private void UpdateOcrVisualRange(int start, int end, int minIdx, int maxIdx)
        {
            if (start < 0 || end < 0) return;
            int min = Math.Max(0, Math.Min(start, end));
            int max = Math.Min(_ocrVisuals.Count - 1, Math.Max(start, end));
            for (int i = min; i <= max; i++)
            {
                UpdateOcrVisual(i, minIdx, maxIdx);
            }
        }

        private void UpdateOcrVisual(int index, int minIdx, int maxIdx)
        {
            if (index < 0 || index >= _ocrVisuals.Count || index >= _interactiveOcrInfo.Words.Count) return;
            if (_ocrVisuals[index] is not Avalonia.Controls.Shapes.Rectangle r) return;
            bool isSelected = minIdx != -1 && maxIdx != -1 && index >= minIdx && index <= maxIdx;
            bool isPersisted = _selectedOcrWords.Contains(_interactiveOcrInfo.Words[index]);
            if (isSelected || isPersisted)
            {
                r.Fill = OcrSelectedFillBrush;
                r.Stroke = OcrSelectedStrokeBrush;
                r.StrokeThickness = 1.5;
            }
            else
            {
                r.Fill = OcrUnselectedFillBrush;
                r.Stroke = OcrUnselectedStrokeBrush;
                r.StrokeThickness = 1;
            }
        }

        private void PaintOcrWords()
        {
            ClearOcrVisuals();
            if (_interactiveOcrInfo?.Words == null || _canvas == null) return;
            if (_ocrVisuals == null) _ocrVisuals = new List<Avalonia.Controls.Control>();

            foreach (var word in _interactiveOcrInfo.Words)
            {
                if (string.IsNullOrWhiteSpace(word.Text)) continue;
                
                var rect = new Avalonia.Controls.Shapes.Rectangle
                {
                    Width = word.Bounds.Width + 2,
                    Height = word.Bounds.Height + 2,
                    Fill = OcrUnselectedFillBrush,
                    Stroke = OcrUnselectedStrokeBrush,
                    StrokeThickness = 1,
                    Tag = word,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(rect, word.Bounds.X);
                Canvas.SetTop(rect, word.Bounds.Y);
                _canvas.Children.Add(rect);
                _ocrVisuals.Add(rect);
            }
            _lastOcrSelectionMin = -2;
            _lastOcrSelectionMax = -2;
            UpdateOcrSelectionVisuals(true);
        }

        private void ClearOcrVisuals()
        {
            if (_canvas != null)
            {
                foreach (var visual in _ocrVisuals)
                {
                    _canvas.Children.Remove(visual);
                }
            }
            _ocrVisuals.Clear();
            _selectedOcrWords.Clear();
            _interactiveOcrIndex = null;
            _lastOcrSelectionMin = -1;
            _lastOcrSelectionMax = -1;
            var ocrToolbar = this.FindControl<Border>("OcrContextToolbar");
            if (ocrToolbar != null) ocrToolbar.IsVisible = false;
        }

        private async void OnOcrContextCopyClick(object sender, RoutedEventArgs e)
        {
            if (_selectedOcrWords.Count == 0) return;
            
            string textToCopy = OcrTextLayout.BuildVisualSelectionText(_selectedOcrWords);
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard != null) await topLevel.Clipboard.SetTextAsync(textToCopy);
            
            OverlayHelper.ShowNotification("TEXT COPIED", this);
            
            _selectedOcrWords.Clear();
            _ocrSelectionStartIndex = -1;
            _ocrSelectionEndIndex = -1;
            UpdateOcrSelectionVisuals(true);
            var ocrToolbar = this.FindControl<Border>("OcrContextToolbar");
            if (ocrToolbar != null) ocrToolbar.IsVisible = false;
        }

        private void OnCropApplyClick(object sender, RoutedEventArgs e)
        {
            OnWindowKeyDown(this, new KeyEventArgs { Key = Key.Enter });
        }

        private void OnUndoClick(object sender, RoutedEventArgs e)
        {
            if (_undoStack.Count > 0)
            {
                var current = CreateSnapshot(true);
                var previous = _undoStack.Last.Value;
                _undoStack.RemoveLast();
                _redoStack.AddLast(current);
                if (_redoStack.Count > MaxStackSize)
                {
                    var oldest = _redoStack.First.Value;
                    _redoStack.RemoveFirst();
                    DisposeSnapshot(oldest);
                }
                EnforceUndoMemoryBudget();
                RestoreSnapshot(previous);
                UpdateUndoRedoState();
            }
        }

        private void OnRedoClick(object sender, RoutedEventArgs e)
        {
            if (_redoStack.Count > 0)
            {
                var current = CreateSnapshot(true);
                var next = _redoStack.Last.Value;
                _redoStack.RemoveLast();
                _undoStack.AddLast(current);
                if (_undoStack.Count > MaxStackSize)
                {
                    var oldest = _undoStack.First.Value;
                    _undoStack.RemoveFirst();
                    DisposeSnapshot(oldest);
                }
                EnforceUndoMemoryBudget();
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
                Cursor = ArrowCursor;
                HideToolGhost();
                HideSnapGuides();
                btn.Classes.Remove("selected");
            }
            else
            {
                _currentTool = tool;
                Cursor = CrossCursor;

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
            // BUGFIX (stale marching ants): activating any tool moves the editing focus away
            // from the currently selected object, so the selection - and its marching-ants
            // frame, handles and context toolbar - must be released here. Previously the ants
            // stayed glued to the old object while the new tool was already in use.
            if (_currentTool != EditorTool.None && _selectedControl != null)
            {
                if (IsPastedImageControl(_selectedControl)) FinalizeSelectedPasteObject();
                _selectedControl = null;
                UpdateSelectionIndicator();
                UpdateHoverIndicator(null);
            }

            UpdateHoverIndicator(null);
            HideVectorInfo();
            UpdateFillToggleVisual();
            UpdateThicknessPanelVisibility();
            UpdateModeStatus();

            if (_toolBrushes.TryGetValue(_currentTool, out var brush) && brush is SolidColorBrush scb)
            {
                UpdateCurrentColorDisplay(scb.Color);
                UpdatePresetSelectionVisuals(scb.Color);
            }

            _ghostSettingsDirty = true; // tool ghost must reflect the newly selected tool
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

        private bool _syncingHexInput;

        // FEATURE (pro color picker): two-way hex entry - typing #RRGGBB (or RRGGBB / #AARRGGBB)
        // moves the spectrum view to that color, and every picker change writes the hex back.
        // A copy button exports the current hex to the clipboard as plain text.
        private void InitializeCustomColorFlyout()
        {
            var picker = this.FindControl<ColorView>("ColorPickerView");
            var hexInput = this.FindControl<TextBox>("HexInput");
            if (picker == null || hexInput == null) return;

            hexInput.Text = ToRgbHex(picker.Color);
            SyncHexPreview(picker.Color);

            picker.ColorChanged += (s, e) =>
            {
                _syncingHexInput = true;
                try
                {
                    hexInput.Text = ToRgbHex(e.NewColor);
                    SyncHexPreview(e.NewColor);
                }
                finally { _syncingHexInput = false; }
            };

            hexInput.TextChanged += (s, e) =>
            {
                if (_syncingHexInput) return;
                if (TryParseHexColor(hexInput.Text, out var parsed))
                {
                    picker.Color = parsed; // raises ColorChanged, which refreshes the preview chip
                }
            };
        }

        private void SyncHexPreview(AvaloniaColor color)
        {
            var chip = this.FindControl<Border>("HexPreviewChip");
            if (chip != null) chip.Background = new SolidColorBrush(color);
        }

        private static bool TryParseHexColor(string text, out AvaloniaColor color)
        {
            color = default;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string trimmed = text.Trim().TrimStart('#');
            if (trimmed.Length != 6 && trimmed.Length != 8) return false;
            foreach (char c in trimmed)
            {
                if (!Uri.IsHexDigit(c)) return false;
            }
            try { color = AvaloniaColor.Parse("#" + trimmed); return true; }
            catch { return false; }
        }

        private async void OnCopyHexClick(object sender, RoutedEventArgs e)
        {
            var picker = this.FindControl<ColorView>("ColorPickerView");
            string hex = picker != null ? ToRgbHex(picker.Color) : "#000000";
            try
            {
                if (Clipboard != null) await Clipboard.SetTextAsync(hex).ConfigureAwait(true);
                OverlayHelper.ShowLightToast($"COPIED {hex}", this);
            }
            catch (Exception ex)
            {
                OverlayHelper.ShowLightToast($"COPY FAILED: {ex.Message}", this);
            }
        }

        private void OnHexInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            var picker = this.FindControl<ColorView>("ColorPickerView");
            if (picker != null)
            {
                UpdateCurrentColorDisplay(picker.Color);
                ApplyColor(picker.Color);
                PushColorToHistory(picker.Color);
            }
            this.FindControl<Button>("CustomColorBtn")?.Flyout?.Hide();
            e.Handled = true;
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
            _ghostSettingsDirty = true; // tool ghost should preview the new color
            var brush = new SolidColorBrush(color);
            if (_selectedControl != null)
            {
                SaveUndoState(true);
                UpdateControlColor(_selectedControl, color);

                var inferredTool = GetToolFromControl(_selectedControl);
                if (inferredTool != EditorTool.None)
                {
                    if (inferredTool == EditorTool.Highlight) {
                        _toolBrushes[EditorTool.Highlight] = new SolidColorBrush(AvaloniaColor.FromArgb(80, color.R, color.G, color.B));
                    } else {
                        _toolBrushes[inferredTool] = brush;
                    }
                    if (inferredTool == EditorTool.Counter) { var config = IniConfig.GetIniSection<CoreConfiguration>(); config.LastCounterColor = ToRgbHex(color); IniConfig.Save(); }
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
                if (_currentTool == EditorTool.Counter) { var config = IniConfig.GetIniSection<CoreConfiguration>(); config.LastCounterColor = ToRgbHex(color); IniConfig.Save(); }
            }
            else if (_currentTool == EditorTool.None)
            {
                var config = IniConfig.GetIniSection<CoreConfiguration>(); 
                config.LastCounterColor = ToRgbHex(color); 
                IniConfig.Save();

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
            SaveToolPreferences();
        }

        private EditorTool GetToolFromControl(AvaloniaControl control)
        {
            if (control is Avalonia.Controls.Shapes.Line) return EditorTool.Line;
            if (control is Avalonia.Controls.Shapes.Rectangle r) {
                if (IsHighlightControl(r)) return EditorTool.Highlight;
                if (r.Classes.Contains("highlight")) return EditorTool.Highlight;
                if (IsPixelateControl(r)) return EditorTool.Blur;
                return EditorTool.Rectangle;
            }
            if (control is Avalonia.Controls.Shapes.Ellipse) return EditorTool.Ellipse;
            if (control is Avalonia.Controls.Shapes.Polyline) return EditorTool.FreeDraw;
            if (control is Border b) {
                if (b.Child is TextBox) return EditorTool.Text;
                if (b.Child is TextBlock) return EditorTool.Counter;
            }
            if (control is TextBlock) return EditorTool.Emoji;
            if (IsArrowControl(control)) return EditorTool.Arrow;
            return EditorTool.None;
        }

        private void UpdateControlColor(AvaloniaControl control, AvaloniaColor color)
        {
            var brush = new SolidColorBrush(color); 
            var isHighlight = control.Classes.Contains("highlight"); 
            var targetBrush = isHighlight ? new SolidColorBrush(AvaloniaColor.FromArgb(80, color.R, color.G, color.B)) : brush;
            if (IsPixelateControl(control)) return;
            if (control is Avalonia.Controls.Shapes.Shape s) { 
                if (s is Avalonia.Controls.Shapes.Rectangle r && r.Tag is HighlightProperties highlightProps)
                {
                    // Greenshot-style highlight recolors the actual pixels, so re-render the patch.
                    highlightProps.Color = AvaloniaColor.FromArgb(255, color.R, color.G, color.B);
                    RefreshHighlightAnnotation(r);
                }
                else if (s is Avalonia.Controls.Shapes.Rectangle r2 && r2.Fill != null) r2.Fill = targetBrush; 
                else s.Stroke = brush; 
            }
            else if (control is Border b) { 
                if (b.Child is TextBlock) b.Background = brush; 
                else b.BorderBrush = brush; 
                if (b.Child is TextBox tb) { tb.Foreground = brush; tb.CaretBrush = brush; }
            }
            else if (control is TextBlock t) t.Foreground = brush;
            else if (control is Canvas c && c.Tag is ArrowProperties)
            {
                foreach (var child in c.Children)
                {
                    if (child is Avalonia.Controls.Shapes.Line line) line.Stroke = brush;
                    else if (child is Avalonia.Controls.Shapes.Polygon polygon) polygon.Fill = brush;
                }

                if (TryGetVectorAbsolutePoints(c, out var start, out var end)) UpdateArrowVisuals(c, start, end);
            }
        }

        private void UpdatePresetSelectionVisuals(AvaloniaColor color) { var panel = this.FindControl<StackPanel>("ColorPresetsPanel"); if (panel != null) { string hex = color.ToString().ToUpperInvariant(); if (hex.Length == 9 && hex.StartsWith("#FF")) hex = "#" + hex.Substring(3); foreach (var child in panel.Children.OfType<Button>().Where(b => b.Classes.Contains("color-preset"))) { if (child.Tag?.ToString() == hex) child.Classes.Add("selected"); else child.Classes.Remove("selected"); } } }

        private void OnEmojiSelectClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string emoji) return;

            _currentTool = EditorTool.Emoji;
            _pendingEmoji = emoji;
            _ghostSettingsDirty = true; // ghost must float the newly picked emoji
            Cursor = HandCursor;

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

            // BUGFIX (emoji popup): picking an emoji commits the choice - fold the flyout
            // immediately instead of leaving it dangling over the canvas while the ghost
            // floats the selection.
            emojiBtn?.Flyout?.Hide();
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
            if (IsPixelateControl(control)) return false;
            if (IsHighlightControl(control)) return false;
            if (control is Avalonia.Controls.Shapes.Shape) return true;
            if (IsArrowControl(control)) return true;
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
            var choicesPanel = this.FindControl<StackPanel>("ThicknessChoicesPanel");
            var fillBtn = this.FindControl<Button>("FillToggleBtn");
            var resetCounterBtn = this.FindControl<Button>("ResetCounterBtn");
            var counterAdjustPanel = this.FindControl<StackPanel>("CounterAdjustPanel");
            var tool = _selectedControl != null ? GetToolFromControl(_selectedControl) : _currentTool;

            bool thicknessRelevant = _selectedControl != null
                ? IsThicknessRelevantForControl(_selectedControl)
                : IsThicknessRelevantForTool(_currentTool);
            bool pixelateRelevant = tool == EditorTool.Blur || IsPixelateControl(_selectedControl);
            bool relevant = thicknessRelevant || pixelateRelevant;

            if (panel != null) panel.IsVisible = relevant;
            if (choicesPanel != null) choicesPanel.IsVisible = thicknessRelevant && !pixelateRelevant;
            if (_pixelateToolbarSection != null) _pixelateToolbarSection.IsVisible = pixelateRelevant;

            if (pixelateRelevant)
            {
                int strength = _selectedControl?.Tag is PixelateProperties props ? props.Strength : _lastPixelateStrength;
                SyncPixelateSlider(strength);
            }

            if (fillBtn != null)
            {
                bool fillRelevant = tool == EditorTool.Rectangle || tool == EditorTool.Ellipse || tool == EditorTool.Text;
                fillBtn.IsVisible = fillRelevant;
            }

            if (resetCounterBtn != null)
            {
                resetCounterBtn.IsVisible = tool == EditorTool.Counter;
            }
            if (counterAdjustPanel != null)
            {
                counterAdjustPanel.IsVisible = tool == EditorTool.Counter;
            }

            if (!relevant) return;
            if (!thicknessRelevant || pixelateRelevant) return;

            var label = this.FindControl<TextBlock>("ThicknessLabel");
            if (label != null) label.Text = GetThicknessLabelText();
            SyncThicknessUI(_currentThickness);
        }
        private void OnThicknessClick(object sender, RoutedEventArgs e) { 
            if (sender is Button btn && btn.Tag is string tStr && double.TryParse(tStr, out var val)) { 
                _currentThickness = val; 
                SyncThicknessUI(val); 
                
                var config = IniConfig.GetIniSection<CoreConfiguration>();
                bool saveConfig = false;

                if (_selectedControl != null) { 
                    SaveUndoState(true); 
                    UpdateControlThickness(_selectedControl, val); 
                    var inferred = GetToolFromControl(_selectedControl); 
                    if (inferred != EditorTool.None) {
                        _toolThicknesses[inferred] = val; 
                        if (inferred == EditorTool.Text) { config.TextToolThickness = val; saveConfig = true; }
                    }
                    OverlayHelper.ShowLightToast("THICKNESS UPDATED", this); 
                    ShowUndoAvailableHint(); 
                } else if (_currentTool != EditorTool.None) { 
                    _toolThicknesses[_currentTool] = val; 
                    if (_currentTool == EditorTool.Text) { config.TextToolThickness = val; saveConfig = true; }
                } else if (_currentTool == EditorTool.None) { 
                    foreach (EditorTool tool in Enum.GetValues<EditorTool>()) { 
                        if (tool != EditorTool.None) _toolThicknesses[tool] = val; 
                    } 
                    config.TextToolThickness = val;
                    saveConfig = true;
                } 
                
                if (saveConfig) IniConfig.Save();
                SaveToolPreferences();
                _ghostSettingsDirty = true; // tool ghost must preview the new thickness
                Log.Info($"Thickness set to: {val}");
            } 
        }

        private void OnFillToggleClick(object sender, RoutedEventArgs e)
        {
            _isFillMode = !_isFillMode;
            var config = IniConfig.GetIniSection<CoreConfiguration>();
            if (_selectedControl != null) SaveUndoState(true);

            if (_currentTool == EditorTool.Text || (_selectedControl is Border b && b.Child is TextBox)) {
                config.UseTextFrame = _isFillMode;
                if (_selectedControl is Border textBorder) {
                    textBorder.BorderThickness = new Thickness(config.UseTextFrame ? config.TextToolThickness : 0);
                    textBorder.Background = _isFillMode ? new SolidColorBrush(AvaloniaColor.FromArgb(100, 30, 30, 30)) : new SolidColorBrush(AvaloniaColor.FromArgb(30, 0, 0, 0));
                }
            }

            UpdateFillToggleVisual();
            _ghostSettingsDirty = true; // ghost must preview the new fill mode
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
                    if (child is Avalonia.Controls.Shapes.Line || child.Stroke != null) child.StrokeThickness = val;
                }

                if (IsArrowControl(c) && TryGetVectorAbsolutePoints(c, out var start, out var end)) UpdateArrowVisuals(c, start, end);
            }
            else if (control is Border b && b.Child is TextBox)
            {
                var config = IniConfig.GetIniSection<CoreConfiguration>();
                b.BorderThickness = new Thickness(config.UseTextFrame ? val : 0);
            }
        }
        private void UpdateControlFill(AvaloniaControl control, bool fill) { 
            if (IsPixelateControl(control)) return;
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
            var border = new Border { Width = config.LastCounterSize, Height = config.LastCounterSize, Background = brush, CornerRadius = new CornerRadius(config.LastCounterSize/2), Child = new TextBlock { Text = val.ToString(), Foreground = textBrush, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, FontSize = config.LastCounterSize * 0.6, FontWeight = FontWeight.Bold, IsHitTestVisible = false } }; 
            Canvas.SetLeft(border, pos.X - (border.Width/2)); Canvas.SetTop(border, pos.Y - (border.Height/2)); AddAnnotation(border); PlayStampIn(border); OverlayHelper.ShowLightToast("COUNTER PLACED", this); 
        }
        private void PlaceEmoji(AvaloniaPoint pos, string emoji) { var config = IniConfig.GetIniSection<CoreConfiguration>(); var text = new TextBlock { Text = emoji, FontSize = config.LastEmojiSize, FontFamily = new Avalonia.Media.FontFamily("avares://snapvox.editor/Drawing/Emoji/#Twemoji Mozilla") }; Canvas.SetLeft(text, pos.X - 16); Canvas.SetTop(text, pos.Y - 16); AddAnnotation(text); PlayStampIn(text); OverlayHelper.ShowLightToast("EMOJI PLACED", this); }
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
                TextWrapping = TextWrapping.Wrap,
                Tag = new TextAnnotationProperties { PreferredFontSize = config.LastTextSize },
                TextAlignment = Avalonia.Media.TextAlignment.Center,
                VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center
            }; 
            var border = new Border { 
                BorderBrush = brush, 
                BorderThickness = new Thickness(fill ? _currentThickness : 0), 
                Background = fill ? new SolidColorBrush(AvaloniaColor.FromArgb(100, 30, 30, 30)) : new SolidColorBrush(AvaloniaColor.FromArgb(30, 0, 0, 0)), 
                Padding = new Thickness(2), 
                Child = textBox 
            }; 
            Canvas.SetLeft(border, pos.X); Canvas.SetTop(border, pos.Y); 
            AttachTextBoxBehavior(border, textBox, config);
            AddAnnotation(border); PlayStampIn(border); Dispatcher.UIThread.Post(() => textBox.Focus()); 
        }

        private void CommitShape(AvaloniaPoint start, AvaloniaPoint end)
        {
            AvaloniaControl shape = null; var brush = _toolBrushes[_currentTool]; double w = Math.Abs(end.X - start.X); double h = Math.Abs(end.Y - start.Y); if (w < 2 && h < 2) return;
            switch (_currentTool)
            {
                case EditorTool.Line: shape = new Avalonia.Controls.Shapes.Line { StartPoint = new AvaloniaPoint(start.X - Math.Min(start.X, end.X), start.Y - Math.Min(start.Y, end.Y)), EndPoint = new AvaloniaPoint(end.X - Math.Min(start.X, end.X), end.Y - Math.Min(start.Y, end.Y)), Stroke = brush, StrokeThickness = _currentThickness, StrokeJoin = PenLineJoin.Round, StrokeLineCap = PenLineCap.Round }; Canvas.SetLeft(shape, Math.Min(start.X, end.X)); Canvas.SetTop(shape, Math.Min(start.Y, end.Y)); break;
                case EditorTool.Arrow: DrawArrow(start, end, brush); return;
                case EditorTool.Highlight: shape = CreateHighlightAnnotation(start, end); break;
                case EditorTool.Rectangle: shape = new Avalonia.Controls.Shapes.Rectangle { Stroke = brush, StrokeThickness = _currentThickness, Width = w, Height = h, Fill = _toolFillModes[EditorTool.Rectangle] ? brush : null }; Canvas.SetLeft(shape, Math.Min(start.X, end.X)); Canvas.SetTop(shape, Math.Min(start.Y, end.Y)); break;
                case EditorTool.Ellipse: shape = new Avalonia.Controls.Shapes.Ellipse { Stroke = brush, StrokeThickness = _currentThickness, Width = w, Height = h, Fill = _toolFillModes[EditorTool.Ellipse] ? brush : null }; Canvas.SetLeft(shape, Math.Min(start.X, end.X)); Canvas.SetTop(shape, Math.Min(start.Y, end.Y)); break;
                case EditorTool.Blur: shape = CreatePixelateAnnotation(start, end); break;
                case EditorTool.Crop:
                    if (w < 10 || h < 10) return;
                    SaveUndoState(true);
                    var cropRect = new Avalonia.Controls.Shapes.Rectangle { Width = w, Height = h, Stroke = Brushes.Yellow, StrokeThickness = 1, StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 2, 2 }, Fill = new SolidColorBrush(AvaloniaColor.FromArgb(50, 0, 0, 0)), Tag = "CropObject", Cursor = SizeAllCursor };
                    Canvas.SetLeft(cropRect, Math.Min(start.X, end.X));
                    Canvas.SetTop(cropRect, Math.Min(start.Y, end.Y));
                    _canvas.Children.Add(cropRect);
                    _currentTool = EditorTool.None; Cursor = ArrowCursor; SyncToolButtonSelection();
                    _selectedControl = cropRect;
                    UpdateSelectionIndicator();
                    return;
            }
            if (shape != null) AddAnnotation(shape);
        }

        private void OnContextApplyClick(object sender, RoutedEventArgs e)
        {
            if (_selectedControl == null) return;
            var tag = _selectedControl.Tag?.ToString();
            
            if (tag == "CropObject")
            {
                double left = Canvas.GetLeft(_selectedControl);
                if (double.IsNaN(left)) left = _selectedControl.Bounds.X;
                double top = Canvas.GetTop(_selectedControl);
                if (double.IsNaN(top)) top = _selectedControl.Bounds.Y;
                double w = double.IsNaN(_selectedControl.Width) ? _selectedControl.Bounds.Width : _selectedControl.Width;
                double h = double.IsNaN(_selectedControl.Height) ? _selectedControl.Bounds.Height : _selectedControl.Height;

                DisposeAnnotationResources(_selectedControl);
                _canvas.Children.Remove(_selectedControl);
                _selectedControl = null;
                UpdateSelectionIndicator();

                ApplyCropRect(new Rect(left, top, w, h));
            }
            else if (tag == "PasteObject")
            {
                _selectedControl.Tag = "PastedImage";
                UpdateSelectionIndicator();
            }
        }

        private async void OnPasteClick(object sender, RoutedEventArgs e)
        {
            try
            {
                bool isSnapVoxEditorImage = await UiClipboard.HasSnapVoxEditorImageAsync().ConfigureAwait(true);
                using var clipboardImage = await UiClipboard.GetImageAsync().ConfigureAwait(true);
                if (clipboardImage == null) return;

                using var trimmedImage = isSnapVoxEditorImage ? TryTrimSnapVoxFrame(clipboardImage) : null;
                var image = trimmedImage ?? clipboardImage;
                var avaloniaBitmap = snapvox.editor.helpers.ImageSharpAvaloniaHelper.ToAvaloniaBitmap(image);
                var imageControl = new Avalonia.Controls.Image
                {
                    Source = avaloniaBitmap,
                    Width = image.Width,
                    Height = image.Height,
                    Stretch = Avalonia.Media.Stretch.Fill,
                    ZIndex = _currentZIndex++,
                    Tag = "PasteObject"
                };

                Canvas.SetLeft(imageControl, Math.Max(0, (_canvas.Bounds.Width - image.Width) / 2));
                Canvas.SetTop(imageControl, Math.Max(0, (_canvas.Bounds.Height - image.Height) / 2));

                SaveUndoState(true);
                _canvas.Children.Add(imageControl);
                if (_currentTool == EditorTool.None)
                {
                    _selectedControl = imageControl;
                }
                else
                {
                    imageControl.Tag = "PastedImage";
                    _selectedControl = null;
                }

                UpdateSelectionIndicator();
                UpdateThicknessPanelVisibility();
            }
            catch (Exception ex)
            {
                snapvox.foundation.core.LogHelper.GetLogger(typeof(ImageEditorWindow)).Error("Failed to paste image.", ex);
            }
        }

        private static ImageSharpImage TryTrimSnapVoxFrame(ImageSharpImage image)
        {
            if (!HasSnapVoxFrame(image)) return null;
            return image.Clone(x => x.Crop(new SixLabors.ImageSharp.Rectangle(SnapVoxFrameThickness, SnapVoxFrameThickness, image.Width - SnapVoxFrameThickness * 2, image.Height - SnapVoxFrameThickness * 2)));
        }

        private static bool HasSnapVoxFrame(ImageSharpImage image)
        {
            if (image == null || image.Width <= SnapVoxFrameThickness * 2 || image.Height <= SnapVoxFrameThickness * 2)
            {
                return false;
            }

            using var pixels = image.CloneAs<Rgba32>();
            int total = 0;
            int matched = 0;
            int stepX = Math.Max(1, image.Width / 64);
            int stepY = Math.Max(1, image.Height / 64);

            for (int y = 0; y < SnapVoxFrameThickness; y++)
            {
                SampleHorizontalFrameRow(pixels, y, stepX, ref total, ref matched);
                SampleHorizontalFrameRow(pixels, image.Height - 1 - y, stepX, ref total, ref matched);
            }

            for (int x = 0; x < SnapVoxFrameThickness; x++)
            {
                SampleVerticalFrameColumn(pixels, x, stepY, ref total, ref matched);
                SampleVerticalFrameColumn(pixels, image.Width - 1 - x, stepY, ref total, ref matched);
            }

            return total > 0 && matched >= total * 0.85;
        }

        private static void SampleHorizontalFrameRow(SixLabors.ImageSharp.Image<Rgba32> image, int y, int step, ref int total, ref int matched)
        {
            for (int x = 0; x < image.Width; x += step)
            {
                total++;
                if (IsSnapVoxFramePixel(image[x, y])) matched++;
            }
        }

        private static void SampleVerticalFrameColumn(SixLabors.ImageSharp.Image<Rgba32> image, int x, int step, ref int total, ref int matched)
        {
            for (int y = 0; y < image.Height; y += step)
            {
                total++;
                if (IsSnapVoxFramePixel(image[x, y])) matched++;
            }
        }

        private static bool IsSnapVoxFramePixel(Rgba32 pixel)
        {
            return pixel.R <= 30 && pixel.G <= 55 && pixel.B >= 55 && pixel.B <= 160;
        }

        private async void ApplyCropRect(Rect rect)
        {
            if (_image == null || rect.Width < 5 || rect.Height < 5 || !TryBeginEditorOperation()) return;
            try
            {
                var cropRect = ClampImageRectangle(rect);
                if (cropRect.Width < 5 || cropRect.Height < 5) return;
                SaveUndoState(true);
                
                foreach (var child in GetUserAnnotations())
                {
                    Canvas.SetLeft(child, Canvas.GetLeft(child) - cropRect.X);
                    Canvas.SetTop(child, Canvas.GetTop(child) - cropRect.Y);
                }

                await Task.Run(() => _image.Mutate(x => x.Crop(cropRect))).ConfigureAwait(true);
                await UpdateDisplayAsync().ConfigureAwait(true);
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
                SaveUndoState(true);
                int oldW = _image.Width;
                int oldH = _image.Height;
                await Task.Run(() => _image.Mutate(x => x.Rotate(mode))).ConfigureAwait(true);
                
                double rotAngle = 0;
                if (mode == RotateMode.Rotate90) rotAngle = 90;
                else if (mode == RotateMode.Rotate180) rotAngle = 180;
                else if (mode == RotateMode.Rotate270) rotAngle = 270;

                foreach (var child in GetUserAnnotations())
                {
                    if (!TryGetControlBounds(child, out var oldBounds)) continue;
                    
                    double l = oldBounds.X;
                    double t = oldBounds.Y;
                    double w = oldBounds.Width;
                    double h = oldBounds.Height;
                    
                    double newL, newT, newW, newH;
                    if (mode == RotateMode.Rotate90)
                    {
                        newL = oldH - t - h;
                        newT = l;
                        newW = h;
                        newH = w;
                    }
                    else if (mode == RotateMode.Rotate270)
                    {
                        newL = t;
                        newT = oldW - l - w;
                        newW = h;
                        newH = w;
                    }
                    else
                    {
                        newL = oldW - l - w;
                        newT = oldH - t - h;
                        newW = w;
                        newH = h;
                    }

                    if (child is Avalonia.Controls.Shapes.Line line)
                    {
                        var s = line.StartPoint; var e = line.EndPoint;
                        if (mode == RotateMode.Rotate90) { line.StartPoint = new AvaloniaPoint(newW - s.Y, s.X); line.EndPoint = new AvaloniaPoint(newW - e.Y, e.X); }
                        else if (mode == RotateMode.Rotate270) { line.StartPoint = new AvaloniaPoint(s.Y, newH - s.X); line.EndPoint = new AvaloniaPoint(e.Y, newH - e.X); }
                        else if (mode == RotateMode.Rotate180) { line.StartPoint = new AvaloniaPoint(newW - s.X, newH - s.Y); line.EndPoint = new AvaloniaPoint(newW - e.X, newH - e.Y); }
                        Canvas.SetLeft(line, newL); Canvas.SetTop(line, newT);
                    }
                    else if (child is Avalonia.Controls.Shapes.Polyline poly)
                    {
                        var pts = new List<AvaloniaPoint>();
                        foreach (var p in poly.Points)
                        {
                            if (mode == RotateMode.Rotate90) pts.Add(new AvaloniaPoint(newW - p.Y, p.X));
                            else if (mode == RotateMode.Rotate270) pts.Add(new AvaloniaPoint(p.Y, newH - p.X));
                            else if (mode == RotateMode.Rotate180) pts.Add(new AvaloniaPoint(newW - p.X, newH - p.Y));
                        }
                        poly.Points = new Avalonia.Collections.AvaloniaList<AvaloniaPoint>(pts);
                        Canvas.SetLeft(poly, newL); Canvas.SetTop(poly, newT);
                    }
                    else if (child is Canvas group && group.Tag is ArrowProperties props)
                    {
                        if (mode == RotateMode.Rotate90)
                        {
                            props.Start = new AvaloniaPoint(oldH - props.Start.Y, props.Start.X);
                            props.End = new AvaloniaPoint(oldH - props.End.Y, props.End.X);
                        }
                        else if (mode == RotateMode.Rotate270)
                        {
                            props.Start = new AvaloniaPoint(props.Start.Y, oldW - props.Start.X);
                            props.End = new AvaloniaPoint(props.End.Y, oldW - props.End.X);
                        }
                        else if (mode == RotateMode.Rotate180)
                        {
                            props.Start = new AvaloniaPoint(oldW - props.Start.X, oldH - props.Start.Y);
                            props.End = new AvaloniaPoint(oldW - props.End.X, oldH - props.End.Y);
                        }
                        UpdateArrowVisuals(group, props.Start, props.End);
                    }
                    else 
                    {
                        Canvas.SetLeft(child, newL);
                        Canvas.SetTop(child, newT);
                        child.Width = newW;
                        child.Height = newH;

                        var tool = GetToolFromControl(child);
                        if (tool == EditorTool.Text || tool == EditorTool.Counter || tool == EditorTool.Emoji)
                        {
                            var transform = child.RenderTransform as RotateTransform;
                            if (transform == null)
                            {
                                transform = new RotateTransform();
                                child.RenderTransform = transform;
                                child.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
                            }
                            transform.Angle = (transform.Angle + rotAngle) % 360;
                        }
                    }
                }
                UpdateSelectionIndicator();
                await UpdateDisplayAsync().ConfigureAwait(true);
                ShowUndoAvailableHint();
            }
            finally
            {
                EndEditorOperation();
            }
        }

        private void AddAnnotation(AvaloniaControl control)
        {
            control.ZIndex = _currentZIndex++;
            SaveUndoState(true);
            _canvas.Children.Add(control);
            _selectedControl = control;
            UpdateSelectionIndicator();
            UpdateThicknessPanelVisibility();
            UpdateUndoRedoState();
            ShowUndoAvailableHint();
        }

        // IDEA_008 (Ink Alive): plays the juicy stamp-in animation on a newly placed object.
        private static void PlayStampIn(AvaloniaControl control)
        {
            if (control == null) return;
            control.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            control.Classes.Add("ink-stamp");
            // Auto-remove the class once the animation completes so it can replay next time.
            DispatcherTimer.RunOnce(() =>
            {
                control.Classes.Remove("ink-stamp");
                control.RenderTransform = null;
            }, TimeSpan.FromMilliseconds(320));
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

        private async Task<ImageSharpImage> GetFlattenedImageAsync()
        {
            if (_image == null) return null;
            byte[] bytes = await RenderFlattenedImageBytesAsync().ConfigureAwait(true);
            if (bytes == null)
            {
                var source = _image;
                return await Task.Run(() => source.Clone()).ConfigureAwait(true);
            }
            bool addFrameBorders = IniConfig.GetIniSection<CoreConfiguration>().AddFrameBorders;
            return await Task.Run(() => LoadFlattenedImage(bytes, addFrameBorders)).ConfigureAwait(true);
        }

        private async Task<ImageSharpImage> GetFlattenedImageForOcrAsync()
        {
            if (_image == null) return null;
            byte[] bytes = await RenderFlattenedImageBytesAsync().ConfigureAwait(true);
            if (bytes == null)
            {
                var source = _image;
                return await Task.Run(() => source.Clone()).ConfigureAwait(true);
            }
            bool addFrameBorders = IniConfig.GetIniSection<CoreConfiguration>().AddFrameBorders;
            return await Task.Run(() => LoadFlattenedImage(bytes, addFrameBorders)).ConfigureAwait(true);
        }

        private async Task<byte[]> RenderFlattenedImageBytesAsync()
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                await Task.Yield();
                return RenderFlattenedImageBytes();
            }

            return await Dispatcher.UIThread.InvokeAsync(RenderFlattenedImageBytes);
        }

        private byte[] RenderFlattenedImageBytes()
        {
            if (_image == null) return null;
            var chromeState = SetEditorChromeVisible(false);
            Panel originalParent = null;
            int originalIndex = -1;
            double oldZoom = _zoomFactor;
            ITransform oldTransform = null;
            
            Grid renderRoot = null;
            Avalonia.Controls.Image imgClone = null;

            try
            {
                int w = _image.Width;
                int h = _image.Height;
                renderRoot = new Grid { Width = w, Height = h };
                imgClone = new Avalonia.Controls.Image { Source = _displayBitmap, Width = w, Height = h, Stretch = Stretch.None };
                renderRoot.Children.Add(imgClone);

                originalParent = _canvas.Parent as Panel;
                originalIndex = originalParent?.Children.IndexOf(_canvas) ?? -1;
                originalParent?.Children.Remove(_canvas);

                oldTransform = _canvas.RenderTransform;
                _zoomFactor = 1.0;
                _canvas.RenderTransform = null;
                _canvas.Width = w;
                _canvas.Height = h;

                renderRoot.Children.Add(_canvas);

                using var rtb = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
                renderRoot.Measure(new AvaloniaSize(w, h));
                renderRoot.Arrange(new Rect(0, 0, w, h));
                rtb.Render(renderRoot);

                using var ms = new MemoryStream();
                rtb.Save(ms);
                return ms.ToArray();
            }
            catch (Exception ex)
            {
                Log.Error("Flattening failed", ex);
                return null;
            }
            finally
            {
                if (renderRoot != null)
                {
                    renderRoot.Children.Clear();
                }
                
                if (imgClone != null)
                {
                    imgClone.Source = null;
                }

                if (_canvas.Parent != originalParent)
                {
                    (_canvas.Parent as Panel)?.Children.Remove(_canvas);
                    if (originalParent != null && originalIndex != -1)
                    {
                        originalParent.Children.Insert(originalIndex, _canvas);
                    }
                }

                _zoomFactor = oldZoom;
                _canvas.RenderTransform = oldTransform;
                ApplyZoom();
                RestoreVisibility(chromeState);
            }
        }

        private static ImageSharpImage LoadFlattenedImage(byte[] bytes, bool addFrameBorders)
        {
            using var ms = new MemoryStream(bytes);
            var sharpImg = ImageSharpImage.Load(ms);
            if (addFrameBorders)
            {
                int w = sharpImg.Width;
                int h = sharpImg.Height;
                sharpImg.Mutate(x => { 
                    int t = 3; 
                    if (w > t * 2 && h > t * 2) 
                        x.Crop(new SixLabors.ImageSharp.Rectangle(t, t, w - t * 2, h - t * 2))
                         .Pad(w, h, SixLabors.ImageSharp.Color.FromRgb(0, 0, 128)); 
                });
            }
            return sharpImg;
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
        
        private async Task FlattenAnnotationsAsync()
        {
            if (GetUserAnnotations().Count == 0) return;
            var flattened = await GetFlattenedImageAsync().ConfigureAwait(true); 
            if (flattened == null) return;
            _image?.Dispose(); 
            _image = flattened; 
            RemoveUserAnnotations(); 
            _selectedControl = null; 
            UpdateSelectionIndicator(); 
            UpdateThicknessPanelVisibility(); 
            await UpdateDisplayAsync().ConfigureAwait(true);
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
                                new Button { Content = "Yes", Width = 80, Cursor = HandCursor, Background = Brushes.Crimson, Foreground = Brushes.White, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center },
                                new Button { Content = "Cancel", Width = 80, Cursor = HandCursor, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center }
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
            if (confirmed) { SaveUndoState(true); RemoveUserAnnotations(); _selectedControl = null; UpdateSelectionIndicator(); ShowUndoAvailableHint(); }
        }
    }
}
