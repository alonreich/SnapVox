global using Color = SixLabors.ImageSharp.Color;
global using Image = SixLabors.ImageSharp.Image;
global using Bitmap = SixLabors.ImageSharp.Image;
global using Graphics = SixLabors.ImageSharp.Processing.IImageProcessingContext;

#pragma warning disable CS0108, CS0114, CS0067, CS0162
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace snapvox.foundation.core {
    public class CapturedCursor : IDisposable { 
        public snapvox.foundation.core.AvaloniaShims.Point HotSpot { get; set; } 
        public snapvox.foundation.core.AvaloniaShims.Size Size { get; set; }
        public Image ColorLayer { get; set; } 
        public Image MaskLayer { get; set; }
        public void Dispose() { } 
    }
    public enum WindowStyleFlags : long { }
    public enum ExtendedWindowStyleFlags : long { WS_EX_TOOLWINDOW = 0x80 }
    public class WindowPlacement { 
        public static WindowPlacement Create() => new WindowPlacement(); 
        public snapvox.foundation.core.AvaloniaShims.Rectangle NormalPosition { get; set; } 
        public snapvox.foundation.core.AvaloniaShims.Point MaxPosition { get; set; } 
        public snapvox.foundation.core.AvaloniaShims.Point MinPosition { get; set; } 
        public ShowWindowCommands ShowCmd { get; set; } 
        public WindowPlacementFlags Flags { get; set; } 
        public static implicit operator snapvox.native.ui.WINDOWPLACEMENT(WindowPlacement p) => default;
        public static implicit operator WindowPlacement(snapvox.native.ui.WINDOWPLACEMENT p) => new WindowPlacement();
    }
    public enum WindowPlacementFlags { }
    public enum ShowWindowCommands { Normal = 1, Restore = 9, Maximize = 3 }
    public enum WindowsMessages : uint { 
        WM_NULL = 0, 
        WM_INPUTLANGCHANGEREQUEST = 0x0050, 
        WM_INPUTLANGCHANGE = 0x0051,
        WM_QUERYENDSESSION = 0x0011,
        WM_ENDSESSION = 0x0016,
        WM_GETICON = 0x007F,
        WM_SYSCOMMAND = 0x0112
    }
    public enum SysCommands : uint { 
        SC_MINIMIZE = 0xF020,
        SC_RESTORE = 0xF120,
        SC_MAXIMIZE = 0xF030
    }
    public class DisplayInfo { 
        public snapvox.foundation.core.AvaloniaShims.Rectangle Bounds { get; set; } 
        public IntPtr MonitorHandle { get; set; }
        public static snapvox.foundation.core.AvaloniaShims.Rectangle ScreenBounds => new snapvox.foundation.core.AvaloniaShims.Rectangle();
        public static List<DisplayInfo> AllDisplayInfos => new List<DisplayInfo>();
        public snapvox.foundation.core.AvaloniaShims.Rectangle WorkingArea { get; set; } 
    }
    public static class snapvoxExtensions { 
        public static bool AreRectangleCornersVisisble(this object r, snapvox.foundation.core.AvaloniaShims.Rectangle rect) => true; 
        public static bool Succeeded(this int r) => r >= 0;
        public static Image Rotate(this Image i, float a) => i;
    }
    public static class HResult { 
        public static bool Succeeded(int r) => r >= 0; 
        public static readonly int S_FALSE = 1;
    }
    public static class CursorHelper { 
        public static bool TryGetCurrentCursor(out CapturedCursor c) { c = new CapturedCursor(); return true; } 
        public static void DrawCursorOnGraphics(Graphics g, CapturedCursor c, snapvox.foundation.core.AvaloniaShims.Point p) { }
        public static void DrawCursorOnGraphics(Graphics g, CapturedCursor c, snapvox.foundation.core.AvaloniaShims.Point p, snapvox.foundation.core.AvaloniaShims.Size s) { }
        public static void DrawCursorOnBitmap(Image b, CapturedCursor c, snapvox.foundation.core.AvaloniaShims.Point p) { }
        public static void DrawCursorOnBitmap(Image b, CapturedCursor c, snapvox.foundation.core.AvaloniaShims.Point p, snapvox.foundation.core.AvaloniaShims.Size s) { }
    }
    public static class NativeDpiMethods { 
        public static int GetDpi(IntPtr h) => 96; 
        public static int GetDpi(snapvox.foundation.core.AvaloniaShims.Point p) => 96;
    }
    public static class DpiCalculator { 
        public static int Scale(int v, double d) => v; 
        public static int Scale(int v, float f) => v;
        public static int Scale(int v, snapvox.foundation.core.AvaloniaShims.Size s) => v;
        public static int Scale(snapvox.foundation.core.AvaloniaShims.Size s, int v) => v;
        public static float Scale(float v, snapvox.foundation.core.AvaloniaShims.Size s) => v;
        public static snapvox.foundation.core.AvaloniaShims.Size Scale(snapvox.foundation.core.AvaloniaShims.Size s, float f) => s;
        public static snapvox.foundation.core.AvaloniaShims.Size Scale(snapvox.foundation.core.AvaloniaShims.Size s, snapvox.foundation.core.AvaloniaShims.Size v) => s;
        public static snapvox.foundation.core.AvaloniaShims.Size Scale(snapvox.foundation.core.AvaloniaShims.Size s, double d) => s;
        public static snapvox.foundation.core.AvaloniaShims.Size ScaleWithDpi(snapvox.foundation.core.AvaloniaShims.Size v, int d) => v; 
        public static int ScaleWithDpiInt(int v, int d) => v;
    }
    public static class GraphicsExtensions { 
        public static snapvox.foundation.Interop.SafeGraphicsDcHandle GetSafeDeviceContext(this Graphics g) => new snapvox.foundation.Interop.SafeGraphicsDcHandle(); 
    }
    public class RichTextOptions { public RichTextOptions(SixLabors.Fonts.Font f) { } public HorizontalAlignment HorizontalAlignment { get; set; } public VerticalAlignment VerticalAlignment { get; set; } public snapvox.foundation.core.AvaloniaShims.PointF Origin { get; set; } }
    
    public class SaveImageFileDialog : IDisposable {
        public string FileName { get; set; }
        public string Title { get; set; }
        public string Filter { get; set; }
        public string FileNameWithExtension => FileName;
        public SaveImageFileDialog() { }
        public SaveImageFileDialog(object o) { }
        public snapvox.foundation.core.AvaloniaShims.DialogResult ShowDialog() => snapvox.foundation.core.AvaloniaShims.DialogResult.OK;
        public void Dispose() { }
    }
    public class QualityDialog {
        public int Quality { get; set; }
        public QualityDialog() { }
        public QualityDialog(object o) { }
        public snapvox.foundation.core.AvaloniaShims.DialogResult ShowDialog() => snapvox.foundation.core.AvaloniaShims.DialogResult.OK;
    }
    public enum HorizontalAlignment { Left, Center, Right }
    public enum VerticalAlignment { Top, Center, Bottom }
}

