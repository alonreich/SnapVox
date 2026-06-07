using System;

namespace snapvox.foundation.core.AvaloniaShims
{
    public interface IWin32Window
    {
        IntPtr Handle { get; }
    }
}
