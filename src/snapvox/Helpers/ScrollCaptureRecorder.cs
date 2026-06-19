using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using log4net;
using snapvox.foundation.core;
using snapvox.native.foundation;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Runtime.InteropServices;

namespace snapvox.helpers
{
    internal sealed class ScrollCaptureRecorder : IAsyncDisposable
    {
        private static readonly ILog Log = LogHelper.GetLogger(typeof(ScrollCaptureRecorder));
        private readonly RECT _target;
        private readonly Channel<Image<Bgra32>> _frames;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly ScrollFrameStitcher _stitcher = new ScrollFrameStitcher();
        private Task _producer;
        private Task _consumer;
        private volatile bool _trackingFailed;
        private int _rejectedFrames;

        public ScrollCaptureRecorder(RECT target)
        {
            _target = target.Normalize();
            _frames = Channel.CreateBounded<Image<Bgra32>>(new BoundedChannelOptions(8)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true
            });
        }

        public int AcceptedFrames => _stitcher.AcceptedFrames;

        public bool TrackingFailed => _trackingFailed;

        public void Start()
        {
            _producer = Task.Run(ProduceAsync);
            _consumer = Task.Run(ConsumeAsync);
        }

        public async Task<Image<Bgra32>> FinishAsync(IProgress<double> progress = null)
        {
            _cts.Cancel();
            _frames.Writer.TryComplete();
            await WaitForTasksAsync().ConfigureAwait(false);
            if (_trackingFailed || _stitcher.AcceptedFrames < 2)
            {
                return null;
            }

            return await Task.Run(() => _stitcher.BuildImage(progress)).ConfigureAwait(false);
        }

        public async Task CancelAsync()
        {
            _cts.Cancel();
            _frames.Writer.TryComplete();
            await WaitForTasksAsync().ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            await CancelAsync().ConfigureAwait(false);
            _cts.Dispose();
            _stitcher.Dispose();
        }