namespace snapvox.foundation.Controls { public class Stub { } }

namespace snapvox.foundation.Interop {
    public class TypeToTypeInfoMarshaler { }
    public class OsVersionInfoEx { 
        public static OsVersionInfoEx Create() => new OsVersionInfoEx(); 
        public byte wProductType;
        public ushort wSuiteMask;
        public ushort wServicePackMajor;
        public ushort wServicePackMinor;
        public string ServicePackVersion;
    }
    public class Win32 { 
        public static string GetMessage(uint c) => ""; 
        public static uint GetLastErrorCode() => (uint)Marshal.GetLastWin32Error();
    }
    public class ClipboardNative { 
        public static bool OpenClipboard(IntPtr h) => true; 
        public static bool CloseClipboard() => true; 
        public static bool EmptyClipboard() => true; 
        public static IntPtr SetClipboardData(uint f, IntPtr h) => h; 
        public static IntPtr CurrentOwner => IntPtr.Zero;
    }
    public class IconHelper { 
        public static Image GetIconImage(IntPtr h) => null; 
        public static T ExtractAssociatedIcon<T>(string p, int i, bool l) where T : class {
            return null;
        }
    }
    public class SafeRegionHandle : IDisposable { public SafeRegionHandle() { } public SafeRegionHandle(IntPtr h, bool r) { } public void Dispose() { } public IntPtr DangerousGetHandle() => IntPtr.Zero; public bool IsInvalid => false; }
    public class SafeGraphicsDcHandle : IDisposable { public void Dispose() { } public IntPtr DangerousGetHandle() => IntPtr.Zero; }
    public class SafeWindowDcHandle : IDisposable { 
        private IntPtr _hdc;
        public SafeWindowDcHandle() {
            _hdc = IntPtr.Zero;
        }
        public void Dispose() { 
        } 
        public IntPtr DangerousGetHandle() => _hdc; 
        public bool IsInvalid => _hdc == IntPtr.Zero; 
        public static SafeWindowDcHandle FromDesktop() => new SafeWindowDcHandle(); 
    }
    public struct BitfieldColorMask { }
    public class GdiPlusApi {
        public static void GdipSetImageAttributesCachedBlur(IntPtr a, int r, bool b) { }
        public static bool IsBlurPossible() => false;
        public static bool IsBlurPossible(object o) => false;
        public static void DrawWithBlur(object a, object b, object c, int d, int e, int f, int g) { }
    }
}

