using System;
using snapvox.foundation.core.AvaloniaShims;

namespace snapvox.foundation.core.AvaloniaShims
{
    public class MouseEventArgs : EventArgs
    {
        public int Clicks { get; private set; }
        public int Delta { get; private set; }
        public Point Location { get; private set; }
        public int X { get; private set; }
        public int Y { get; private set; }
        
        public int Button { get; private set; } 

        public MouseEventArgs(int button, int clicks, int x, int y, int delta)
        {
            Button = button;
            Clicks = clicks;
            X = x;
            Y = y;
            Location = new Point(x, y);
            Delta = delta;
        }
    }
}
