using snapvox.native;
using snapvox.native.foundation;
using snapvox.native.graphics;
using snapvox.native.ui;
using System;
using System.Runtime.InteropServices;

namespace snapvox.foundation.Interop
{
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), ComVisible(true), Guid("B722BCCB-4E68-101B-A2BC-00AA00404770")]
    public interface IOleCommandTarget
    {
        [return: MarshalAs(UnmanagedType.I4)]
        [PreserveSig]
        int QueryStatus([In, MarshalAs(UnmanagedType.LPStruct)]
            Guid pguidCmdGroup, int cCmds, IntPtr prgCmds, IntPtr pCmdText);

        [return: MarshalAs(UnmanagedType.I4)]
        [PreserveSig]
        int Exec([In, MarshalAs(UnmanagedType.LPStruct)]
            Guid pguidCmdGroup, int nCmdID, int nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut);
    }
}
