using System;
using System.Collections.Generic;
using snapvox.native.foundation;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Transforms;

namespace snapvox.native
{
    public enum OcrPreprocessingProfile
    {
        Windows,
        Tesseract
    }

    public sealed class OcrPreparedImage : IDisposable
    {
        private readonly int _sourceWidth;
        private readonly int _sourceHeight;
        private readonly int _resizedWidth;
        private readonly int _resizedHeight;
        private readonly int _processedWidth;
        private readonly int _processedHeight;
        private readonly double _deskewDegrees;

        public OcrPreparedImage(Image<Bgra32> image, int sourceWidth, int sourceHeight, int resizedWidth, int resizedHeight, double deskewDegrees)
        {
            Image = image;
            _sourceWidth = Math.Max(1, sourceWidth);
            _sourceHeight = Math.Max(1, sourceHeight);
            _resizedWidth = Math.Max(1, resizedWidth);
            _resizedHeight = Math.Max(1, resizedHeight);
            _processedWidth = Math.Max(1, image?.Width ?? resizedWidth);
            _processedHeight = Math.Max(1, image?.Height ?? resizedHeight);
            _deskewDegrees = deskewDegrees;
        }

        public Image<Bgra32> Image { get; }

        public RECT MapBounds(double x, double y, double width, double height)
        {
            if (width <= 0 || height <= 0)
            {
                return RECT.Empty;
            }

            if (Math.Abs(_deskewDegrees) < 0.05)
            {
                return RECT.FromXYWH(
                    (int)Math.Round(x * _sourceWidth / _processedWidth),
                    (int)Math.Round(y * _sourceHeight / _processedHeight),
                    Math.Max(1, (int)Math.Round(width * _sourceWidth / _processedWidth)),
                    Math.Max(1, (int)Math.Round(height * _sourceHeight / _processedHeight)));
            }

            Span<(double X, double Y)> points = stackalloc (double X, double Y)[4];
            points[0] = MapPoint(x, y);
            points[1] = MapPoint(x + width, y);
            points[2] = MapPoint(x, y + height);
            points[3] = MapPoint(x + width, y + height);

            double minX = Math.Min(Math.Min(points[0].X, points[1].X), Math.Min(points[2].X, points[3].X));
            double minY = Math.Min(Math.Min(points[0].Y, points[1].Y), Math.Min(points[2].Y, points[3].Y));
            double maxX = Math.Max(Math.Max(points[0].X, points[1].X), Math.Max(points[2].X, points[3].X));
            double maxY = Math.Max(Math.Max(points[0].Y, points[1].Y), Math.Max(points[2].Y, points[3].Y));

            int left = Math.Clamp((int)Math.Floor(minX), 0, _sourceWidth - 1);
            int top = Math.Clamp((int)Math.Floor(minY), 0, _sourceHeight - 1);
            int right = Math.Clamp((int)Math.Ceiling(maxX), left + 1, _sourceWidth);
            int bottom = Math.Clamp((int)Math.Ceiling(maxY), top + 1, _sourceHeight);
            return RECT.FromXYWH(left, top, right - left, bottom - top);
        }

        private (double X, double Y) MapPoint(double x, double y)
        {
            double radians = _deskewDegrees * Math.PI / 180.0;
            double cos = Math.Cos(radians);
            double sin = Math.Sin(radians);
            double px = x - (_processedWidth / 2.0);
            double py = y - (_processedHeight / 2.0);
            double rx = px * cos - py * sin + (_resizedWidth / 2.0);
            double ry = px * sin + py * cos + (_resizedHeight / 2.0);
            return (rx * _sourceWidth / _resizedWidth, ry * _sourceHeight / _resizedHeight);
        }

        public void Dispose()
        {
            Image?.Dispose();
        }
    }

    public static class OcrImagePreprocessor
    {
        private const int MaxDimension = 3840;
        private const int MinimumTextDimension = 1400;

