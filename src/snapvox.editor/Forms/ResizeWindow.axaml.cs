using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System;

namespace snapvox.editor.forms
{
    public partial class ResizeWindow : Window
    {
        private TextBox _widthInput;
        private TextBox _heightInput;
        private CheckBox _ratioCheckbox;
        private double _ratio;
        private bool _isUpdating;

        public int ResultWidth { get; private set; }
        public int ResultHeight { get; private set; }
        public bool IsConfirmed { get; private set; }

        private Slider _sizeSlider;
        private TextBlock _percentText;
        private TextBlock _currentSizeText;
        private int _originalWidth;
        private int _originalHeight;

        public ResizeWindow()
        {
            InitializeComponent();
        }

        public ResizeWindow(int currentWidth, int currentHeight)
        {
            InitializeComponent();
            _widthInput = this.FindControl<TextBox>("WidthInput");
            _heightInput = this.FindControl<TextBox>("HeightInput");
            _ratioCheckbox = this.FindControl<CheckBox>("RatioCheckbox");
            _sizeSlider = this.FindControl<Slider>("SizeSlider");
            _percentText = this.FindControl<TextBlock>("PercentText");
            _currentSizeText = this.FindControl<TextBlock>("CurrentSizeText");

            _originalWidth = currentWidth;
            _originalHeight = currentHeight;
            _ratio = (double)currentWidth / currentHeight;
            _widthInput.Text = currentWidth.ToString();
            _heightInput.Text = currentHeight.ToString();
            if (_currentSizeText != null) _currentSizeText.Text = $"Current: {currentWidth} x {currentHeight}";

            _widthInput.PropertyChanged += OnWidthChanged;
            _heightInput.PropertyChanged += OnHeightChanged;
            _sizeSlider.PropertyChanged += OnSliderChanged;
            UpdatePercentText(100);
            
            KeyDown += OnWindowKeyDown;
            Opened += (_, __) => _widthInput?.Focus();
        }

        private void OnSliderChanged(object sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property.Name == "Value" && !_isUpdating && _ratioCheckbox.IsChecked == true)
            {
                double val = _sizeSlider.Value;
                if (Math.Abs(val - 100) < 3.5) val = 100;

                if (Math.Abs(_sizeSlider.Value - val) > 0.1)
                {
                    _sizeSlider.Value = val;
                    return;
                }

                double scale = val / 100.0;
                _isUpdating = true;
                _widthInput.Text = ((int)(_originalWidth * scale)).ToString();
                _heightInput.Text = ((int)(_originalHeight * scale)).ToString();
                UpdatePercentText(val);
                _isUpdating = false;
            }
        }

        private void OnPresetClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button) return;
            double percent;
            if (button.Tag?.ToString() == "Fit")
            {
                percent = GetFitPercent();
            }
            else if (!double.TryParse(button.Tag?.ToString(), out percent))
            {
                return;
            }

            percent = Math.Clamp(percent, _sizeSlider.Minimum, _sizeSlider.Maximum);
            _sizeSlider.Value = percent;
            ApplyScalePercent(percent);
        }

        private double GetFitPercent()
        {
            var screen = Screens.ScreenFromPoint(Position) ?? Screens.Primary;
            if (screen == null || _originalWidth <= 0 || _originalHeight <= 0) return 100;
            double scaling = screen.Scaling <= 0 ? 1.0 : screen.Scaling;
            double maxWidth = screen.WorkingArea.Width / scaling * 0.9;
            double maxHeight = screen.WorkingArea.Height / scaling * 0.9;
            return Math.Min(maxWidth / _originalWidth, maxHeight / _originalHeight) * 100.0;
        }

        private void ApplyScalePercent(double percent)
        {
            double scale = percent / 100.0;
            _isUpdating = true;
            _widthInput.Text = Math.Max(1, (int)(_originalWidth * scale)).ToString();
            _heightInput.Text = Math.Max(1, (int)(_originalHeight * scale)).ToString();
            UpdatePercentText(percent);
            _isUpdating = false;
        }

        private void UpdatePercentText(double percent)
        {
            if (_percentText != null) _percentText.Text = $"{(int)Math.Round(percent)}%";
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void OnWidthChanged(object sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property.Name == "Text" && !_isUpdating && _ratioCheckbox.IsChecked == true)
            {
                if (int.TryParse(_widthInput.Text, out int w))
                {
                    _isUpdating = true;
                    _heightInput.Text = ((int)(w / _ratio)).ToString();
                    double percent = _originalWidth == 0 ? 100 : w * 100.0 / _originalWidth;
                    _sizeSlider.Value = Math.Clamp(percent, _sizeSlider.Minimum, _sizeSlider.Maximum);
                    UpdatePercentText(percent);
                    _isUpdating = false;
                }
            }
        }

        private void OnHeightChanged(object sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property.Name == "Text" && !_isUpdating && _ratioCheckbox.IsChecked == true)
            {
                if (int.TryParse(_heightInput.Text, out int h))
                {
                    _isUpdating = true;
                    _widthInput.Text = ((int)(h * _ratio)).ToString();
                    double percent = _originalHeight == 0 ? 100 : h * 100.0 / _originalHeight;
                    _sizeSlider.Value = Math.Clamp(percent, _sizeSlider.Minimum, _sizeSlider.Maximum);
                    UpdatePercentText(percent);
                    _isUpdating = false;
                }
            }
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                OnOkClick(sender, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                OnCancelClick(sender, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(_widthInput.Text, out int w) && int.TryParse(_heightInput.Text, out int h))
            {
                ResultWidth = w;
                ResultHeight = h;
                IsConfirmed = true;
                Close();
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            IsConfirmed = false;
            Close();
        }
    }
}
