using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using snapvox.editor.forms;
using snapvox.editor.Services;
using snapvox.foundation.core;
using snapvox.foundation.IniFile;
using snapvox.native.foundation;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SharpImage = SixLabors.ImageSharp.Image;
using SharpImage32 = SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>;
using Xunit;

namespace snapvox.tests
{
    public class BorderedExportSafetyTests
    {
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private static object? Call(ImageEditorWindow editor, string method, params object[] args)
            => typeof(ImageEditorWindow).GetMethod(method, Private)!.Invoke(editor, args);

        private static T Field<T>(ImageEditorWindow editor, string name)
            => (T)typeof(ImageEditorWindow).GetField(name, Private)!.GetValue(editor)!;

        private static Task<SharpImage> Export(ImageEditorWindow editor)
            => (Task<SharpImage>)Call(editor, "GetFlattenedImageAsync")!;

        [Fact]
        public void ApplyFrameBorder_ExpandsCanvasOutward_PreservingAllInnerAndEdgePixels()
        {
            const int originalWidth = 100;
            const int originalHeight = 100;
            const int thickness = 4;

            var green = new Rgba32(0, 255, 0, 255);
            var white = new Rgba32(255, 255, 255, 255);
            var blue = new Rgba32(0, 0, 255, 255);
            var red = new Rgba32(255, 0, 0, 255);
            var borderCol = SixLabors.ImageSharp.Color.FromRgb(0x43, 0x43, 0x43);
            var expectedBorderRgba = new Rgba32(0x43, 0x43, 0x43, 255);

            using var img = new SharpImage32(originalWidth, originalHeight, green);

            // Mark 4 corner pixels of original image
            img[0, 0] = white;
            img[originalWidth - 1, 0] = white;
            img[0, originalHeight - 1] = white;
            img[originalWidth - 1, originalHeight - 1] = white;

            // Mark 4 edge midpoints
            img[50, 0] = blue;
            img[0, 50] = blue;
            img[originalWidth - 1, 50] = blue;
            img[50, originalHeight - 1] = blue;

            // Mark center pixel
            img[50, 50] = red;

            // Apply 4px border outward
            EditorExportService.ApplyFrameBorder(img, thickness, borderCol);

            // Assert canvas expanded outward by 2*thickness in both dimensions
            Assert.Equal(originalWidth + 2 * thickness, img.Width);   // 108
            Assert.Equal(originalHeight + 2 * thickness, img.Height); // 108

            // Assert border area has the border color (outermost 4 pixels on all sides)
            Assert.Equal(expectedBorderRgba, img[0, 0]);
            Assert.Equal(expectedBorderRgba, img[img.Width - 1, 0]);
            Assert.Equal(expectedBorderRgba, img[0, img.Height - 1]);
            Assert.Equal(expectedBorderRgba, img[img.Width - 1, img.Height - 1]);
            Assert.Equal(expectedBorderRgba, img[thickness - 1, thickness - 1]);
            Assert.Equal(expectedBorderRgba, img[img.Width - thickness, img.Height - thickness]);

            // Assert original corner pixels are uncropped and centered at offset (thickness, thickness)
            Assert.Equal(white, img[thickness, thickness]);
            Assert.Equal(white, img[thickness + originalWidth - 1, thickness]);
            Assert.Equal(white, img[thickness, thickness + originalHeight - 1]);
            Assert.Equal(white, img[thickness + originalWidth - 1, thickness + originalHeight - 1]);

            // Assert original edge midpoints are uncropped and properly shifted
            Assert.Equal(blue, img[thickness + 50, thickness]);
            Assert.Equal(blue, img[thickness, thickness + 50]);
            Assert.Equal(blue, img[thickness + originalWidth - 1, thickness + 50]);
            Assert.Equal(blue, img[thickness + 50, thickness + originalHeight - 1]);

            // Assert center pixel is preserved
            Assert.Equal(red, img[thickness + 50, thickness + 50]);

            // Assert inner fill is intact
            Assert.Equal(green, img[thickness + 1, thickness + 1]);
            Assert.Equal(green, img[thickness + originalWidth - 2, thickness + originalHeight - 2]);
        }

