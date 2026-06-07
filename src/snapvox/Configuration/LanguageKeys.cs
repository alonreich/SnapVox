using snapvox.native;
using snapvox.native.foundation;
using snapvox.native.graphics;
using snapvox.native.ui;
using System.Diagnostics.CodeAnalysis;

namespace snapvox.Configuration
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public enum LangKey
    {
        none,
        contextmenu_capturefullscreen_all,
        contextmenu_capturefullscreen_left,
        contextmenu_capturefullscreen_top,
        contextmenu_capturefullscreen_right,
        contextmenu_capturefullscreen_bottom,
        editor_clipboardfailed,
        editor_close_on_save,
        editor_close_on_save_title,
        editor_copytoclipboard,
        editor_cuttoclipboard,
        editor_deleteelement,
        editor_downonelevel,
        editor_downtobottom,
        editor_duplicate,
        editor_email,
        editor_imagesaved,
        editor_title,
        editor_uponelevel,
        editor_uptotop,
        editor_undo,
        editor_redo,
        editor_resetsize,
        error,
        error_multipleinstances,
        error_openfile,
        error_openlink,
        error_save,
        error_save_invalid_chars,
        print_error,
        quicksettings_destination_file,
        settings_destination,
        settings_destination_clipboard,
        settings_destination_editor,
        settings_destination_fileas,
        settings_destination_printer,
        settings_destination_picker,
        settings_filenamepattern,
        settings_message_filenamepattern,
        settings_printoptions,
        settings_tooltip_filenamepattern,
        settings_tooltip_language,
        settings_tooltip_primaryimageformat,
        settings_tooltip_storagelocation,
        settings_visualization,
        settings_window_capture_mode,
        tooltip_firststart,
        warning,
        warning_hotkeys,
        update_found
    }
}