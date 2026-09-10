using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace snapvox.forms
{
    public partial class ScrollCaptureBarWindow : Window
    {
        private TextBlock _screensBadge;
        private TextBlock _subtextHint;

        public ScrollCaptureBarWindow()
        {
            InitializeComponent();
            _screensBadge = this.FindControl<TextBlock>("ScreensBadge");
            _subtextHint = this.FindControl<TextBlock>("SubtextHint");
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public void UpdateStats(double screens, int frames)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_screensBadge != null)
                {
                    string screenWord = screens <= 1.05 ? "screen" : "screens";
                    _screensBadge.Text = $"Captured: {screens:0.0} {screenWord} ({frames} frames)";
                }
            });
        }

        public void SetHint(string hint)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_subtextHint != null)
                {
                    _subtextHint.Text = hint;
                }
            });
        }

        private void OnFinishClick(object sender, RoutedEventArgs e)
        {
            _ = ScrollCaptureWindow.FinishRecordingFromBarAsync();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            _ = ScrollCaptureWindow.ExitModeFromBarAsync();
        }

        private void OnPillPointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        }
    }
}
