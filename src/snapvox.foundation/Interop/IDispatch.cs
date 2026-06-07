using snapvox.native;
using snapvox.native.foundation;
using snapvox.native.graphics;
using snapvox.native.ui;
using System;
using System.Runtime.InteropServices;

namespace snapvox.foundation.Interop
{
    [ComImport, Guid("00020400-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    public interface IDispatch : IUnknown
    {
        [PreserveSig]
        int GetTypeInfoCount(out int count);

        [PreserveSig]
        int GetTypeInfo([MarshalAs(UnmanagedType.U4)] int iTInfo, [MarshalAs(UnmanagedType.U4)] int lcid,
            [MarshalAs(UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof(TypeToTypeInfoMarshaler))]
            out Type typeInfo);

        [PreserveSig]
        int GetIDsOfNames(ref Guid riid, [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)]
            string[] rgsNames, int cNames, int lcid, [MarshalAs(UnmanagedType.LPArray)] int[] rgDispId);

        [PreserveSig]
        int Invoke(int dispIdMember, ref Guid riid, uint lcid, ushort wFlags, ref System.Runtime.InteropServices.ComTypes.DISPPARAMS pDispParams,
            out object pVarResult, ref System.Runtime.InteropServices.ComTypes.EXCEPINFO pExcepInfo, IntPtr[] pArgErr);
    }
}