        [Fact]
        public void ApplyFrameBorder_WithHexColor_ParsesCorrectly()
        {
            using var img = new SharpImage32(50, 50, new Rgba32(0, 0, 0));
            EditorExportService.ApplyFrameBorder(img, 2, "#FF0000");

            Assert.Equal(54, img.Width);
            Assert.Equal(54, img.Height);
            Assert.Equal(new Rgba32(255, 0, 0, 255), img[0, 0]);
            Assert.Equal(new Rgba32(0, 0, 0, 255), img[2, 2]);
        }

        [Fact]
        public void ApplyFrameBorder_ZeroOrNegativeThickness_DoesNotMutate()
        {
            using var img = new SharpImage32(30, 30, new Rgba32(100, 100, 100));
            EditorExportService.ApplyFrameBorder(img, 0, "#123456");
            Assert.Equal(30, img.Width);
            Assert.Equal(30, img.Height);

            EditorExportService.ApplyFrameBorder(img, -5, "#123456");
            Assert.Equal(30, img.Width);
            Assert.Equal(30, img.Height);
        }

        [AvaloniaFact]
        public async Task Export_WithBorder_DoesNotDetachVisualTree_AndPreservesOriginalEdgePixels()
        {
            var config = IniConfig.GetIniSection<CoreConfiguration>(allowSave: false);
            config.AddFrameBorders = true;
            config.FrameBorderThickness = 4;
            config.FrameBorderColor = "#434343";
            config.WarnBeforeClosingEditor = false;

            const int w = 100;
            const int h = 100;
            var green = new Rgba32(0, 255, 0, 255);
            var white = new Rgba32(255, 255, 255, 255);

            var initialImage = new SharpImage32(w, h, green);
            initialImage[0, 0] = white;
            initialImage[w - 1, 0] = white;
            initialImage[0, h - 1] = white;
            initialImage[w - 1, h - 1] = white;

            var editor = new ImageEditorWindow();
            try
            {
                await editor.SetImageAsync(initialImage, RECT.FromXYWH(0, 0, w, h));

                var canvas = Field<Canvas>(editor, "_canvas");
                var originalParent = canvas.Parent;
                Assert.NotNull(originalParent);

                var snipBorder = Field<Border>(editor, "_snipBorder");
                var overlayCanvas = Field<Canvas>(editor, "_overlayCanvas");
                var zoomContainer = Field<Panel>(editor, "_zoomContainer");

                // Execute export
                using var exported = await Export(editor);
                using var pixels = exported.CloneAs<Rgba32>();

                // 1. Visual tree detachment check: canvas must remain under its original parent!
                Assert.Same(originalParent, canvas.Parent);

                // 2. Export dimensions check: must be outward padded (100 + 2*4 = 108)
                Assert.Equal(108, exported.Width);
                Assert.Equal(108, exported.Height);

                // 3. Border color check at corners
                var expectedBorder = new Rgba32(0x43, 0x43, 0x43, 255);
                Assert.Equal(expectedBorder, pixels[0, 0]);
                Assert.Equal(expectedBorder, pixels[107, 0]);
                Assert.Equal(expectedBorder, pixels[0, 107]);
                Assert.Equal(expectedBorder, pixels[107, 107]);

                // 4. Original edge/corner pixels must NOT be cropped or shifted
                Assert.Equal(white, pixels[4, 4]);
                Assert.Equal(white, pixels[103, 4]);
                Assert.Equal(white, pixels[4, 103]);
                Assert.Equal(white, pixels[103, 103]);

                // 5. UI state restoration check
                Assert.NotNull(snipBorder);
                Assert.NotNull(overlayCanvas);
                Assert.NotNull(zoomContainer);
            }
            finally
            {
                editor.Close();
            }
        }
    }
}
