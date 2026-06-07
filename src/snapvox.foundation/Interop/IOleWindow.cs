using snapvox.native;
using snapvox.native.foundation;
using snapvox.native.graphics;
using snapvox.native.ui;
using System;
using System.Runtime.InteropServices;

namespace snapvox.foundation.Interop
{

    [ComImport, Guid("00000114-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IOleWindow
    {
        void GetWindow(out IntPtr phWnd);
        void ContextSensitiveHelp([In, MarshalAs(UnmanagedType.Bool)] bool fEnterMode);
    }
}