        private async Task ProduceAsync()
        {
            try
            {
                while (!_cts.IsCancellationRequested && !_trackingFailed)
                {
                    Image<Bgra32> frame = NativeCapture.CaptureRegion(_target, false);
                    if (frame != null)
                    {
                        try
                        {
                            await _frames.Writer.WriteAsync(frame, _cts.Token).ConfigureAwait(false);
                        }
                        catch
                        {
                            frame.Dispose();
                            throw;
                        }
                    }

                    await Task.Delay(80, _cts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Log.Error("Scroll capture producer failed.", ex);
                _trackingFailed = true;
            }
            finally
            {
                _frames.Writer.TryComplete();
            }
        }

        private async Task ConsumeAsync()
        {
            try
            {
                await foreach (Image<Bgra32> frame in _frames.Reader.ReadAllAsync().ConfigureAwait(false))
                {
                    ScrollFrameStatus status = _stitcher.AddFrame(frame);
                    if (status == ScrollFrameStatus.Rejected)
                    {
                        _rejectedFrames++;
                        if (_rejectedFrames >= 5 && _stitcher.AcceptedFrames > 0)
                        {
                            _trackingFailed = true;
                            _cts.Cancel();
                            break;
                        }
                    }
                    else if (status == ScrollFrameStatus.Accepted)
                    {
                        _rejectedFrames = 0;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Log.Error("Scroll capture consumer failed.", ex);
                _trackingFailed = true;
            }
        }

        private async Task WaitForTasksAsync()
        {
            try
            {
                if (_producer != null)
                {
                    await _producer.ConfigureAwait(false);
                }
            }
            catch
            {
            }

            try
            {
                if (_consumer != null)
                {
                    await _consumer.ConfigureAwait(false);
                }
            }
            catch
            {
            }
        }
    }

    internal enum ScrollFrameStatus
    {
        Accepted,
        Duplicate,
        Rejected
    }

    internal sealed class ScrollFrameStitcher : IDisposable
    {
        private static readonly ILog Log = LogHelper.GetLogger(typeof(ScrollFrameStitcher));
        private const int MinMovementPixels = 12;
        private const double MaxAverageDiff = 50.0;
        private const long MaxCompositePixels = 180L * 1024L * 1024L;
        private readonly List<ScrollSegment> _segments = new List<ScrollSegment>();
        private SampleFrame _previousSample;
        private int _offsetX;
        private int _offsetY;
        private int _frameWidth;
        private int _frameHeight;
        private bool _disposed;

        public int AcceptedFrames { get; private set; }

        public ScrollFrameStatus AddFrame(Image<Bgra32> frame)
        {
            if (frame == null)
            {
                return ScrollFrameStatus.Rejected;
            }

            try
            {
                if (_segments.Count == 0)
                {
                    _frameWidth = frame.Width;
                    _frameHeight = frame.Height;
                    _previousSample = SampleFrame.Create(frame);
                    _segments.Add(new ScrollSegment(frame.Clone(), 0, 0));
                    AcceptedFrames = 1;
                    return ScrollFrameStatus.Accepted;
                }

                if (frame.Width != _frameWidth || frame.Height != _frameHeight)
                {
                    return ScrollFrameStatus.Rejected;
                }

                SampleFrame currentSample = SampleFrame.Create(frame);
                MovementEstimate estimate = EstimateMovement(_previousSample, currentSample);
                if (!estimate.IsReliable)
                {
                    currentSample.Dispose();
                    return ScrollFrameStatus.Rejected;
                }

                if (Math.Abs(estimate.DeltaX) < MinMovementPixels && Math.Abs(estimate.DeltaY) < MinMovementPixels)
                {
                    currentSample.Dispose();
                    return ScrollFrameStatus.Duplicate;
                }

                _offsetX += estimate.DeltaX;
                _offsetY += estimate.DeltaY;
                AddVisibleStrips(frame, _offsetX, _offsetY, estimate.DeltaX, estimate.DeltaY);
                _previousSample.Dispose();
                _previousSample = currentSample;
                AcceptedFrames++;
                return ScrollFrameStatus.Accepted;
            }
            finally
            {
                frame.Dispose();
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetPhysicallyInstalledSystemMemory(out long TotalMemoryInKilobytes);

        public Image<Bgra32> BuildImage(IProgress<double> progress = null)
        {
            if (_segments.Count < 2)
            {
                return null;
            }

            int minX = int.MaxValue;
            int minY = int.MaxValue;
            int maxX = int.MinValue;
            int maxY = int.MinValue;
            foreach (ScrollSegment segment in _segments)
            {
                minX = Math.Min(minX, segment.X);
                minY = Math.Min(minY, segment.Y);
                maxX = Math.Max(maxX, segment.X + segment.Image.Width);
                maxY = Math.Max(maxY, segment.Y + segment.Image.Height);
            }

            int width = maxX - minX;
            int height = maxY - minY;
            long totalPixels = (long)width * height;
            
            if (width <= 0 || height <= 0 || totalPixels > MaxCompositePixels)
            {
                Log.Error($"Scroll capture exceeds memory limits: {width}x{height} ({totalPixels} pixels)");
                return null;
            }

            if (GetPhysicallyInstalledSystemMemory(out long ramKb))
            {
                long availableBytes = ramKb * 1024 / 4; // Safety margin
                if (totalPixels * 4 > availableBytes)
                {
                    Log.Error("Insufficient physical memory to composite elongated image.");
                    return null;
                }
            }

            var result = new Image<Bgra32>(width, height);
            int count = 0;
            foreach (ScrollSegment segment in _segments)
            {
                result.Mutate(ctx => ctx.DrawImage(segment.Image, new Point(segment.X - minX, segment.Y - minY), 1f));
                count++;
                progress?.Report((double)count / _segments.Count);
            }
            return result;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _previousSample?.Dispose();
            foreach (ScrollSegment segment in _segments)
            {
                segment.Image.Dispose();
            }

            _segments.Clear();
        }

        private void AddVisibleStrips(Image<Bgra32> frame, int offsetX, int offsetY, int deltaX, int deltaY)
        {
            int absY = Math.Abs(deltaY);
            int absX = Math.Abs(deltaX);

            if (absY >= absX && absY >= MinMovementPixels)
            {
                int h = Math.Min(absY, frame.Height);
                Rectangle crop = deltaY > 0
                    ? new Rectangle(0, frame.Height - h, frame.Width, h)
                    : new Rectangle(0, 0, frame.Width, h);
                int y = deltaY > 0 ? offsetY + frame.Height - h : offsetY;
                _segments.Add(new ScrollSegment(frame.Clone(ctx => ctx.Crop(crop)), offsetX, y));
            }
            else if (absX > absY && absX >= MinMovementPixels)
            {
                int w = Math.Min(absX, frame.Width);
                Rectangle crop = deltaX > 0
                    ? new Rectangle(frame.Width - w, 0, w, frame.Height)
                    : new Rectangle(0, 0, w, frame.Height);
                int x = deltaX > 0 ? offsetX + frame.Width - w : offsetX;
                _segments.Add(new ScrollSegment(frame.Clone(ctx => ctx.Crop(crop)), x, offsetY));
            }
            else
            {
                _segments.Add(new ScrollSegment(frame.Clone(), offsetX, offsetY));
            }
        }

        private static MovementEstimate EstimateMovement(SampleFrame previous, SampleFrame current)
        {
            if (previous == null || current == null || previous.Width != current.Width || previous.Height != current.Height)
            {
                return MovementEstimate.Failed;
            }

            int width = previous.Width;
            int height = previous.Height;
            int maxDx = Math.Max(1, (int)(width * 0.70));
            int maxDy = Math.Max(1, (int)(height * 0.85));
            int minOverlap = Math.Max(32, (width * height) / 10);
            double bestScore = double.MaxValue;
            int bestDx = 0;
            int bestDy = 0;

            for (int dy = -maxDy; dy <= maxDy; dy++)
            {
                for (int dx = -maxDx; dx <= maxDx; dx++)
                {
                    int xStart = Math.Max(0, -dx);
                    int yStart = Math.Max(0, -dy);
                    int xEnd = Math.Min(width, width - dx);
                    int yEnd = Math.Min(height, height - dy);
                    int overlapW = xEnd - xStart;
                    int overlapH = yEnd - yStart;
                    if (overlapW <= 0 || overlapH <= 0 || overlapW * overlapH < minOverlap)
                    {
                        continue;
                    }

                    double score = AverageDiff(previous, current, dx, dy, xStart, yStart, xEnd, yEnd);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestDx = dx;
                        bestDy = dy;
                    }
                }
            }

            int originalDx = (int)Math.Round((double)bestDx * previous.Step);
            int originalDy = (int)Math.Round((double)bestDy * previous.Step);
            return new MovementEstimate(originalDx, originalDy, bestScore <= MaxAverageDiff);
        }

        private static double AverageDiff(SampleFrame previous, SampleFrame current, int dx, int dy, int xStart, int yStart, int xEnd, int yEnd)
        {
            long diff = 0;
            int count = 0;
            int stride = Math.Max(1, Math.Min(xEnd - xStart, yEnd - yStart) / 40);
            for (int y = yStart; y < yEnd; y += stride)
            {
                int previousRow = (y + dy) * previous.Width;
                int currentRow = y * current.Width;
                for (int x = xStart; x < xEnd; x += stride)
                {
                    diff += Math.Abs(previous.Gray[previousRow + x + dx] - current.Gray[currentRow + x]);
                    count++;
                }
            }

            return count == 0 ? double.MaxValue : (double)diff / count;
        }
    }

    internal sealed class ScrollSegment
    {
        public ScrollSegment(Image<Bgra32> image, int x, int y)
        {
            Image = image;
            X = x;
            Y = y;
        }

        public Image<Bgra32> Image { get; }
        public int X { get; }
        public int Y { get; }
    }

    internal readonly struct MovementEstimate
    {
        public MovementEstimate(int deltaX, int deltaY, bool reliable)
        {
            DeltaX = deltaX;
            DeltaY = deltaY;
            IsReliable = reliable;
        }

        public int DeltaX { get; }
        public int DeltaY { get; }
        public bool IsReliable { get; }
        public static MovementEstimate Failed => new MovementEstimate(0, 0, false);
    }

    internal sealed class SampleFrame : IDisposable
    {
        private SampleFrame(byte[] gray, int width, int height, int step)
        {
            Gray = gray;
            Width = width;
            Height = height;
            Step = step;
        }

        public byte[] Gray { get; private set; }
        public int Width { get; }
        public int Height { get; }
        public int Step { get; }

        public static SampleFrame Create(Image<Bgra32> image)
        {
            int marginX = Math.Max(0, image.Width / 10);
            int top = Math.Max(0, image.Height / 5);
            int bottomMargin = Math.Max(0, image.Height / 10);
            int contentWidth = Math.Max(1, image.Width - marginX * 2);
            int contentHeight = Math.Max(1, image.Height - top - bottomMargin);
            int step = Math.Max(1, Math.Max(contentWidth / 180, contentHeight / 140));
            int sampleWidth = Math.Max(1, contentWidth / step);
            int sampleHeight = Math.Max(1, contentHeight / step);
            byte[] gray = new byte[sampleWidth * sampleHeight];

            image.ProcessPixelRows(accessor =>
            {
                for (int sy = 0; sy < sampleHeight; sy++)
                {
                    int sourceY = Math.Min(image.Height - 1, top + sy * step);
                    Span<Bgra32> row = accessor.GetRowSpan(sourceY);
                    int targetRow = sy * sampleWidth;
                    for (int sx = 0; sx < sampleWidth; sx++)
                    {
                        int sourceX = Math.Min(image.Width - 1, marginX + sx * step);
                        Bgra32 px = row[sourceX];
                        gray[targetRow + sx] = (byte)((px.R * 30 + px.G * 59 + px.B * 11) / 100);
                    }
                }
            });

            return new SampleFrame(gray, sampleWidth, sampleHeight, step);
        }

        public void Dispose()
        {
            Gray = null;
        }
    }
}
