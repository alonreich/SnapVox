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
    }
}