        public static OcrPreparedImage Prepare(Image source, OcrPreprocessingProfile profile)
        {
            if (source == null)
            {
                return null;
            }

            Image<Bgra32> working = source is Image<Bgra32> bgra ? bgra.Clone(x => { }) : source.CloneAs<Bgra32>();
            double scale = GetScale(source.Width, source.Height);
            int targetWidth = Math.Max(1, (int)Math.Round(source.Width * scale));
            int targetHeight = Math.Max(1, (int)Math.Round(source.Height * scale));

            if (targetWidth != working.Width || targetHeight != working.Height)
            {
                working.Mutate(ctx => ctx.Resize(targetWidth, targetHeight, KnownResamplers.Lanczos3));
            }

            if (profile == OcrPreprocessingProfile.Windows)
            {
                working.Mutate(ctx => ctx.Grayscale().Contrast(1.12f).GaussianBlur(0.25f).GaussianSharpen(0.65f));
            }
            else
            {
                working.Mutate(ctx => ctx.Grayscale().Contrast(1.25f).GaussianBlur(0.3f).GaussianSharpen(0.85f).AdaptiveThreshold(0.12f));
            }

            double skew = EstimateDeskewDegrees(working);
            if (Math.Abs(skew) >= 0.25 && Math.Abs(skew) <= 3.0)
            {
                working.Mutate(ctx => ctx.Rotate((float)(-skew), KnownResamplers.Bicubic).BackgroundColor(Color.White));
                if (profile == OcrPreprocessingProfile.Tesseract)
                {
                    working.Mutate(ctx => ctx.AdaptiveThreshold(0.12f));
                }
            }
            else
            {
                skew = 0;
            }

            return new OcrPreparedImage(working, source.Width, source.Height, targetWidth, targetHeight, skew);
        }

        private static double GetScale(int width, int height)
        {
            int maxSide = Math.Max(width, height);
            int minSide = Math.Min(width, height);
            double scale = 1.0;

            if (maxSide < MinimumTextDimension || minSide < 720)
            {
                scale = 2.0;
            }

            if (maxSide < 900 || minSide < 420)
            {
                scale = 3.0;
            }

            if (maxSide * scale > MaxDimension)
            {
                scale = MaxDimension / (double)Math.Max(1, maxSide);
            }

            return Math.Max(1.0, scale);
        }

        private static double EstimateDeskewDegrees(Image<Bgra32> image)
        {
            if (image.Width < 120 || image.Height < 60)
            {
                return 0;
            }

            var points = CollectDarkPixels(image);
            if (points.Count < 80)
            {
                return 0;
            }

            double bestAngle = 0;
            double bestScore = double.MinValue;
            for (double angle = -3.0; angle <= 3.001; angle += 0.25)
            {
                double score = ScoreAngle(points, image.Width, image.Height, angle);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestAngle = angle;
                }
            }

            return Math.Abs(bestAngle) < 0.25 ? 0 : bestAngle;
        }

        private static List<(double X, double Y)> CollectDarkPixels(Image<Bgra32> image)
        {
            int step = Math.Max(1, Math.Min(image.Width, image.Height) / 500);
            var points = new List<(double X, double Y)>(4096);
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y += step)
                {
                    Span<Bgra32> row = accessor.GetRowSpan(y);
                    for (int x = 0; x < row.Length; x += step)
                    {
                        Bgra32 pixel = row[x];
                        int luminance = (pixel.R * 299 + pixel.G * 587 + pixel.B * 114) / 1000;
                        if (pixel.A > 32 && luminance < 135)
                        {
                            points.Add((x, y));
                        }
                    }
                }
            });
            return points;
        }

        private static double ScoreAngle(List<(double X, double Y)> points, int width, int height, double degrees)
        {
            double radians = degrees * Math.PI / 180.0;
            double sin = Math.Sin(radians);
            double cos = Math.Cos(radians);
            double cx = width / 2.0;
            double cy = height / 2.0;
            int[] rows = new int[height + 64];

            foreach (var point in points)
            {
                double px = point.X - cx;
                double py = point.Y - cy;
                int y = (int)Math.Round(px * sin + py * cos + cy) + 32;
                if ((uint)y < (uint)rows.Length)
                {
                    rows[y]++;
                }
            }

            double score = 0;
            for (int i = 1; i < rows.Length - 1; i++)
            {
                int value = rows[i];
                score += value * value;
                score -= Math.Abs(rows[i - 1] - rows[i + 1]) * 0.15;
            }

            return score;
        }
    }
}
