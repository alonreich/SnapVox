using System;
using System.Runtime.InteropServices;

namespace snapvox.foundation.Interop
{
    public static class ComVtableHelper
    {
        public unsafe static IntPtr GetVtableMethod(IntPtr unknownPtr, int index)
        {
            if (unknownPtr == IntPtr.Zero) return IntPtr.Zero;
            IntPtr* vtable = *(IntPtr**)unknownPtr;
            return vtable[index];
        }

        public unsafe static int CallIsLauncherVisible(IntPtr unknownPtr, out bool pfVisible)
        {
            pfVisible = false;
            IntPtr method = GetVtableMethod(unknownPtr, 4);
            if (method == IntPtr.Zero) return -1;
            
            delegate* unmanaged[Stdcall]<IntPtr, int*, int> func = (delegate* unmanaged[Stdcall]<IntPtr, int*, int>)method;
            int visible = 0;
            int hr = func(unknownPtr, &visible);
            pfVisible = visible != 0;
            return hr;
        }

        public unsafe static int CreateForWindow(IntPtr factoryPtr, IntPtr hWnd, ref Guid iid, out IntPtr result)
        {
            result = IntPtr.Zero;
            IntPtr method = GetVtableMethod(factoryPtr, 3);
            if (method == IntPtr.Zero) return -1;

            fixed (Guid* pIid = &iid)
            {
                fixed (IntPtr* pResult = &result)
                {
                    delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int> func = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int>)method;
                    return func(factoryPtr, hWnd, pIid, pResult);
                }
            }
        }

        public unsafe static int CreateForMonitor(IntPtr factoryPtr, IntPtr hMonitor, ref Guid iid, out IntPtr result)
        {
            result = IntPtr.Zero;
            IntPtr method = GetVtableMethod(factoryPtr, 4);
            if (method == IntPtr.Zero) return -1;

            fixed (Guid* pIid = &iid)
            {
                fixed (IntPtr* pResult = &result)
                {
                    delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int> func = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int>)method;
                    return func(factoryPtr, hMonitor, pIid, pResult);
                }
            }
        }

        public unsafe static int D3D11GetDeviceRemovedReason(IntPtr devicePtr)
        {
            IntPtr method = GetVtableMethod(devicePtr, 40);
            if (method == IntPtr.Zero) return -1;
            delegate* unmanaged[Stdcall]<IntPtr, int> func = (delegate* unmanaged[Stdcall]<IntPtr, int>)method;
            return func(devicePtr);
        }

        public unsafe static int D3D11CreateTexture2D(IntPtr devicePtr, IntPtr pDesc, IntPtr pInitialData, out IntPtr ppTexture2D)
        {
            ppTexture2D = IntPtr.Zero;
            IntPtr method = GetVtableMethod(devicePtr, 5);
            if (method == IntPtr.Zero) return -1;
            fixed (IntPtr* pTex = &ppTexture2D)
            {
                delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, IntPtr*, int> func = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, IntPtr*, int>)method;
                return func(devicePtr, pDesc, pInitialData, pTex);
            }
        }

        public unsafe static void D3D11GetTexture2DDesc(IntPtr texturePtr, IntPtr pDesc)
        {
            IntPtr method = GetVtableMethod(texturePtr, 10);
            if (method == IntPtr.Zero) return;
            delegate* unmanaged[Stdcall]<IntPtr, IntPtr, void> func = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, void>)method;
            func(texturePtr, pDesc);
        }

        public unsafe static void D3D11CopyResource(IntPtr contextPtr, IntPtr pDst, IntPtr pSrc)
        {
            IntPtr method = GetVtableMethod(contextPtr, 47);
            if (method == IntPtr.Zero) return;
            delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, void> func = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, void>)method;
            func(contextPtr, pDst, pSrc);
        }

        public unsafe static int D3D11Map(IntPtr contextPtr, IntPtr pResource, uint subresource, int mapType, uint mapFlags, IntPtr pMappedResource)
        {
            IntPtr method = GetVtableMethod(contextPtr, 14);
            if (method == IntPtr.Zero) return -1;
            delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, int, uint, IntPtr, int> func = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, int, uint, IntPtr, int>)method;
            return func(contextPtr, pResource, subresource, mapType, mapFlags, pMappedResource);
        }

        public unsafe static void D3D11Unmap(IntPtr contextPtr, IntPtr pResource, uint subresource)
        {
            IntPtr method = GetVtableMethod(contextPtr, 15);
            if (method == IntPtr.Zero) return;
            delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, void> func = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, void>)method;
            func(contextPtr, pResource, subresource);
        }
    }
}
