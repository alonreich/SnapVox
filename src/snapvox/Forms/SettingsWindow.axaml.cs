using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using snapvox.foundation.core;
using snapvox.foundation.IniFile;
using snapvox.helpers;
using snapvox.foundation.interfaces.Ocr;
using snapvox.foundation.core.AvaloniaShims;
using snapvox.editor.helpers;
using System.Linq;
using System.Threading.Tasks;

namespace snapvox.Forms
{
    public partial class SettingsWindow : Window
    {
        private CoreConfiguration _config;

        public SettingsWindow()
        {
            InitializeComponent();
            _config = IniConfig.GetIniSection<CoreConfiguration>();
            LoadSettings();
        }

        private void LoadSettings()
        {
            var chkKeepBackup = this.FindControl<CheckBox>("ChkKeepBackup");
            if (chkKeepBackup != null) chkKeepBackup.IsChecked = _config.KeepBackup;

            var chkCloseEditor = this.FindControl<CheckBox>("ChkCloseEditor");
            if (chkCloseEditor != null) chkCloseEditor.IsChecked = _config.CloseEditorOnAction;

            var chkAddBorder = this.FindControl<CheckBox>("ChkAddBorder");
            if (chkAddBorder != null) chkAddBorder.IsChecked = _config.AddFrameBorders;

            var ocrPanel = this.FindControl<StackPanel>("OcrEnginePanel");
            var cboOcrEngine = this.FindControl<ComboBox>("CboOcrEngine");
            if (cboOcrEngine != null)
            {
                var providers = SimpleServiceProvider.Current.GetAllInstances<IOcrProvider>().ToList();
                var providerNames = providers.Select(provider => provider.DisplayName).Distinct().ToList();
                cboOcrEngine.ItemsSource = providerNames;
                cboOcrEngine.SelectedItem = providerNames.Contains(_config.OcrEngine) ? _config.OcrEngine : providerNames.FirstOrDefault();
            }

            _ = UpdateAdminButtonStateAsync();

            SetHotkeyTextBox("TxtArrowKey", _config.ArrowHotkey);
            SetHotkeyTextBox("TxtLineKey", _config.LineHotkey);
            SetHotkeyTextBox("TxtTextKey", _config.TextHotkey);
            SetHotkeyTextBox("TxtResizeKey", _config.ResizeHotkey);
            SetHotkeyTextBox("TxtFreehandKey", _config.FreehandHotkey);
            SetHotkeyTextBox("TxtEmojiKey", _config.EmojiHotkey);
            SetHotkeyTextBox("TxtCounterKey", _config.CounterHotkey);
            SetHotkeyTextBox("TxtHighlightKey", _config.HighlightHotkey);
            SetHotkeyTextBox("TxtPixelate1Key", _config.PixelateHotkey1);
            SetHotkeyTextBox("TxtPixelate2Key", _config.PixelateHotkey2);
            SetHotkeyTextBox("TxtCropKey", _config.CropHotkey);
            SetHotkeyTextBox("TxtRotateCwKey", _config.RotateCwHotkey);
            SetHotkeyTextBox("TxtRotateCcwKey", _config.RotateCcwHotkey);
            SetHotkeyTextBox("TxtDuplicateObjectKey", _config.DuplicateObjectHotkey);
            SetHotkeyTextBox("TxtDeleteObjectKey", _config.DeleteObjectHotkey);
            
            SetHotkeyTextBox("TxtRegionKey", _config.RegionHotkey);
            SetHotkeyTextBox("TxtWindowKey", _config.WindowHotkey);
            SetHotkeyTextBox("TxtFullscreenKey", _config.FullscreenHotkey);
            SetHotkeyTextBox("TxtLastRegionKey", _config.LastregionHotkey);
            SetHotkeyTextBox("TxtClipboardKey", _config.ClipboardHotkey);
        }

        private void SetHotkeyTextBox(string name, string value)
        {
            var txt = this.FindControl<TextBox>(name);
            if (txt != null) txt.Text = value;
        }

        private void OnResetHotkeysClick(object sender, RoutedEventArgs e)
        {
            SetHotkeyTextBox("TxtRegionKey", "PrintScreen");
            SetHotkeyTextBox("TxtWindowKey", "Alt + PrintScreen");
            SetHotkeyTextBox("TxtFullscreenKey", "Ctrl + PrintScreen");
            SetHotkeyTextBox("TxtLastRegionKey", "None");
            SetHotkeyTextBox("TxtClipboardKey", "None");
            SetHotkeyTextBox("TxtArrowKey", "A");
            SetHotkeyTextBox("TxtLineKey", "L");
            SetHotkeyTextBox("TxtTextKey", "T");
            SetHotkeyTextBox("TxtResizeKey", "R");
            SetHotkeyTextBox("TxtFreehandKey", "D");
            SetHotkeyTextBox("TxtEmojiKey", "E");
            SetHotkeyTextBox("TxtCounterKey", "I");
            SetHotkeyTextBox("TxtHighlightKey", "H");
            SetHotkeyTextBox("TxtPixelate1Key", "O");
            SetHotkeyTextBox("TxtPixelate2Key", "P");
            SetHotkeyTextBox("TxtCropKey", "C");
            SetHotkeyTextBox("TxtRotateCwKey", "Right");
            SetHotkeyTextBox("TxtRotateCcwKey", "Left");
            SetHotkeyTextBox("TxtDuplicateObjectKey", "Ctrl + D");
            SetHotkeyTextBox("TxtDeleteObjectKey", "Delete");

            foreach (var textBox in this.GetVisualDescendants().OfType<TextBox>())
            {
                textBox.Background = Brushes.Transparent;
            }

            var warning = this.FindControl<TextBlock>("TxtHotkeyWarning");
            if (warning != null) warning.IsVisible = false;
            OverlayHelper.ShowNotification("Hotkeys reset. Save to apply.", this);
        }

