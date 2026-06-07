using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System;
using System.Threading.Tasks;
using snapvox.foundation.core;
using snapvox.editor.helpers;

namespace snapvox.editor.forms
{
    public partial class CustomColorPickerWindow : Window
    {
        public Color SelectedColor { get; private set; }
        public bool IsConfirmed { get; private set; }

        public CustomColorPickerWindow() : this(Colors.Blue) { }

        public CustomColorPickerWindow(Color initialColor)
        {
            InitializeComponent();
            var picker = this.FindControl<ColorPicker>("Picker");
            if (picker != null) picker.Color = initialColor;
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            var picker = this.FindControl<ColorPicker>("Picker");
            if (picker != null) SelectedColor = picker.Color;
            IsConfirmed = true;
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            IsConfirmed = false;
            Close();
        }

        private async void OnSamplerClick(object sender, RoutedEventArgs e)
        {
            Hide();
            try
            {
                var color = await ScreenColorSampler.PickColorAsync();
                if (color.HasValue)
                {
                    var picker = this.FindControl<ColorPicker>("Picker");
                    if (picker != null) picker.Color = color.Value;
                }
            }
            finally
            {
                Show();
            }
        }
    }

    public static class ScreenColorSampler
    {
        public static async Task<Color?> PickColorAsync()
        {
            var bounds = NativeCapture.GetVirtualDesktopBounds();
            using var screenShot = NativeCapture.CaptureRegion(bounds);
            if (screenShot == null) return null;

            var tcs = new TaskCompletionSource<Color?>();
            Bitmap magnifierBitmap = null;
            Avalonia.Controls.Image magnifierImage = null;
            var samplerWindow = new Window
            {
                SystemDecorations = SystemDecorations.None,
                Background = Brushes.Transparent,
                Topmost = true,
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Cross),
                WindowStartupLocation = WindowStartupLocation.Manual
            };

            samplerWindow.Position = new PixelPoint(bounds.Left, bounds.Top);
            
            double scaling = 1.0;
            var screen = samplerWindow.Screens.ScreenFromPoint(new PixelPoint(bounds.Left, bounds.Top));
            if (screen != null) scaling = screen.Scaling;

            samplerWindow.Width = bounds.Width / scaling;
            samplerWindow.Height = bounds.Height / scaling;

            var canvas = new Canvas { Background = Brushes.Transparent };
            samplerWindow.Content = canvas;

            var instruction = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(220, 30, 30, 30)),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 5),
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = "Click a color. Esc cancels.",
                    Foreground = Brushes.White,
                    FontSize = 12,
                    FontWeight = FontWeight.SemiBold
                }
            };

            var magnifierBorder = new Border
            {
                Width = 120, Height = 120,
                BorderBrush = Brushes.White, BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(60),
                BoxShadow = new BoxShadows(new BoxShadow { Blur = 10, Color = Colors.Black }),
                ClipToBounds = true,
                IsVisible = false,
                Background = Brushes.Black
            };

            magnifierBitmap = ImageSharpAvaloniaHelper.ToAvaloniaBitmap(screenShot);
            magnifierImage = new Avalonia.Controls.Image
            {
                Width = screenShot.Width, Height = screenShot.Height,
                Source = magnifierBitmap,
                Stretch = Stretch.None
            };
            
            var magnifierCanvas = new Canvas { Width = 120, Height = 120 };
            magnifierCanvas.Children.Add(magnifierImage);
            magnifierBorder.Child = magnifierCanvas;
            
            magnifierCanvas.Children.Add(new Avalonia.Controls.Shapes.Line { StartPoint = new Point(60, 0), EndPoint = new Point(60, 120), Stroke = Brushes.Red, StrokeThickness = 1 });
            magnifierCanvas.Children.Add(new Avalonia.Controls.Shapes.Line { StartPoint = new Point(0, 60), EndPoint = new Point(120, 60), Stroke = Brushes.Red, StrokeThickness = 1 });

            var colorLabel = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
                Padding = new Thickness(5),
                CornerRadius = new CornerRadius(4),
                Child = new TextBlock { Foreground = Brushes.White, FontSize = 10 },
                IsVisible = false
            };

            canvas.Children.Add(magnifierBorder);
            canvas.Children.Add(colorLabel);
            canvas.Children.Add(instruction);
            Canvas.SetLeft(instruction, 20);
            Canvas.SetTop(instruction, 20);

            samplerWindow.PointerMoved += (s, e) =>
            {
                var pos = e.GetPosition(samplerWindow);
                var screenPoint = samplerWindow.PointToScreen(pos);
                int px = screenPoint.X - bounds.Left;
                int py = screenPoint.Y - bounds.Top;

                if (px >= 0 && px < screenShot.Width && py >= 0 && py < screenShot.Height)
                {
                    var pixel = screenShot[px, py];
                    var color = Color.FromArgb(pixel.A, pixel.R, pixel.G, pixel.B);
                    
                    magnifierBorder.IsVisible = true;
                    colorLabel.IsVisible = true;
                    
                    Canvas.SetLeft(magnifierBorder, pos.X + 20);
                    Canvas.SetTop(magnifierBorder, pos.Y + 20);
                    
                    Canvas.SetLeft(colorLabel, pos.X + 20);
                    Canvas.SetTop(colorLabel, pos.Y + 145);
                    
                    ((TextBlock)colorLabel.Child).Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
                    
                    Canvas.SetLeft(magnifierImage, (-px * 8) + 60);
                    Canvas.SetTop(magnifierImage, (-py * 8) + 60);
                    magnifierImage.RenderTransform = new ScaleTransform(8, 8);
                    magnifierImage.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
                }
            };

            samplerWindow.PointerPressed += (s, e) =>
            {
                var screenPoint = samplerWindow.PointToScreen(e.GetPosition(samplerWindow));
                int px = screenPoint.X - bounds.Left;
                int py = screenPoint.Y - bounds.Top;
                
                if (px >= 0 && px < screenShot.Width && py >= 0 && py < screenShot.Height)
                {
                    var pixel = screenShot[px, py];
                    tcs.SetResult(Color.FromArgb(pixel.A, pixel.R, pixel.G, pixel.B));
                }
                else
                {
                    tcs.SetResult(null);
                }
                samplerWindow.Close();
            };

            samplerWindow.KeyDown += (s, e) => { if (e.Key == Avalonia.Input.Key.Escape) { tcs.TrySetResult(null); samplerWindow.Close(); } };
            samplerWindow.Closed += (s, e) => tcs.TrySetResult(null);

            try
            {
                samplerWindow.Show();
                var result = await tcs.Task;
                samplerWindow.Close(); 
                return result;
            }
            finally
            {
                if (magnifierImage != null)
                {
                    magnifierImage.Source = null;
                }

                magnifierBitmap?.Dispose();
            }
        }
    }
}
