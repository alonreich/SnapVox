using snapvox.native;
using snapvox.native.foundation;
using snapvox.native.graphics;
using snapvox.native.ui;
using System;

namespace snapvox.foundation.core
{

    public static class WindowsVersion
    {

        public static Version WinVersion { get; } = Environment.OSVersion.Version;

        public static bool IsWindows10 { get; } = WinVersion.Major == 10;

        public static bool IsWindows11OrLater { get; } = WinVersion.Major == 10 && WinVersion.Build >= 22000;

        public static bool IsWindows10OrLater { get; } = WinVersion.Major >= 10;

        public static bool IsWindows81OrLater { get; } = WinVersion.Major == 6 && WinVersion.Minor >= 3 || WinVersion.Major > 6;

        public static bool IsWindows8OrLater { get; } = WinVersion.Major == 6 && WinVersion.Minor >= 2 || WinVersion.Major > 6;

        public static bool IsWindowsVistaOrLater { get; } = WinVersion.Major >= 6;

        public static int BuildVersion => WinVersion.Build;

        public static bool IsWindows10BuildOrLater(int minimalBuildNumber)
        {
            return IsWindows10 && WinVersion.Build >= minimalBuildNumber;
        }
    }
}