        private void OnHotkeyTextBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                if (e.Key != Key.None && e.Key != Key.LWin && e.Key != Key.RWin && e.Key != Key.LeftShift && e.Key != Key.RightShift && e.Key != Key.LeftCtrl && e.Key != Key.RightCtrl && e.Key != Key.LeftAlt && e.Key != Key.RightAlt)
                {
                    var modifiers = new System.Collections.Generic.List<string>();
                    if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) modifiers.Add("Ctrl");
                    if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) modifiers.Add("Alt");
                    if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) modifiers.Add("Shift");
                    if (e.KeyModifiers.HasFlag(KeyModifiers.Meta)) modifiers.Add("Win");
                    
                    string keyName = e.Key.ToString();
                    string combined = modifiers.Count > 0 ? string.Join(" + ", modifiers) + " + " + keyName : keyName;
                    textBox.Text = combined;
                    e.Handled = true;

                    if (textBox.Name == "TxtRegionKey" || textBox.Name == "TxtWindowKey" || textBox.Name == "TxtFullscreenKey")
                    {
                        ValidateGlobalHotkey(textBox, combined);
                    }
                    else
                    {
                        textBox.Background = Brushes.Transparent;
                    }
                }
            }
        }

        private void ValidateGlobalHotkey(TextBox textBox, string hotkey)
        {
            var warning = this.FindControl<TextBlock>("TxtHotkeyWarning");
            bool conflict = false;
            try
            {
                conflict = !HotkeyManager.IsHotkeyAvailable(hotkey);
            }
            catch { conflict = true; }

            if (conflict)
            {
                textBox.Background = new SolidColorBrush(Avalonia.Media.Color.Parse("#55FF0000"));
                if (warning != null)
                {
                    warning.Text = $"The shortcut key of {hotkey} is already taken by another app. Please release this key from the other app or select a different key and try again.";
                    warning.IsVisible = true;
                }
            }
            else
            {
                textBox.Background = Brushes.Transparent;
                if (warning != null) warning.IsVisible = false;
            }
        }

        private async Task UpdateAdminButtonStateAsync()
        {
            var btn = this.FindControl<Button>("BtnToggleAdmin");
            if (btn != null)
            {
                if (await StartupTaskHelper.HasElevatedStartupTaskAsync().ConfigureAwait(true))
                {
                    btn.Content = "Remove Administrator Permissions";
                    btn.Background = new SolidColorBrush(Avalonia.Media.Color.Parse("#AA4444"));
                }
                else
                {
                    btn.Content = "Run This App As an Administrator (Highest Privileges)";
                    btn.Background = new SolidColorBrush(Avalonia.Media.Color.Parse("#333333"));
                }
            }
        }

        private async void OnToggleAdminClick(object sender, RoutedEventArgs e)
        {
            bool wasAdmin = await StartupTaskHelper.HasElevatedStartupTaskAsync().ConfigureAwait(true);
            try
            {
                if (wasAdmin)
                {
                    await StartupTaskHelper.DeleteElevatedStartupTaskAsync().ConfigureAwait(true);
                    if (!await StartupTaskHelper.HasElevatedStartupTaskAsync().ConfigureAwait(true))
                    {
                        OverlayHelper.ShowNotification("Admin Startup Removed", this);
                    }
                    else
                    {
                        OverlayHelper.ShowNotification("Failed to Remove Admin", this);
                    }
                }
                else
                {
                    await StartupTaskHelper.ConfigureElevatedStartupTaskAsync().ConfigureAwait(true);
                    if (await StartupTaskHelper.HasElevatedStartupTaskAsync().ConfigureAwait(true))
                    {
                        OverlayHelper.ShowNotification("Admin Startup Configured", this);
                    }
                    else
                    {
                        OverlayHelper.ShowNotification("Failed to Configure Admin", this);
                    }
                }
            }
            catch
            {
                OverlayHelper.ShowNotification("Permission Error", this);
            }
            await UpdateAdminButtonStateAsync().ConfigureAwait(true);
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            var globalKeys = new[] { "TxtRegionKey", "TxtWindowKey", "TxtFullscreenKey" };
            foreach (var name in globalKeys)
            {
                var tb = this.FindControl<TextBox>(name);
                if (tb != null && tb.Background is SolidColorBrush scb && scb.Color.R > 200)
                {
                    StartupTaskHelper.ShowForegroundMessageBox(
                        $"The shortcut key of {tb.Text} is already taken by another app. Please release this key from the other app or select a different key and try again.",
                        "Hotkey Conflict", 
                        MessageBoxButtons.OK, 
                        MessageBoxIcon.Error);
                    return;
                }
            }

            var chkKeepBackup = this.FindControl<CheckBox>("ChkKeepBackup");
            if (chkKeepBackup != null) _config.KeepBackup = chkKeepBackup.IsChecked ?? true;

            var chkCloseEditor = this.FindControl<CheckBox>("ChkCloseEditor");
            if (chkCloseEditor != null) _config.CloseEditorOnAction = chkCloseEditor.IsChecked ?? true;

            var chkWarnClose = this.FindControl<CheckBox>("ChkWarnClose");
            if (chkWarnClose != null) _config.WarnBeforeClosingEditor = chkWarnClose.IsChecked ?? false;

            var chkAddBorder = this.FindControl<CheckBox>("ChkAddBorder");
            if (chkAddBorder != null) _config.AddFrameBorders = chkAddBorder.IsChecked ?? true;

            var cboOcrEngine = this.FindControl<ComboBox>("CboOcrEngine");
            if (cboOcrEngine != null && cboOcrEngine.IsVisible && cboOcrEngine.SelectedItem != null)
            {
                _config.OcrEngine = cboOcrEngine.SelectedItem.ToString();
            }

            _config.ArrowHotkey = this.FindControl<TextBox>("TxtArrowKey")?.Text ?? _config.ArrowHotkey;
            _config.LineHotkey = this.FindControl<TextBox>("TxtLineKey")?.Text ?? _config.LineHotkey;
            _config.TextHotkey = this.FindControl<TextBox>("TxtTextKey")?.Text ?? _config.TextHotkey;
            _config.ResizeHotkey = this.FindControl<TextBox>("TxtResizeKey")?.Text ?? _config.ResizeHotkey;
            _config.FreehandHotkey = this.FindControl<TextBox>("TxtFreehandKey")?.Text ?? _config.FreehandHotkey;
            _config.EmojiHotkey = this.FindControl<TextBox>("TxtEmojiKey")?.Text ?? _config.EmojiHotkey;
            _config.CounterHotkey = this.FindControl<TextBox>("TxtCounterKey")?.Text ?? _config.CounterHotkey;
            _config.HighlightHotkey = this.FindControl<TextBox>("TxtHighlightKey")?.Text ?? _config.HighlightHotkey;
            _config.PixelateHotkey1 = this.FindControl<TextBox>("TxtPixelate1Key")?.Text ?? _config.PixelateHotkey1;
            _config.PixelateHotkey2 = this.FindControl<TextBox>("TxtPixelate2Key")?.Text ?? _config.PixelateHotkey2;
            _config.CropHotkey = this.FindControl<TextBox>("TxtCropKey")?.Text ?? _config.CropHotkey;
            _config.DuplicateObjectHotkey = this.FindControl<TextBox>("TxtDuplicateObjectKey")?.Text ?? _config.DuplicateObjectHotkey;
            _config.DeleteObjectHotkey = this.FindControl<TextBox>("TxtDeleteObjectKey")?.Text ?? _config.DeleteObjectHotkey;
            
            string oldRegion = _config.RegionHotkey;
            string oldWindow = _config.WindowHotkey;
            string oldFull = _config.FullscreenHotkey;
            string oldLast = _config.LastregionHotkey;
            string oldClip = _config.ClipboardHotkey;
            
            _config.RegionHotkey = this.FindControl<TextBox>("TxtRegionKey")?.Text ?? _config.RegionHotkey;
            _config.WindowHotkey = this.FindControl<TextBox>("TxtWindowKey")?.Text ?? _config.WindowHotkey;
            _config.FullscreenHotkey = this.FindControl<TextBox>("TxtFullscreenKey")?.Text ?? _config.FullscreenHotkey;
            _config.LastregionHotkey = this.FindControl<TextBox>("TxtLastRegionKey")?.Text ?? _config.LastregionHotkey;
            _config.ClipboardHotkey = this.FindControl<TextBox>("TxtClipboardKey")?.Text ?? _config.ClipboardHotkey;

            IniConfig.Save();
            
            if (oldRegion != _config.RegionHotkey || oldWindow != _config.WindowHotkey || oldFull != _config.FullscreenHotkey || oldLast != _config.LastregionHotkey || oldClip != _config.ClipboardHotkey)
            {
                HotkeyManager.Stop();
                HotkeyManager.Start();
            }
            
            Close();
        }
    }
}
