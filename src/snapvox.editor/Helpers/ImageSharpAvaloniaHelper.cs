using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Bmp;

namespace snapvox.editor.helpers
{
    public static class ImageSharpAvaloniaHelper
    {
        public static Bitmap ToAvaloniaBitmap(this Image image)
        {
            if (image == null) return null;
            using (var ms = new MemoryStream())
            {
                image.Save(ms, new BmpEncoder());
                ms.Position = 0;
                return new Bitmap(ms);
            }
        }

        public static Task<Bitmap> ToAvaloniaBitmapAsync(this Image image)
        {
            if (image == null) return Task.FromResult<Bitmap>(null);
            return Task.Run(() =>
            {
                using (var ms = new MemoryStream())
                {
                    image.Save(ms, new BmpEncoder());
                    ms.Position = 0;
                    return new Bitmap(ms);
                }
            });
        }
    }
}