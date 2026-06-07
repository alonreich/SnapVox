# SnapVox Technical Engineering Notes

This document archives crucial engineering insights and rationales that were previously embedded as comments in the source code. These notes are essential for maintaining the stability and architectural integrity of the SnapVox v3.0 Native AOT project.

---

## 1. Visual & Graphic Engines

### Metallic Icon Hue-Shift Algorithm
**Context:** `snapvox.app` (Tray Icon Initialization)
**Note:** To preserve the premium metallic shine and transparency of the original `SnapVox.ico` while providing a "Recording" state, a mathematical hue-shift is applied to the raw `Vector4` pixel rows. 
- **Algorithm:** The Red channel is maximized based on the relative brightness of the source pixel, while Green and Blue channels are dimmed by 80%. This maintains the original luminosity and saturation profiles.
- **Reference:** `SixLabors.ImageSharp` process-pixel-rows implementation.

### High-Fidelity DIB Injection
**Context:** `snapvox.foundation.core.UiClipboard`
**Note:** Legacy Windows applications (Office, older image editors) do not accept full BMP file streams (with the 14-byte header) via the `CF_DIB` format.
- **Requirement:** The 14-byte BMP file header MUST be stripped before injection. 
- **Payload:** The resulting byte array contains only the `BITMAPINFOHEADER` and pixel data.

---

## 2. Interaction & UI Lifecycle

### Window Startup Timing
**Context:** `snapvox.forms.CaptureWindow`
**Note:** Window bounds and position MUST be set manually before the `Show()` call. Relying on default Avalonia centering or post-show positioning leads to a "Top-Left Flickering" effect and can cause the window to become stuck or invisible on high-DPI secondary monitors.

### Keyboard Focus Mandate
**Context:** `snapvox.forms.CaptureWindow`
**Note:** Explicitly call `Activate()` and `Focus()` on window opening. This is critical for ensuring that the global `ESC` key listener works immediately, especially when the window is spawned on a non-primary monitor that doesn't have active OS focus.

### Tray Menu Delay logic
**Context:** `snapvox.helpers.CaptureHelper` (CaptureActiveWindow)
**Note:** When capturing the active window via the System Tray menu, a 300ms asynchronous delay is mandatory. This allows the Win32 context menu to physically close and OS focus to return to the previously active application before the `GetForegroundWindow` handle is grabbed.

---

## 3. Localization & OCR

### Hebrew/Bidi Rebuilding logic
**Context:** `snapvox.helpers.HebrewOcrCorrectionHelper`
**Note:** The OCR engine returns words in physical reading order, which may not align with visual selection text for right-to-left (RTL) or mixed-direction strings. 
- **Current Strategy:** Rebuilds the main text block from individual word bounding boxes. 
- **Caution:** A simple character-level reversal is insufficient; a full bidi algorithm is required if sophisticated mixed-text layout is to be supported in the future.

---

## 4. Deployment & Native AOT

### COM-Free Shell Integration
**Context:** `snapvox.helpers.ShellLinkWriter`
**Note:** Native AOT has severe stability limitations with built-in .NET COM wrappers (like `IShellLinkW`).
- **Solution:** SnapVox uses PowerShell to create shell shortcuts out-of-process. This is 100% robust and prevents trimming-related crashes during installation/uninstallation.

### Mandatory DLL Persistence
**Context:** `snapvox.helpers.DeploymentLifecycle`
**Note:** All DLL dependencies MUST be copied to the `%TEMP%` runtime folder during the standalone boostrap phase. Executing from a partial extraction or relying on the OS search path leads to runtime failures (Issue 004) when the primary binary is running in a low-privilege context.

### "Scorched Earth" Rationale
**Context:** `snapvox.helpers.DeploymentLifecycle`
**Note:** To ensure zero-footprint system hygiene, the uninstaller must purge the MUICache, registry keys, and all filesystem artifacts across `%APPDATA%`, `%PROGRAMDATA%`, and `%TEMP%`. Partial cleanups are rejected.

---

## 5. Configuration & Initialization

### IniConfig Directory Priority
**Context:** `snapvox.foundation.inifile.IniConfig`
**Note:** Explicitly setting the configuration directory is priority #1. This resolves redirection issues on non-standard Windows installations and ensures that the application always finds its state.

### Keys Stub Rationale
**Context:** `snapvox.foundation.core.AvaloniaShims.Keys`
**Note:** This is a simplified stub to replace `System.Windows.Forms.Keys`. It allows the configuration enums and hotkey definitions to compile without depending on the heavy WinForms library. The main application subsequently maps these values to Avalonia `Key` bindings.
