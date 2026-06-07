using System;

namespace snapvox.foundation.core.AvaloniaShims
{
    public class Cursor : IDisposable
    {
        public IntPtr Handle { get; private set; }
        public void Dispose() {}
    }
}
