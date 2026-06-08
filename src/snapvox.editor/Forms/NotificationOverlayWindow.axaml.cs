using Avalonia;

using Avalonia.Controls;

using Avalonia.Controls.ApplicationLifetimes;

using Avalonia.Markup.Xaml;

using Avalonia.Media;

using Avalonia.Threading;

using System;

using System.Linq;

using System.Threading.Tasks;


namespace snapvox.editor.forms

{

    public partial class NotificationOverlayWindow : Window
    {
        private static int _activeToasts = 0;
        private static readonly object _toastLock = new object();

        public NotificationOverlayWindow()
        {
            InitializeComponent();
        }


        private void InitializeComponent()

        {

            AvaloniaXamlLoader.Load(this);

        }


        public static void ShowNotification(string message, Window owner)
        {
            Dispatcher.UIThread.Post(async () =>
            {
                var desktop = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                var contextWindow = owner ?? desktop?.Windows.FirstOrDefault(w => w.IsActive) ?? desktop?.Windows.FirstOrDefault();
                if (contextWindow == null) return;
                var targetScreen = contextWindow.Screens.ScreenFromWindow(contextWindow) ?? contextWindow.Screens.Primary;
                if (targetScreen == null) return;

                var window = new NotificationOverlayWindow();
                var textBlock = window.FindControl<TextBlock>("NotificationText");
                var icon = window.FindControl<TextBlock>("NotificationIcon");
                if (textBlock != null) textBlock.Text = message;

                window.Show();
                window.UpdateLayout();

                var bounds = window.Bounds;
                var work = targetScreen.WorkingArea;
                window.Position = new PixelPoint(
                    work.X + (work.Width - (int)bounds.Width) / 2,
                    work.Y + (work.Height - (int)bounds.Height) / 2);

                var whiteBrush = Brushes.White;
                var redBrush = new SolidColorBrush(Color.Parse("#E00000"));
                var blueBrush = new SolidColorBrush(Color.Parse("#007ACC"));
                IBrush[] colors = { whiteBrush, redBrush, blueBrush };

                for (int i = 0; i < 4; i++)
                {
                    IBrush foreground = colors[i % 3];
                    if (textBlock != null) textBlock.Foreground = foreground;
                    if (icon != null) icon.Foreground = foreground;
                    await Task.Delay(250);
                }
                window.Close();
            });
        }

        public static void ShowLightToast(string message, Window owner)
        {
            Dispatcher.UIThread.Post(async () =>
            {
                var desktop = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                var contextWindow = owner ?? desktop?.Windows.FirstOrDefault(w => w.IsActive) ?? desktop?.Windows.FirstOrDefault();
                if (contextWindow == null) return;

                int offset;
                lock(_toastLock) { offset = _activeToasts++; }

                var window = new NotificationOverlayWindow();
                var chrome = window.FindControl<Border>("NotificationChrome");
                var textBlock = window.FindControl<TextBlock>("NotificationText");
                var icon = window.FindControl<TextBlock>("NotificationIcon");
                if (chrome != null)
                {
                    chrome.Background = new SolidColorBrush(Color.Parse("#CC2D2D30"));
                    chrome.Padding = new Thickness(14, 8);
                    chrome.MinWidth = 180;
                }
                if (textBlock != null) { 
                    textBlock.Text = message; 
                    textBlock.FontSize = 14;
                    textBlock.FontWeight = FontWeight.SemiBold;
                    textBlock.Foreground = new SolidColorBrush(Color.Parse("#FFF2B84B")); 
                    textBlock.MaxWidth = 300;
                }
                if (icon != null) { 
                    icon.Text = "\uE946"; // Info icon
                    icon.FontSize = 16;
                    icon.Foreground = new SolidColorBrush(Color.Parse("#FFF2B84B"));
                }

                window.Opacity = 0;
                window.Show();
                window.UpdateLayout();

                var bounds = window.Bounds;
                var ownerBounds = contextWindow.Bounds;
                var ownerPos = contextWindow.Position;
                
                bool positioned = false;
                var snipBorder = contextWindow.FindControl<Control>("SnipBorder");
                if (snipBorder != null)
                {
                    var snipTop = snipBorder.TranslatePoint(new Point(0, 0), contextWindow);
                    if (snipTop.HasValue)
                    {
                        double toolbarBottom = 92; // Approx 32 title + 60 toolbar
                        double voidHeight = snipTop.Value.Y - toolbarBottom;
                        if (voidHeight > bounds.Height + 20)
                        {
                            // Place in the top void
                            double targetY = ownerPos.Y + toolbarBottom + (voidHeight - bounds.Height) / 2 + (offset * (bounds.Height + 5));
                            window.Position = new PixelPoint(
                                ownerPos.X + (int)(ownerBounds.Width - bounds.Width) / 2,
                                (int)targetY);
                            positioned = true;
                        }
                    }
                }

                if (!positioned)
                {
                    var targetScreen = contextWindow.Screens.ScreenFromWindow(contextWindow) ?? contextWindow.Screens.Primary;
                    var work = targetScreen.WorkingArea;
                    window.Position = new PixelPoint(
                        work.X + (work.Width - (int)bounds.Width) / 2,
                        work.Y + (work.Height - (int)bounds.Height) / 2 + 100 + (offset * (int)(bounds.Height + 5)));
                }

                // Fade in (200ms)
                for (int i = 0; i < 5; i++) { window.Opacity += 0.2; await Task.Delay(40); }
                window.Opacity = 1.0;
                
                await Task.Delay(800); // Display (800ms)
                
                // Fade out (200ms)
                for (int i = 0; i < 5; i++) { window.Opacity -= 0.2; await Task.Delay(40); }
                window.Close();
                lock(_toastLock) { _activeToasts--; if (_activeToasts < 0) _activeToasts = 0; }
            });
        }

    }

}


