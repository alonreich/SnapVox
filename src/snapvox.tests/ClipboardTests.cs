using System;
using System.Threading.Tasks;
using snapvox.foundation.core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace snapvox.tests
{
    public class ClipboardTests
    {
        [Fact]
        public async Task InvalidImage_ReportsFailureInsteadOfPretendingToCopy()
        {
            var image = new Image<Bgra32>(20, 20);
            image.Dispose();
            // Fails before OS clipboard access; never changes the user's clipboard.
            await Assert.ThrowsAnyAsync<Exception>(() => UiClipboard.SetImageAsync(image));
        }

        [Fact]
        public void ImageEncoding_ProvidesRealDibBmpAndPngPayloads()
        {
            using var image = new Image<Bgra32>(20, 20, new Bgra32(255, 0, 0));
            var method = typeof(UiClipboard).GetMethod("EncodeClipboardImage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            var encoded = ((byte[] Dib, byte[] Bmp, byte[] Png))method.Invoke(null, new object[] { image })!;
            Assert.Equal(new byte[] { 0x42, 0x4d }, encoded.Bmp.Take(2));
            Assert.Equal(new byte[] { 137, 80, 78, 71 }, encoded.Png.Take(4));
            Assert.Equal(encoded.Bmp.Skip(14), encoded.Dib);
            using var decoded = Image.Load<Bgra32>(encoded.Png);
            Assert.Equal(image[0, 0], decoded[0, 0]);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetClipboardOwner();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern uint RegisterClipboardFormat(string lpszFormat);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsClipboardFormatAvailable(uint format);

        [Fact]
        public async Task SetImageAsync_MaintainsLivingClipboardOwnerWindow_AfterCompletion()
        {
            if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                return;

            using var image = new Image<Rgba32>(32, 32, new Rgba32(120, 180, 240, 255));
            await UiClipboard.SetImageAsync(image);

            IntPtr ownerHwnd = UiClipboard.GetClipboardOwnerHwnd();
            Assert.NotEqual(IntPtr.Zero, ownerHwnd);
            Assert.True(IsWindow(ownerHwnd), "Clipboard owner window must be a living HWND.");

            IntPtr currentOwner = GetClipboardOwner();
            Assert.Equal(ownerHwnd, currentOwner);
            Assert.True(IsWindow(currentOwner), "GetClipboardOwner() must return a living window handle after SetImageAsync completes.");

            // Perform second clipboard operation to ensure the owner is persistent across operations
            using var image2 = new Image<Rgba32>(16, 16, new Rgba32(200, 100, 50, 255));
            await UiClipboard.SetImageAsync(image2);

            Assert.Equal(ownerHwnd, UiClipboard.GetClipboardOwnerHwnd());
            Assert.Equal(ownerHwnd, GetClipboardOwner());
            Assert.True(IsWindow(GetClipboardOwner()), "Clipboard owner window must remain alive across multiple clipboard writes.");
        }

        [Fact]
        public async Task SetImageAsync_WritesBothDibAndPngFormats_PreservingAlphaTransparency()
        {
            if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                return;

            const int w = 32;
            const int h = 32;
            using var image = new Image<Rgba32>(w, h);

            // Create translucent highlight (50% alpha yellow) on left half, opaque on right half
            var highlight = new Rgba32(255, 255, 0, 128);
            var opaque = new Rgba32(0, 128, 255, 255);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    image[x, y] = x < w / 2 ? highlight : opaque;
                }
            }

            await UiClipboard.SetImageAsync(image);

            // 1. Verify CF_DIB (8) is available for legacy Win32 consumers
            const uint CF_DIB = 8;
            Assert.True(IsClipboardFormatAvailable(CF_DIB), "CF_DIB must be available on clipboard for legacy Win32 apps.");

            // 2. Verify registered "PNG" is available for modern alpha-aware consumers
            uint pngFormat = RegisterClipboardFormat("PNG");
            Assert.NotEqual(0u, pngFormat);
            Assert.True(IsClipboardFormatAvailable(pngFormat), "Registered PNG format must be available on clipboard.");

            // 3. Verify CF_DIBV5 (17) is available for alpha-aware DIB consumers
            const uint CF_DIBV5 = 17;
            Assert.True(IsClipboardFormatAvailable(CF_DIBV5), "CF_DIBV5 must be available on clipboard when image contains alpha channel.");

            // 4. Paste back as a PNG-aware consumer and verify alpha preservation
            using var pastedImage = await UiClipboard.GetImageAsync();
            Assert.NotNull(pastedImage);

            using var pastedRgba = pastedImage.CloneAs<Rgba32>();
            Assert.Equal(w, pastedRgba.Width);
            Assert.Equal(h, pastedRgba.Height);

            // Verify translucent highlight alpha is preserved at 128 (not flattened to 255 or 0)
            Rgba32 pastedTranslucent = pastedRgba[5, 5];
            Assert.True(pastedTranslucent.A > 100 && pastedTranslucent.A < 150,
                $"Translucent highlight alpha must be preserved around 128, actual: {pastedTranslucent.A}");
            Assert.True(pastedTranslucent.R > 240 && pastedTranslucent.G > 240 && pastedTranslucent.B < 20,
                "Translucent highlight RGB channels must be preserved.");

            // Verify opaque pixel alpha is 255
            Rgba32 pastedOpaque = pastedRgba[25, 25];
            Assert.Equal(255, pastedOpaque.A);
            Assert.Equal(0, pastedOpaque.R);
            Assert.True(pastedOpaque.G > 100 && pastedOpaque.B > 240);
        }

        [Fact]
        public async Task SetTextAsync_MaintainsLivingClipboardOwnerWindow()
        {
            if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                return;

            await UiClipboard.SetTextAsync("SnapVox Persistent Owner Test");

            IntPtr owner = GetClipboardOwner();
            Assert.NotEqual(IntPtr.Zero, owner);
            Assert.True(IsWindow(owner), "Clipboard owner window must remain alive after SetTextAsync.");
            Assert.Equal(UiClipboard.GetClipboardOwnerHwnd(), owner);
        }
    }
}
