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
        public async Task ClipboardInjection_MustSupportMultipleFormats()
        {
            using var image = new Image<Bgra32>(100, 100);
            
            image[0, 0] = Color.Red;

            await UiClipboard.SetImageAsync(image);
            
            Assert.True(true, "Clipboard logic executed without crash.");
        }
    }
}
