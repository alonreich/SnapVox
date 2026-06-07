using System;
using snapvox.foundation.core.AvaloniaShims;

namespace snapvox.foundation.core.AvaloniaShims
{
    public class CreateParams { }

    public class Control : IWin32Window
    {
        public IntPtr Handle { get; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool Visible { get; set; }
        public Point Location { get; set; }
        public Size Size { get; set; }
        public Rectangle Bounds { get; set; }
        
        public void SuspendLayout() { }
        public void ResumeLayout(bool performLayout) { }
        public void Dispose() { }
    }

    public class Form : Control
    {
        public Form() { }
        public bool ShowInTaskbar { get; set; }
        public double Opacity { get; set; }
        public bool TopMost { get; set; }
        public Color BackColor { get; set; }
        public Color TransparencyKey { get; set; }
        public int FormBorderStyle { get; set; }
        public int StartPosition { get; set; }
        public void Show() { }
        public void Close() { }
        public void Hide() { }
    }

    public class Label : Control
    {
        public string Text { get; set; }
        public Color ForeColor { get; set; }
        public Color BackColor { get; set; }
        public Font Font { get; set; }
        public ContentAlignment TextAlign { get; set; }
        public bool AutoSize { get; set; }
    }

    public enum FormBorderStyle { None }
    public enum FormStartPosition { Manual }
    public enum ContentAlignment { MiddleCenter }
}
