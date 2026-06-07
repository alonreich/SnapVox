using snapvox.native;
using snapvox.native.foundation;
using snapvox.native.graphics;
using snapvox.native.ui;
using System;
using System.Runtime.InteropServices;

namespace snapvox.foundation.Interop
{

    [ComImport, Guid("2246EA2D-CAEA-4444-A3C4-6DE827E44313"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAppVisibility
    {
        MONITOR_APP_VISIBILITY GetAppVisibilityOnMonitor(IntPtr hMonitor);
        bool IsLauncherVisible { get; }
    }

    public enum MONITOR_APP_VISIBILITY
    {
        MAV_APP_VISIBLE = 2
    }
}
