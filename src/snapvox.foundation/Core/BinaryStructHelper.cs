using snapvox.native;
using snapvox.native.foundation;
using snapvox.native.graphics;
using snapvox.native.ui;
using System;
using System.Runtime.InteropServices;

namespace snapvox.foundation.core
{

    public static class BinaryStructHelper
    {

        public static T FromByteArray<T>(byte[] bytes) where T : struct
        {
            IntPtr ptr = IntPtr.Zero;
            try
            {
                int size = Marshal.SizeOf<T>();
                ptr = Marshal.AllocHGlobal(size);
                Marshal.Copy(bytes, 0, ptr, size);
                return FromIntPtr<T>(ptr);
            }
            finally
            {
                if (ptr != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(ptr);
                }
            }
        }

        public static T FromIntPtr<T>(IntPtr intPtr) where T : struct
        {
            return Marshal.PtrToStructure<T>(intPtr);
        }

        public static byte[] ToByteArray<T>(T obj) where T : struct
        {
            IntPtr ptr = IntPtr.Zero;
            try
            {
                int size = Marshal.SizeOf<T>();
                ptr = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(obj, ptr, true);
                return FromPtrToByteArray<T>(ptr);
            }
            finally
            {
                if (ptr != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(ptr);
                }
            }
        }

        public static byte[] FromPtrToByteArray<T>(IntPtr ptr) where T : struct
        {
            int size = Marshal.SizeOf<T>();
            byte[] bytes = new byte[size];
            Marshal.Copy(ptr, bytes, 0, size);
            return bytes;
        }
    }
}