namespace snapvox.editor.forms {
    public class BackgroundForm : snapvox.foundation.core.AvaloniaShims.Form {
        public BackgroundForm() { }
        public BackgroundForm(object a, object b) { }
        public void CloseDialog() { }
    }
}
namespace snapvox.editor.Controls.Emoji { 
    public class EmojiPicker : snapvox.foundation.core.AvaloniaShims.Control { 
        public string Selection { get; set; }
        public void ShowPopup(snapvox.foundation.core.AvaloniaShims.Point p) { }
        public void ShowPopup(bool b) { }
        public event EventHandler<EmojiPickedEventArgs> Picked;
    } 
    public class EmojiPickedEventArgs : EventArgs { public string Emoji { get; set; } }
}

namespace SixLabors {
    namespace Fonts { 
        public class FontCollection { public void Add(string p) { } public void Add(Stream s) { } public bool TryGet(string n, out FontFamily f) { f = default; return false; } } 
        public struct FontFamily { public Font CreateFont(float s, object st) => new Font(); } 
        public enum FontStyle { Regular } 
        public class Font { public float Size { get; set; } }
    }
    namespace ImageSharp { 
        namespace Drawing { namespace Processing { public class Stub { } public interface IImageProcessingContext { void DrawText(object o, object f, object b, snapvox.foundation.core.AvaloniaShims.Point p); void DrawText(snapvox.foundation.core.RichTextOptions o, string t, SixLabors.ImageSharp.Color c); } } } 
    }
}

namespace snapvox.foundation.core.AvaloniaShims {
    public struct PointF { public float X; public float Y; public PointF(float x, float y) { X = x; Y = y; } }
    public struct SizeF { public float Width; public float Height; public SizeF(float w, float h) { Width = w; Height = h; } }
    public struct RectangleF { public float X; public float Y; public float Width; public float Height; public RectangleF(float x, float y, float w, float h) { X = x; Y = y; Width = w; Height = h; } }
    public class Icon : IDisposable { public void Dispose() {} }
    public class Font : IDisposable { public void Dispose() {} }
    public class Region : IDisposable { public Region() {} public Region(object o) {} public void Dispose() {} public bool IsEmpty(object o) => true; public void Union(object o) {} }
    public class Matrix : IDisposable { public void Dispose() {} public void Translate(float x, float y, object o) {} }
    public enum ImageLockMode { ReadOnly, WriteOnly, ReadWrite }
    public class BitmapData { public int Width; public int Height; public int Stride; public IntPtr Scan0; }
    public class PropertyItem { public int Id; public int Type; public int Len; public byte[] Value; }
    public class FontFamily { public FontFamily(string n) {} }
    public enum PixelFormat { Format32bppArgb, Format24bppRgb, Format8bppIndexed, DontCare, Format32bppPArgb, Format32bppRgb }
    public enum MatrixOrder { Append }
    public enum RotateFlipType { RotateNoneFlipNone }
}
