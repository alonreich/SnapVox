using snapvox.native;
using snapvox.native.foundation;
using snapvox.native.graphics;
using snapvox.native.ui;
using System;
using System.Runtime.InteropServices;

namespace snapvox.foundation.Interop
{
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("00000000-0000-0000-C000-000000000046")]
    public interface IUnknown
    {
        IntPtr QueryInterface(ref Guid riid);

        [PreserveSig]
        uint AddRef();

        [PreserveSig]
        uint Release();
    }
}
