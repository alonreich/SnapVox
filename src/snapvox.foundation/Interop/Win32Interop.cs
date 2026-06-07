using snapvox.native;
using snapvox.native.foundation;
using snapvox.native.graphics;
using snapvox.native.ui;
using System;
using System.Runtime.InteropServices;
using snapvox.foundation.core.AvaloniaShims;
using System.Text;
using snapvox.foundation.core;

namespace snapvox.foundation.Interop
{
    public delegate bool EnumWindowsProc(HWND hWnd, LPARAM lParam);

    public static class User32Api
    {
        public static HWND GetForegroundWindow() => PInvoke.GetForegroundWindow();
        public static uint GetWindowThreadProcessId(HWND hWnd, out uint processId) => PInvoke.GetWindowThreadProcessId(hWnd, out processId);
        public static IntPtr SendMessage(HWND hWnd, uint msg, WPARAM wParam, LPARAM lParam) => PInvoke.SendMessage(hWnd, msg, wParam, lParam);
        public static bool SetForegroundWindow(HWND hWnd) => PInvoke.SetForegroundWindow(hWnd);
        public static HWND GetParent(HWND hWnd) => PInvoke.GetParent(hWnd);
        public static bool IsWindowVisible(HWND hWnd) => PInvoke.IsWindowVisible(hWnd);
        public static HWND GetDesktopWindow() => PInvoke.GetDesktopWindow();
        public static bool GetCursorPos(out POINT lpPoint) => PInvoke.GetCursorPos(out lpPoint);
        public static POINT GetCursorLocation() { GetCursorPos(out var p); return p; }
        public static HWND SetCapture(HWND hWnd) => PInvoke.SetCapture(hWnd);
        public static bool ReleaseCapture() => PInvoke.ReleaseCapture();
        public static bool BringWindowToTop(HWND hWnd) => PInvoke.BringWindowToTop(hWnd);
        public static bool ShowWindowAsync(HWND hWnd, SHOW_WINDOW_CMD nCmdShow) => PInvoke.ShowWindowAsync(hWnd, (int)nCmdShow);
        public static bool DestroyIcon(HICON hIcon) => PInvoke.DestroyIcon(hIcon);
        public static bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach) => PInvoke.AttachThreadInput(idAttach, idAttachTo, fAttach);
        public static bool PrintWindow(HWND hWnd, HDC hdcBlt, uint nFlags) => PInvoke.PrintWindow(hWnd, hdcBlt, nFlags);
        public static string GetClassname(HWND hWnd) { var sb = new StringBuilder(256); PInvoke.GetClassName(hWnd, sb, sb.Capacity); return sb.ToString(); }
        public static string GetText(HWND hWnd) { int length = PInvoke.GetWindowTextLength(hWnd); if (length == 0) return string.Empty; var sb = new StringBuilder(length + 1); PInvoke.GetWindowText(hWnd, sb, sb.Capacity); return sb.ToString(); }
        public static bool IsIconic(HWND hWnd) => PInvoke.IsIconic(hWnd);
        public static bool IsZoomed(HWND hWnd) => PInvoke.IsZoomed(hWnd);
        public static bool GetWindowPlacement(HWND hWnd, ref WINDOWPLACEMENT lpwndpl) => PInvoke.GetWindowPlacement(hWnd, ref lpwndpl);
        public static bool SetWindowPlacement(HWND hWnd, ref WINDOWPLACEMENT lpwndpl) => PInvoke.SetWindowPlacement(hWnd, ref lpwndpl);
        public static bool GetWindowInfo(HWND hWnd, ref WINDOWINFO pwi) => PInvoke.GetWindowInfo(hWnd, ref pwi);
        public static uint GetGuiResources(HANDLE hProcess, uint uiFlags) => PInvoke.GetGuiResources(hProcess, uiFlags);
        public static uint GetGuiResourcesGdiCount() => GetGuiResources(PInvoke.GetCurrentProcess(), 0);
        public static uint GetGuiResourcesUserCount() => GetGuiResources(PInvoke.GetCurrentProcess(), 1);
        public static Exception CreateWin32Exception(string message) => new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        public static IntPtr GetWindowLongWrapper(HWND hWnd, int nIndex) => PInvoke.GetWindowLongPtr(hWnd, nIndex);
        public static IntPtr SetWindowLongWrapper(HWND hWnd, int nIndex, IntPtr dwNewLong) => PInvoke.SetWindowLongPtr(hWnd, nIndex, dwNewLong);
        public static int GetWindowRgn(HWND hWnd, HRGN hRgn) => PInvoke.GetWindowRgn(hWnd, hRgn);
        public static bool EnumChildWindows(HWND hWndParent, PInvoke.EnumWindowsProc lpEnumFunc, LPARAM lParam) => PInvoke.EnumChildWindows(hWndParent, lpEnumFunc, lParam);
        public static IntPtr GetClassLongWrapper(HWND hWnd, ClassLongIndex nIndex) => PInvoke.GetClassLongPtr(hWnd, (int)nIndex);
        public static IntPtr CopyIcon(IntPtr hIcon) => PInvoke.CopyIcon((HICON)hIcon).Value;
    }

    public static class Kernel32Api
    {
        public static bool AttachConsole(uint dwProcessId) => PInvoke.AttachConsole(dwProcessId);
        public static bool AttachConsole() => AttachConsole(unchecked((uint)-1));
        public static bool AllocConsole() => PInvoke.AllocConsole();
        public static string GetProcessPath(uint processId) { try { using var proc = System.Diagnostics.Process.GetProcessById((int)processId); return proc.MainModule?.FileName; } catch { return string.Empty; } }
        public static bool CloseHandle(HANDLE hObject) => PInvoke.CloseHandle(hObject);
        public static void SetLastError(uint dwErrCode) => PInvoke.SetLastError(dwErrCode);
        public static HANDLE GetCurrentProcess() => PInvoke.GetCurrentProcess();
        public static uint GetProductInfo(uint maj, uint min, uint spMaj, uint spMin, out uint type) => PInvoke.GetProductInfo(maj, min, spMaj, spMin, out type);
        public static bool GetVersionEx(ref OsVersionInfoEx info) => true;
        public static HANDLE OpenThread(ThreadAccess dwDesiredAccess, bool bInheritHandle, uint dwThreadId) => PInvoke.OpenThread((uint)dwDesiredAccess, bInheritHandle, dwThreadId);
        public static uint SuspendThread(HANDLE hThread) => PInvoke.SuspendThread(hThread);
        public static uint ResumeThread(HANDLE hThread) => PInvoke.ResumeThread(hThread);
    }

    public static class Gdi32Api
    {
        public static HDC CreateCompatibleDC(HDC hdc) => PInvoke.CreateCompatibleDC(hdc);
        public static bool BitBlt(HDC hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, HDC hdcSrc, int nXSrc, int nYSrc, ROP_CODE dwRop) => PInvoke.BitBlt(hdcDest, nXDest, nYDest, nWidth, nHeight, hdcSrc, nXSrc, nYSrc, (uint)dwRop);
        public static HRGN CreateRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect) => PInvoke.CreateRectRgn(nLeftRect, nTopRect, nRightRect, nBottomRect);
        public static uint GetPixel(HDC hdc, int nXPos, int nYPos) => PInvoke.GetPixel(hdc, nXPos, nYPos);
        public static IntPtr CreateDIBSection(HDC hdc, ref BitmapInfo pbmi, uint iUsage, out IntPtr ppvBits, HANDLE hSection, uint dwOffset) => PInvoke.CreateDIBSection(hdc, ref pbmi, iUsage, out ppvBits, hSection, dwOffset);
    }

    public static class DwmApi
    {
        public static bool IsWindowCloaked(IntPtr hWnd) { PInvoke.DwmGetWindowAttribute(hWnd, (int)DwmWindowAttributes.Cloaked, out int cloaked, sizeof(int)); return cloaked != 0; }
        public static bool IsDwmEnabled { get { PInvoke.DwmIsCompositionEnabled(out var enabled); return enabled; } }
        public static int DwmRegisterThumbnail(IntPtr hwndDestination, IntPtr hwndSource, out IntPtr phThumbnailId) { phThumbnailId = IntPtr.Zero; return 0; }
        public static int DwmUnregisterThumbnail(IntPtr hThumbnailId) => 0;
        public static int DwmUpdateThumbnailProperties(IntPtr hThumbnailId, ref DwmThumbnailProperties ptnProperties) => 0;
        public static int DwmQueryThumbnailSourceSize(IntPtr hThumbnailId, out Size pSize) { pSize = new Size(0,0); return 0; }
        public static int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute) => PInvoke.DwmGetWindowAttribute(hwnd, dwAttribute, out pvAttribute, cbAttribute);
        public static Color ColorizationColor => Color.Blue;
    }

    public struct DwmThumbnailProperties { public uint Opacity; public bool Visible; public RECT Destination; }
    public enum DwmWindowAttributes { ExtendedFrameBounds = 9, Cloaked = 14 }
    public static class RegionResults { public const int Error = 0; public const int NullRegion = 1; }
    public enum WindowLongIndex { GWL_STYLE = -16, GWL_EXSTYLE = -20 }
    public enum ClassLongIndex { GCLP_HICON = -14 }
    public enum ThreadAccess { SUSPEND_RESUME = 0x0002 }
    public enum PrintWindowFlags { PW_COMPLETE = 0, PW_RENDERFULLCONTENT = 2 }
    public enum WindowsProductTypes : byte { VER_NT_WORKSTATION = 1, VER_NT_DOMAIN_CONTROLLER = 2, VER_NT_SERVER = 3 }
    public enum WindowsSuites : ushort { SmallBusiness = 0x0001, Enterprise = 0x0002, BackOffice = 0x0004, Communications = 0x0008, Terminal = 0x0010, SmallBusinessRestricted = 0x0020, EmbeddedNT = 0x0040, DataCenter = 0x0080, SingleUserTS = 0x0100, Personal = 0x0200, Blade = 0x0400, EmbeddedRestricted = 0x0800, Appliance = 0x1000, WHServer = 0x8000 }
}
