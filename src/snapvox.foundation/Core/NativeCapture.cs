using System;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using snapvox.foundation.Interop;
using snapvox.native.foundation;
using log4net;

namespace snapvox.foundation.core
{
    public static class NativeCapture
    {
        private static readonly ILog Log = snapvox.foundation.core.LogHelper.GetLogger(typeof(NativeCapture));
        private const int MaxCaptureDimension = 16384;
        private const long MaxCapturePixels = 64L * 1024 * 1024;
        private const uint Srccopy = 0x00CC0020;
        private const uint CaptureBlt = 0x40000000;

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            public uint bmiColors;
        }

        [DllImport("gdi32.dll")]
        public static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, uint dwRop);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint iUsage, out IntPtr ppvBits, IntPtr hSection, uint dwOffset);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);
        private const int SmXVirtualScreen = 76;
        private const int SmYVirtualScreen = 77;
        private const int SmCXVirtualScreen = 78;
        private const int SmCYVirtualScreen = 79;

        public static RECT GetVirtualDesktopBounds()
        {
            return RECT.FromXYWH(
                GetSystemMetrics(SmXVirtualScreen),
                GetSystemMetrics(SmYVirtualScreen),
                GetSystemMetrics(SmCXVirtualScreen),
                GetSystemMetrics(SmCYVirtualScreen));
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CURSORINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hCursor;
            public POINT ptScreenPos;
        }

        [DllImport("user32.dll")]
        public static extern bool GetCursorInfo(out CURSORINFO pci);

        [DllImport("user32.dll")]
        public static extern bool DrawIcon(IntPtr hDC, int X, int Y, IntPtr hIcon);

        [DllImport("user32.dll")]
        public static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

        [StructLayout(LayoutKind.Sequential)]
        public struct ICONINFO
        {
            public bool fIcon;
            public int xHotspot;
            public int yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        public static unsafe Image<Bgra32> CaptureRegion(RECT region) => CaptureRegion(region, false);

        public static unsafe Image<Bgra32> CaptureRegion(RECT region, bool captureMouse)
        {
            region = region.Normalize();
            if (region.Width <= 0 || region.Height <= 0)
            {
                return null;
            }

            long pixelCount = (long)region.Width * region.Height;
            if (region.Width > MaxCaptureDimension || region.Height > MaxCaptureDimension || pixelCount > MaxCapturePixels)
            {
                Log.WarnFormat("Refusing oversized capture region {0}x{1}", region.Width, region.Height);
                return null;
            }

            IntPtr hdcScreen = IntPtr.Zero;
            IntPtr hdcDest = IntPtr.Zero;
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr hOldBitmap = IntPtr.Zero;

            try
            {
                hdcScreen = GetDC(IntPtr.Zero);
                if (hdcScreen == IntPtr.Zero)
                {
                    return null;
                }

                hdcDest = CreateCompatibleDC(hdcScreen);
                if (hdcDest == IntPtr.Zero)
                {
                    return null;
                }

                BITMAPINFO bmi = new BITMAPINFO();
                bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
                bmi.bmiHeader.biWidth = region.Width;
                bmi.bmiHeader.biHeight = -region.Height;
                bmi.bmiHeader.biPlanes = 1;
                bmi.bmiHeader.biBitCount = 32;
                bmi.bmiHeader.biCompression = 0;

                IntPtr pBits;
                hBitmap = CreateDIBSection(hdcDest, ref bmi, 0, out pBits, IntPtr.Zero, 0);
                if (hBitmap == IntPtr.Zero || pBits == IntPtr.Zero)
                {
                    return null;
                }

                hOldBitmap = SelectObject(hdcDest, hBitmap);
                if (!BitBlt(hdcDest, 0, 0, region.Width, region.Height, hdcScreen, region.Left, region.Top, Srccopy | CaptureBlt))
                {
                    return null;
                }

                if (captureMouse)
                {
                    CURSORINFO ci = new CURSORINFO();
                    ci.cbSize = Marshal.SizeOf(ci);
                    if (GetCursorInfo(out ci) && ci.flags == 0x00000001)
                    {
                        if (GetIconInfo(ci.hCursor, out ICONINFO ii))
                        {
                            DrawIcon(hdcDest, ci.ptScreenPos.X - region.Left - ii.xHotspot, ci.ptScreenPos.Y - region.Top - ii.yHotspot, ci.hCursor);
                            if (ii.hbmMask != IntPtr.Zero) DeleteObject(ii.hbmMask);
                            if (ii.hbmColor != IntPtr.Zero) DeleteObject(ii.hbmColor);
                        }
                    }
                }

                int length = checked(region.Width * region.Height);
                using var image = Image.LoadPixelData<Bgra32>(
                    new ReadOnlySpan<Bgra32>(pBits.ToPointer(), length),
                    region.Width,
                    region.Height);

                return image.Clone(ctx => { });
            }
            catch (Exception ex)
            {
                Log.Error("CaptureRegion failed.", ex);
                return null;
            }
            finally
            {
                if (hOldBitmap != IntPtr.Zero && hdcDest != IntPtr.Zero) SelectObject(hdcDest, hOldBitmap);
                if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
                if (hdcDest != IntPtr.Zero) DeleteDC(hdcDest);
                if (hdcScreen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, hdcScreen);
            }
        }

        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

        private const int DwmwaExtendedFrameBounds = 9;
        private const uint PwRenderFullContent = 0x00000002;

        // Renders the window itself into an offscreen bitmap via PrintWindow, which works even
        // when other windows cover it: the result looks like the window was brought to the
        // front and contains only that window's own content. Returns null when the window
        // cannot be rendered this way, so callers can fall back to a region capture.
        public static unsafe Image<Bgra32> CaptureWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return null;

            try
            {
                if (!GetWindowRect(hWnd, out RECT windowRect) || windowRect.Width <= 0 || windowRect.Height <= 0) return null;
                if (windowRect.Width > MaxCaptureDimension || windowRect.Height > MaxCaptureDimension || (long)windowRect.Width * windowRect.Height > MaxCapturePixels)
                {
                    Log.WarnFormat("Refusing oversized window capture {0}x{1}", windowRect.Width, windowRect.Height);
                    return null;
                }

                // PrintWindow renders relative to the full window rect (which includes the
                // invisible resize borders); the visible content aligns with the DWM extended
                // frame bounds, so capture the full rect and crop the frame offset afterwards.
                RECT frameBounds = windowRect;
                if (DwmGetWindowAttribute(hWnd, DwmwaExtendedFrameBounds, out RECT dwmBounds, Marshal.SizeOf<RECT>()) == 0 && dwmBounds.Width > 0 && dwmBounds.Height > 0)
                {
                    frameBounds = dwmBounds;
                }

                IntPtr hdcScreen = IntPtr.Zero;
                IntPtr hdcDest = IntPtr.Zero;
                IntPtr hBitmap = IntPtr.Zero;
                IntPtr hOldBitmap = IntPtr.Zero;

                try
                {
                    hdcScreen = GetDC(IntPtr.Zero);
                    if (hdcScreen == IntPtr.Zero)
                    {
                        return null;
                    }

                    hdcDest = CreateCompatibleDC(hdcScreen);
                    if (hdcDest == IntPtr.Zero)
                    {
                        return null;
                    }

                    BITMAPINFO bmi = new BITMAPINFO();
                    bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
                    bmi.bmiHeader.biWidth = windowRect.Width;
                    bmi.bmiHeader.biHeight = -windowRect.Height;
                    bmi.bmiHeader.biPlanes = 1;
                    bmi.bmiHeader.biBitCount = 32;
                    bmi.bmiHeader.biCompression = 0;

                    hBitmap = CreateDIBSection(hdcDest, ref bmi, 0, out IntPtr pBits, IntPtr.Zero, 0);
                    if (hBitmap == IntPtr.Zero || pBits == IntPtr.Zero)
                    {
                        return null;
                    }

                    hOldBitmap = SelectObject(hdcDest, hBitmap);
                    // PW_RENDERFULLCONTENT also renders DirectComposition/hardware-accelerated
                    // content (browsers, VS, etc.) on Windows 8.1 and newer.
                    if (!PrintWindow(hWnd, hdcDest, PwRenderFullContent))
                    {
                        return null;
                    }

                    int offsetX = Math.Max(0, Math.Min(windowRect.Width - 1, frameBounds.Left - windowRect.Left));
                    int offsetY = Math.Max(0, Math.Min(windowRect.Height - 1, frameBounds.Top - windowRect.Top));
                    int cropWidth = Math.Max(0, Math.Min(windowRect.Width - offsetX, frameBounds.Width));
                    int cropHeight = Math.Max(0, Math.Min(windowRect.Height - offsetY, frameBounds.Height));
                    if (cropWidth <= 0 || cropHeight <= 0) return null;

                    int length = checked(windowRect.Width * windowRect.Height);
                    using var image = Image.LoadPixelData<Bgra32>(
                        new ReadOnlySpan<Bgra32>(pBits.ToPointer(), length),
                        windowRect.Width,
                        windowRect.Height);

                    var cropRectangle = new Rectangle(offsetX, offsetY, cropWidth, cropHeight);
                    if (cropRectangle.X == 0 && cropRectangle.Y == 0 && cropRectangle.Width == windowRect.Width && cropRectangle.Height == windowRect.Height)
                    {
                        return image.Clone(ctx => { });
                    }
                    return image.Clone(ctx => ctx.Crop(cropRectangle));
                }
                finally
                {
                    if (hOldBitmap != IntPtr.Zero && hdcDest != IntPtr.Zero) SelectObject(hdcDest, hOldBitmap);
                    if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
                    if (hdcDest != IntPtr.Zero) DeleteDC(hdcDest);
                    if (hdcScreen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, hdcScreen);
                }
            }
            catch (Exception ex)
            {
                Log.Error("CaptureWindow failed.", ex);
                return null;
            }
        }
    }
}
