using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
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
        private string _loadedFingerprint = string.Empty;
        private bool _savedAndClosing;
        private bool _saveInProgress;

        public SettingsWindow()
        {
            InitializeComponent();
            UiLayoutDirection.Apply(this);
            _config = IniConfig.GetIniSection<CoreConfiguration>();
            LoadSettings();
            _loadedFingerprint = BuildFingerprint();
        }

        private string BuildFingerprint()
        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (var box in this.GetVisualDescendants().OfType<TextBox>())
            {
                parts.Add((box.Name ?? string.Empty) + "=" + (box.Text ?? string.Empty));
            }

            foreach (var check in this.GetVisualDescendants().OfType<CheckBox>())
            {
                parts.Add((check.Name ?? string.Empty) + "=" + (check.IsChecked ?? false));
            }

            foreach (var combo in this.GetVisualDescendants().OfType<ComboBox>())
            {
                parts.Add((combo.Name ?? string.Empty) + "=" + (combo.SelectedItem?.ToString() ?? string.Empty));
            }

            parts.Sort(System.StringComparer.Ordinal);
            return string.Join("|", parts);
        }

        private bool HasUnsavedChanges()
        {
            return !string.Equals(_loadedFingerprint, BuildFingerprint(), System.StringComparison.Ordinal);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void LoadSettings()
        {
            var chkKeepBackup = this.FindControl<CheckBox>("ChkKeepBackup");
            if (chkKeepBackup != null) chkKeepBackup.IsChecked = _config.KeepBackup;

            var chkCloseEditor = this.FindControl<CheckBox>("ChkCloseEditor");
            if (chkCloseEditor != null) chkCloseEditor.IsChecked = _config.CloseEditorOnAction;

            var chkAddBorder = this.FindControl<CheckBox>("ChkAddBorder");
            if (chkAddBorder != null) chkAddBorder.IsChecked = _config.AddFrameBorders;

            var chkLeavePictureAsIs = this.FindControl<CheckBox>("ChkLeavePictureAsIs");
            if (chkLeavePictureAsIs != null) chkLeavePictureAsIs.IsChecked = _config.LeavePictureAsIsDuringOcr;

            var chkWarnClose = this.FindControl<CheckBox>("ChkWarnClose");
            if (chkWarnClose != null) chkWarnClose.IsChecked = _config.WarnBeforeClosingEditor;

            var cboOverlayDuration = this.FindControl<ComboBox>("CboOverlayDuration");
            if (cboOverlayDuration != null)
            {
                var overlayChoices = new[] { "0.5 s (fast)", "1 s (normal)", "2 s (slow)", "3 s (slower)", "5 s (longest)" };
                cboOverlayDuration.ItemsSource = overlayChoices;
                int overlayMs = _config.NotificationOverlayDurationMs;
                string overlaySelected = overlayMs <= 500 ? overlayChoices[0]
                    : overlayMs <= 1000 ? overlayChoices[1]
                    : overlayMs <= 2000 ? overlayChoices[2]
                    : overlayMs <= 3000 ? overlayChoices[3]
                    : overlayChoices[4];
                cboOverlayDuration.SelectedItem = overlaySelected;
            }

            var ocrPanel = this.FindControl<StackPanel>("OcrEnginePanel");
            var cboOcrEngine = this.FindControl<ComboBox>("CboOcrEngine");
            var ocrEmptyState = this.FindControl<Border>("OcrEngineEmptyState");
            var ocrEmptyText = this.FindControl<TextBlock>("OcrEngineEmptyText");
            if (cboOcrEngine != null)
            {
                var providers = SimpleServiceProvider.Current.GetAllInstances<IOcrProvider>().ToList();
                var providerNames = providers.Select(provider => provider.DisplayName).Distinct().ToList();
                cboOcrEngine.ItemsSource = providerNames;
                cboOcrEngine.SelectedItem = providerNames.Contains(_config.OcrEngine) ? _config.OcrEngine : providerNames.FirstOrDefault();

                bool hasProviders = providerNames.Count > 0;
                bool hasUsableProvider = providers.Any(provider => SafeHasLanguages(provider));

                var chkAdaptive = this.FindControl<CheckBox>("ChkOcrAdaptiveThreshold");
                if (chkAdaptive != null)
                {
#if USE_TESSERACT
                    chkAdaptive.IsVisible = providers.Any(provider =>
                        provider != null && provider.EngineId != null &&
                        provider.EngineId.IndexOf("tesseract", System.StringComparison.OrdinalIgnoreCase) >= 0);
                    chkAdaptive.IsChecked = _config.OcrAdaptiveThreshold;
#else
                    chkAdaptive.IsVisible = false;
#endif
                }
                cboOcrEngine.IsVisible = hasProviders;
                cboOcrEngine.IsEnabled = hasProviders;

                if (ocrEmptyState != null)
                {
                    ocrEmptyState.IsVisible = !hasProviders || !hasUsableProvider;
                    if (ocrEmptyState.IsVisible && ocrEmptyText != null)
                    {
                        ocrEmptyText.Text = !hasProviders
                            ? "No text-recognition engine is registered, so OCR is unavailable. Restart SnapVox; if it persists, reinstall the application."
                            : "The installed engine is missing its English or Hebrew language pack, so OCR will fail. Add both languages in Windows Settings > Time & language > Language & region, then restart SnapVox.";
                    }
                }
            }
            else if (ocrEmptyState != null)
            {
                ocrEmptyState.IsVisible = false;
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
            SetHotkeyTextBox("TxtScrollCaptureDelimiterKey", _config.ScrollCaptureDelimiterHotkey);

            UpdatePrintScreenConflictWarning();
            _ = Task.Run(async () =>
            {
                await PrintScreenConflictHelper.WaitForHotkeyRegistrationAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                if (!PrintScreenConflictHelper.IsPrintScreenBlocked(out _)) return;
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(UpdatePrintScreenConflictWarning);
            });
        }

        private void SetHotkeyTextBox(string name, string value)
        {
            var txt = this.FindControl<TextBox>(name);
            if (txt != null) txt.Text = value;
        }

        private void UpdatePrintScreenConflictWarning()
        {
            var panel = this.FindControl<Border>("PrintScreenConflictPanel");
            var detail = this.FindControl<TextBlock>("TxtPrintScreenConflictDetail");
            if (panel == null || detail == null) return;

            if (PrintScreenConflictHelper.IsPrintScreenBlocked(out string reason))
            {
                detail.Text = PrintScreenConflictHelper.BuildSettingsWarning(reason);
                panel.IsVisible = true;
            }
            else
            {
                panel.IsVisible = false;
            }
        }

        private async void OnResetHotkeysClick(object sender, RoutedEventArgs e)
        {
            bool confirmed = await ConfirmDialog.ShowAsync(
                this,
                "Reset all hotkeys?",
                "Every shortcut on this tab goes back to the SnapVox factory default. Any key combinations you set yourself are replaced and cannot be recovered.",
                "Reset All Hotkeys",
                "Keep My Hotkeys",
                true).ConfigureAwait(true);

            if (!confirmed) return;

            SetHotkeyTextBox("TxtRegionKey", "PrintScreen");
            SetHotkeyTextBox("TxtWindowKey", "Alt + PrintScreen");
            SetHotkeyTextBox("TxtFullscreenKey", "Ctrl + PrintScreen");
            SetHotkeyTextBox("TxtLastRegionKey", "None");
            SetHotkeyTextBox("TxtClipboardKey", "None");
            SetHotkeyTextBox("TxtScrollCaptureDelimiterKey", "Space");
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

                    if (textBox.Name == "TxtRegionKey" || textBox.Name == "TxtWindowKey" || textBox.Name == "TxtFullscreenKey" || textBox.Name == "TxtLastRegionKey" || textBox.Name == "TxtClipboardKey")
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

            textBox.Classes.Remove("hotkey-conflict");
            if (conflict)
            {
                textBox.Classes.Add("hotkey-conflict");
                textBox.Tag = "conflict";
                if (warning != null)
                {
                    warning.Text = $"\"{hotkey}\" is taken by another app or by another SnapVox box.";
                    warning.IsVisible = true;
                }
            }
            else
            {
                textBox.Tag = null;
                ClearHotkeyConflictStyles();
                if (warning != null) warning.IsVisible = false;
            }
        }

        private void ClearHotkeyConflictStyles()
        {
            foreach (var tb in this.GetVisualDescendants().OfType<TextBox>())
            {
                if (tb.Name != null && tb.Name.StartsWith("Txt", StringComparison.Ordinal))
                {
                    tb.Classes.Remove("hotkey-conflict");
                }
            }
        }

        private async void OnCancelClick(object sender, RoutedEventArgs e)
        {
            if (HasUnsavedChanges())
            {
                bool discard = await ConfirmDialog.ShowAsync(
                    this,
                    "Discard your changes?",
                    "You changed settings but did not save them. Closing now throws those changes away.",
                    "Discard Changes",
                    "Keep Editing",
                    true).ConfigureAwait(true);

                if (!discard) return;
            }

            _savedAndClosing = true;
            Close();
        }

        private async void OnSettingsClosing(object sender, WindowClosingEventArgs e)
        {
            if (_savedAndClosing || !HasUnsavedChanges()) return;

            e.Cancel = true;
            bool discard = await ConfirmDialog.ShowAsync(
                this,
                "Discard your changes?",
                "You changed settings but did not save them. Closing now throws those changes away.",
                "Discard Changes",
                "Keep Editing",
                true).ConfigureAwait(true);

            if (!discard) return;

            _savedAndClosing = true;
            Close();
        }

        private static bool SafeHasLanguages(IOcrProvider provider)
        {
            try
            {
                return provider != null && provider.HasRequiredLanguages();
            }
            catch
            {
                return false;
            }
        }

        private async Task UpdateAdminButtonStateAsync()
        {
            var btn = this.FindControl<Button>("BtnToggleAdmin");
            var stateLabel = this.FindControl<TextBlock>("AdminStateLabel");
            bool isAdmin = await StartupTaskHelper.HasElevatedStartupTaskAsync().ConfigureAwait(true);

            if (btn != null)
            {
                if (isAdmin)
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

            if (stateLabel != null)
            {
                stateLabel.Text = isAdmin ? "Status: Administrator startup is ENABLED" : "Status: Not configured (standard privileges)";
            }
        }

        private async void OnToggleAdminClick(object sender, RoutedEventArgs e)
        {
            bool wasAdmin = await StartupTaskHelper.HasElevatedStartupTaskAsync().ConfigureAwait(true);
            try
            {
                if (wasAdmin)
                {
                    bool removed = await StartupTaskHelper.DeleteElevatedStartupTaskAsync().ConfigureAwait(true);
                    if (removed && !await StartupTaskHelper.HasElevatedStartupTaskAsync().ConfigureAwait(true))
                    {
                        _config.RunAsAdministratorOnStartup = false;
                        IniConfig.Save();
                        OverlayHelper.ShowNotification("Admin Startup Removed", this);
                    }
                    else
                    {
                        OverlayHelper.ShowNotification("Failed to Remove Admin", this);
                    }
                }
                else
                {
                    bool configured = await StartupTaskHelper.ConfigureElevatedStartupTaskAsync().ConfigureAwait(true);
                    if (configured && await StartupTaskHelper.HasElevatedStartupTaskAsync().ConfigureAwait(true))
                    {
                        _config.RunAsAdministratorOnStartup = true;
                        IniConfig.Save();
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

        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (_saveInProgress) return;
            _saveInProgress = true;
            try
            {
                var globalKeys = new[] { "TxtRegionKey", "TxtWindowKey", "TxtFullscreenKey", "TxtLastRegionKey", "TxtClipboardKey" };
                foreach (var name in globalKeys)
                {
                    var tb = this.FindControl<TextBox>(name);
                    if (tb != null && tb.Tag is string tag && tag == "conflict")
                    {
                        await ConfirmDialog.ShowAlertAsync(
                            this,
                            "Hotkey Conflict",
                            $"The shortcut key of {tb.Text} is already taken by another app. Please release this key from the other app or select a different key and try again.",
                            "OK",
                            true).ConfigureAwait(true);
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

                var chkLeavePictureAsIs = this.FindControl<CheckBox>("ChkLeavePictureAsIs");
                if (chkLeavePictureAsIs != null) _config.LeavePictureAsIsDuringOcr = chkLeavePictureAsIs.IsChecked ?? false;

#if USE_TESSERACT
                var chkAdaptiveSave = this.FindControl<CheckBox>("ChkOcrAdaptiveThreshold");
                if (chkAdaptiveSave != null && chkAdaptiveSave.IsVisible) _config.OcrAdaptiveThreshold = chkAdaptiveSave.IsChecked ?? false;
#endif

                var cboOverlayDuration = this.FindControl<ComboBox>("CboOverlayDuration");
                if (cboOverlayDuration?.SelectedItem is string overlayChoice)
                {
                    int overlayMs = overlayChoice.StartsWith("0.5") ? 500
                        : overlayChoice.StartsWith("1 ") ? 1000
                        : overlayChoice.StartsWith("2 ") ? 2000
                        : overlayChoice.StartsWith("3 ") ? 3000
                        : 5000;
                    _config.NotificationOverlayDurationMs = overlayMs;
                }

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
                _config.RotateCwHotkey = this.FindControl<TextBox>("TxtRotateCwKey")?.Text ?? _config.RotateCwHotkey;
                _config.RotateCcwHotkey = this.FindControl<TextBox>("TxtRotateCcwKey")?.Text ?? _config.RotateCcwHotkey;
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
                _config.ScrollCaptureDelimiterHotkey = this.FindControl<TextBox>("TxtScrollCaptureDelimiterKey")?.Text ?? _config.ScrollCaptureDelimiterHotkey;

                IniConfig.Save();
                
                if (oldRegion != _config.RegionHotkey || oldWindow != _config.WindowHotkey || oldFull != _config.FullscreenHotkey || oldLast != _config.LastregionHotkey || oldClip != _config.ClipboardHotkey)
                {
                    HotkeyManager.Stop();
                    HotkeyManager.Start();
                }
                
                _loadedFingerprint = BuildFingerprint();
                _savedAndClosing = true;
                OverlayHelper.ShowNotification("Settings Saved Successfully", this);
                Close();
            }
            catch (System.Exception ex)
            {
                await ConfirmDialog.ShowAlertAsync(this, "Error", $"Error saving settings: {ex.Message}", "OK", true).ConfigureAwait(true);
            }
            finally
            {
                _saveInProgress = false;
            }
        }
    }
}