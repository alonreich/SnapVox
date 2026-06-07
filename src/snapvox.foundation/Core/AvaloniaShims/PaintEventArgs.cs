using System;
using snapvox.foundation.core.AvaloniaShims;

namespace snapvox.foundation.core.AvaloniaShims
{
    public class PaintEventArgs : EventArgs
    {
        public Graphics Graphics { get; private set; }
        public Rectangle ClipRectangle { get; private set; }

        public PaintEventArgs(Graphics graphics, Rectangle clipRect)
        {
            Graphics = graphics;
            ClipRectangle = clipRect;
        }
    }
}
