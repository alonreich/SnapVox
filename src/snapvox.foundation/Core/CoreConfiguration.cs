/*
 * Portions of this file, specifically the configuration schema and 
 * INI property bindings, were adapted from the Greenshot project, 
 * which is licensed under the GNU General Public License (GPL).
 * SnapVox acknowledges and complies with this license.
 */
using snapvox.native;
using snapvox.native.foundation;
using snapvox.native.graphics;
using snapvox.native.ui;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using snapvox.foundation.core.AvaloniaShims;
using System.IO;
using System.Reflection;
using snapvox.foundation.core.Enums;
using snapvox.foundation.IniFile;
using snapvox.foundation.Interfaces;
using System.Diagnostics.CodeAnalysis;

namespace snapvox.foundation.core
{
    [IniSection("Core", Description = "snapvox core configuration")]
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public class CoreConfiguration : IniSection, INotifyPropertyChanged
    {
#pragma warning disable CS0067
        public event PropertyChangedEventHandler PropertyChanged;
#pragma warning restore CS0067

        [IniProperty("Language", Description = "The language in IETF format (e.g. en-US)")]
        public string Language { get; set; }

        [IniProperty("RegionHotkey", Description = "Hotkey for starting the region capture", DefaultValue = "PrintScreen")]
        public string RegionHotkey { get; set; } = "PrintScreen";

        [IniProperty("WindowHotkey", Description = "Hotkey for starting the window capture", DefaultValue = "Alt + PrintScreen")]
        public string WindowHotkey { get; set; } = "Alt + PrintScreen";

        [IniProperty("FullscreenHotkey", Description = "Hotkey for starting the fullscreen capture", DefaultValue = "Ctrl + PrintScreen")]
        public string FullscreenHotkey { get; set; } = "Ctrl + PrintScreen";
        
        [IniProperty("LastregionHotkey", Description = "Hotkey for starting the last region capture", DefaultValue = "")]
        public string LastregionHotkey { get; set; } = "";

        [IniProperty("ClipboardHotkey", Description = "Hotkey for starting capture from clipboard", DefaultValue = "")]
        public string ClipboardHotkey { get; set; } = "";

        [IniProperty("TextToolThickness", Description = "Default thickness for Text tool", DefaultValue = "2.0")]
        public double TextToolThickness { get; set; } = 2.0;

        [IniProperty("IsFirstLaunch", Description = "Is this the first time launch?", DefaultValue = "true")]
        public bool IsFirstLaunch { get; set; } = true;

        [IniProperty("IsBetaTester", Description = "Is the user a beta tester?", DefaultValue = "false")]
        public bool IsBetaTester { get; set; } = false;

        [IniProperty("ClipboardFormats", Separator = ",", Description = "Specify which formats we copy on the clipboard? Options are: PNG, HTML, HTMLDATAURL and DIB", DefaultValue = "PNG,DIB")]
        public List<ClipboardFormat> ClipboardFormats { get; set; } = new List<ClipboardFormat>();

        [IniProperty("CaptureMousepointer", Description = "Should the mouse be captured?", DefaultValue = "false")]
        public bool CaptureMousepointer { get; set; } = false;

        [IniProperty("CaptureWindowsInteractive", Description = "Use interactive window selection to capture? (false=Capture active window)", DefaultValue = "true")]
        public bool CaptureWindowsInteractive { get; set; } = true;

        [IniProperty("CaptureDelay", Description = "Capture delay in milliseconds.", DefaultValue = "0")]
        public int CaptureDelay { get; set; } = 0;

        [IniProperty("ScreenCaptureMode", Description = "The capture mode used to capture a screen. (Auto, FullScreen, Fixed)", DefaultValue = "Auto")]
        public ScreenCaptureMode ScreenCaptureMode { get; set; }

        [IniProperty("ScreenToCapture", Description = "The screen number to capture when using ScreenCaptureMode Fixed.", DefaultValue = "1")]
        public int ScreenToCapture { get; set; } = 1;

        [IniProperty("WindowCaptureMode", Description = "The capture mode used to capture a Window (Screen, GDI, Aero, AeroTransparent, Auto).", DefaultValue = "Auto")]
        public WindowCaptureMode WindowCaptureMode { get; set; }

        [IniProperty("DWMBackgroundColor", Description = "The background color for a DWM window capture.")]
        public Color DWMBackgroundColor { get; set; } = Color.Transparent;

        [IniProperty("ShowTrayNotification", LanguageKey = "settings_shownotify", Description = "Show a notification from the systray when a capture is taken.", DefaultValue = "true")]
        public bool ShowTrayNotification { get; set; } = true;

        [IniProperty("RunAsAdministratorOnStartup", Description = "Run snapvox elevated when it starts automatically.", DefaultValue = "false")]
        public bool RunAsAdministratorOnStartup { get; set; } = false;

        [IniProperty("OutputFilePath", Description = "Output file path.")]
        public string OutputFilePath { get; set; } = Path.Combine(Path.GetTempPath(), "snapvox");

        [IniProperty("OutputFileFilenamePattern", Description = "Filename pattern for screenshot.", DefaultValue = "${capturetime:d\"yyyy-MM-dd HH_mm_ss\"}-${title}")]
        public string OutputFileFilenamePattern { get; set; }

        [IniProperty("OutputFileFormat", Description = "Default file type for writing screenshots. (Always jpg in v3.0)", DefaultValue = "jpg")]
        public OutputFormat OutputFileFormat { get; set; } = OutputFormat.jpg;

        [IniProperty("OutputFileJpegQuality", Description = "JPEG file save quality in %.", DefaultValue = "100")]
        public int OutputFileJpegQuality { get; set; } = 100;

        [IniProperty("LogFile", Description = "The path to the log file.")]
        public string LogFile { get; set; }

        [IniProperty("LogLevel", Description = "The log level (OFF, FATAL, ERROR, WARN, INFO, DEBUG, ALL).", DefaultValue = "INFO")]
        public string LogLevel { get; set; } = "INFO";

        [IniProperty("DisableHotkeys", Description = "Disable hotkeys.", DefaultValue = "False")]
        public bool DisableHotkeys { get; set; } = false;

        [IniProperty("AutoZoomLargeSnippets", Description = "Automatically zoom out on snippets larger than the monitor working area.", DefaultValue = "true")]
        public bool AutoZoomLargeSnippets { get; set; } = true;

        [IniProperty("EditorGlobalLocalMode", Description = "Global/Local mode for tool settings. True=Global, False=Local/Isolated.", DefaultValue = "true")]
        public bool EditorGlobalLocalMode { get; set; } = true;

        [IniProperty("UseTextFrame", Description = "Show a rectangular frame around text annotations.", DefaultValue = "true")]
        public bool UseTextFrame { get; set; } = true;

        [IniProperty("KeepBackup", Description = "Keep 24 Hours Backup of Snips/Text.", DefaultValue = "true")]
        public bool KeepBackup { get; set; } = true;

        [IniProperty("CloseEditorOnAction", Description = "Close Image Editor on Copy/Download/OCR.", DefaultValue = "true")]
        public bool CloseEditorOnAction { get; set; } = true;

        [IniProperty("UserDownloadPath", Description = "Custom download path if default Downloads folder is missing.")]
        public string UserDownloadPath { get; set; }

        [IniProperty("LastPixelateStrength", DefaultValue = "25")]
        public int LastPixelateStrength { get; set; } = 25;

        [IniProperty("OcrEngine", Description = "English Language Default OCR Engine", DefaultValue = "Tesseract (External OCR Engine)")]
        public string OcrEngine { get; set; } = "Tesseract (External OCR Engine)";

        [IniProperty("LeavePictureAsIsDuringOcr", Description = "Should the picture remain as is during OCR?", DefaultValue = "false")]
        public bool LeavePictureAsIsDuringOcr { get; set; } = false;

        [IniProperty("AddFrameBorders", Description = "Add Frame Borders to Snips.", DefaultValue = "true")]
        public bool AddFrameBorders { get; set; } = true;

        [IniProperty("WarnBeforeClosingEditor", Description = "Warn before closing the image editor with unsaved changes.", DefaultValue = "false")]
        public bool WarnBeforeClosingEditor { get; set; } = false;

        [IniProperty("ArrowHotkey", DefaultValue = "A")]
        public string ArrowHotkey { get; set; } = "A";

        [IniProperty("LineHotkey", DefaultValue = "L")]
        public string LineHotkey { get; set; } = "L";

        [IniProperty("TextHotkey", DefaultValue = "T")]
        public string TextHotkey { get; set; } = "T";

        [IniProperty("ResizeHotkey", DefaultValue = "R")]
        public string ResizeHotkey { get; set; } = "R";

        [IniProperty("FreehandHotkey", DefaultValue = "D")]
        public string FreehandHotkey { get; set; } = "D";

        [IniProperty("EmojiHotkey", DefaultValue = "E")]
        public string EmojiHotkey { get; set; } = "E";

        [IniProperty("CounterHotkey", DefaultValue = "I")]
        public string CounterHotkey { get; set; } = "I";

        [IniProperty("HighlightHotkey", DefaultValue = "H")]
        public string HighlightHotkey { get; set; } = "H";

        [IniProperty("PixelateHotkey1", DefaultValue = "O")]
        public string PixelateHotkey1 { get; set; } = "O";

        [IniProperty("PixelateHotkey2", DefaultValue = "P")]
        public string PixelateHotkey2 { get; set; } = "P";

        [IniProperty("CropHotkey", DefaultValue = "C")]
        public string CropHotkey { get; set; } = "C";

        [IniProperty("CopyHotkey", DefaultValue = "C")]
        public string CopyHotkey { get; set; } = "C";

        [IniProperty("DownloadHotkey", DefaultValue = "D")]
        public string DownloadHotkey { get; set; } = "D";

        [IniProperty("RotateCcwHotkey", DefaultValue = "Left")]
        public string RotateCcwHotkey { get; set; } = "Left";

        [IniProperty("RotateCwHotkey", DefaultValue = "Right")]
        public string RotateCwHotkey { get; set; } = "Right";

        [IniProperty("DuplicateObjectHotkey", DefaultValue = "Ctrl + D")]
        public string DuplicateObjectHotkey { get; set; } = "Ctrl + D";

        [IniProperty("DeleteObjectHotkey", DefaultValue = "Delete")]
        public string DeleteObjectHotkey { get; set; } = "Delete";

        [IniProperty("ScrollCaptureDelimiterHotkey", DefaultValue = "Space")]
        public string ScrollCaptureDelimiterHotkey { get; set; } = "Space";

        [IniProperty("CustomColors", Description = "A comma separated list of custom colors.", DefaultValue = "#000000,#00FF00,#FF0000,#FFFF00,#0000FF,#FFFFFF")]
        public string CustomColors { get; set; } = "#000000,#00FF00,#FF0000,#FFFF00,#0000FF,#FFFFFF";

        [IniProperty("LastTextSize", DefaultValue = "20")]
        public double LastTextSize { get; set; } = 20;

        [IniProperty("LastEmojiSize", DefaultValue = "60")]
        public double LastEmojiSize { get; set; } = 60.0;

        [IniProperty("LastCounterSize", DefaultValue = "42")]
        public double LastCounterSize { get; set; } = 42.0;

        [IniProperty("LastCounterColor", DefaultValue = "#FF0000")]
        public string LastCounterColor { get; set; } = "#FF0000";

        [IniProperty("LastToolColors", DefaultValue = "")]
        public string LastToolColors { get; set; } = "";

        [IniProperty("LastToolThicknesses", DefaultValue = "")]
        public string LastToolThicknesses { get; set; } = "";

        public override object GetDefault(string property) => property switch
        {
            nameof(OutputFilePath) => Path.Combine(Path.GetTempPath(), "snapvox"),
            _ => null
        };

        public override void AfterLoad()
        {
            if (string.IsNullOrWhiteSpace(OutputFileFilenamePattern)) OutputFileFilenamePattern = "${capturetime:d\"yyyy-MM-dd HH_mm_ss\"}-${title}";
            if (ClipboardFormats == null || ClipboardFormats.Count == 0) ClipboardFormats = new List<ClipboardFormat> { ClipboardFormat.DIB };
            OutputFileFormat = OutputFormat.jpg;
            OutputFileJpegQuality = 100;
            OutputFilePath = Path.Combine(Path.GetTempPath(), "snapvox");
        }
    }
}
