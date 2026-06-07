using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace snapvox.native
{
    public static class OcrImagePreprocessor
    {
        private const int MaxDimension = 3840;

        public static Image<Bgra32> Prepare(Image source)
        {
            if (source == null)
            {
                return null;
            }

            Image<Bgra32> working = source is Image<Bgra32> bgra
                ? bgra.Clone(x => { })
                : source.CloneAs<Bgra32>();

            int maxSide = Math.Max(working.Width, working.Height);
            if (maxSide > MaxDimension)
            {
                double scale = MaxDimension / (double)maxSide;
                int targetWidth = Math.Max(1, (int)Math.Round(working.Width * scale));
                int targetHeight = Math.Max(1, (int)Math.Round(working.Height * scale));
                working.Mutate(ctx => ctx.Resize(targetWidth, targetHeight));
            }

            working.Mutate(ctx => ctx.Grayscale().BinaryThreshold(0.45f));
            return working;
        }

        public static (float ScaleX, float ScaleY) GetScaleFactors(Image source, Image processed)
        {
            if (source == null || processed == null)
            {
                return (1f, 1f);
            }

            float scaleX = source.Width / (float)Math.Max(1, processed.Width);
            float scaleY = source.Height / (float)Math.Max(1, processed.Height);
            return (scaleX, scaleY);
        }
    }
}
