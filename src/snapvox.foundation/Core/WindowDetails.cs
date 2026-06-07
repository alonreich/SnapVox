using System;
using SixLabors.ImageSharp;
using snapvox.foundation.Interfaces;
using snapvox.foundation.core.AvaloniaShims;

namespace snapvox.foundation.core
{
    public class WindowDetails
    {
        public string Text { get; set; }
        public IntPtr Handle { get; set; }
        public static Image CaptureWindow(IntPtr hWnd) => null;
        public static void ToForeground() { }
    }
}