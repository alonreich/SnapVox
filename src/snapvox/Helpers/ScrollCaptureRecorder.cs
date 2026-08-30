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
                        await Task.Delay(35, _cts.Token).ConfigureAwait(false);
                        using Image<Bgra32> confirm = NativeCapture.CaptureRegion(_target, false);
                        if (confirm != null && !IsFrameSettled(frame, confirm))
                        {
                            frame.Dispose();
                            await Task.Delay(45, _cts.Token).ConfigureAwait(false);
                            continue;
                        }

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

        private static bool IsFrameSettled(Image<Bgra32> first, Image<Bgra32> second)
        {
            if (first.Width != second.Width || first.Height != second.Height) return false;
            long diff = 0;
            long count = 0;
            int stride = Math.Max(4, Math.Min(first.Width, first.Height) / 120);
            first.ProcessPixelRows(second, (a, b) =>
            {
                for (int y = 0; y < a.Height; y += stride)
                {
                    Span<Bgra32> rowA = a.GetRowSpan(y);
                    Span<Bgra32> rowB = b.GetRowSpan(y);
                    for (int x = 0; x < a.Width; x += stride)
                    {
                        diff += Math.Abs(rowA[x].R - rowB[x].R);
                        count++;
                    }
                }
            });
            return count == 0 || (double)diff / count < 2.5;
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
            try { if (_producer != null) await _producer.ConfigureAwait(false); } catch { }
            try { if (_consumer != null) await _consumer.ConfigureAwait(false); } catch { }
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
        private const double MaxAverageDiff = 30.0;
        private const double MaxRefinedDiff = 16.0;
        private const int BandHeightPixels = 257;
        private const long MaxCompositePixels = 180L * 1024L * 1024L;

        private readonly List<ScrollSegment> _segments = new List<ScrollSegment>();
        private SampleFrame _previousSample;
        private byte[] _previousBand;
        private int _bandWidth;
        private int _bandHeight;
        private int _offsetX;
        private int _offsetY;
        private int _frameWidth;
        private int _frameHeight;
        private bool _disposed;

        private Image<Bgra32> _firstFrame;
        private Image<Bgra32> _lastFrame;
        private bool _viewportDetected;
        private Rectangle _viewport;

        public int AcceptedFrames { get; private set; }

        public ScrollFrameStatus AddFrame(Image<Bgra32> frame)
        {
            if (frame == null) return ScrollFrameStatus.Rejected;

            try
            {
                if (AcceptedFrames == 0)
                {
                    _frameWidth = frame.Width;
                    _frameHeight = frame.Height;
                    _firstFrame = frame.Clone(x => { });
                    _lastFrame = frame.Clone(x => { });
                    _previousSample = SampleFrame.Create(frame, new Rectangle(0, 0, frame.Width, frame.Height));
                    _previousBand = BuildBand(frame, new Rectangle(0, 0, frame.Width, frame.Height));
                    AcceptedFrames = 1;
                    return ScrollFrameStatus.Accepted;
                }

                if (frame.Width != _frameWidth || frame.Height != _frameHeight) return ScrollFrameStatus.Rejected;

                Rectangle safeSearchArea = _viewportDetected ? _viewport : new Rectangle(0, 0, _frameWidth, _frameHeight);
                SampleFrame currentSample = SampleFrame.Create(frame, safeSearchArea);
                MovementEstimate estimate = EstimateMovement(_previousSample, currentSample);

                if (!estimate.IsReliable)
                {
                    currentSample.Dispose();
                    return ScrollFrameStatus.Rejected;
                }

                byte[] currentBand = BuildBand(frame, safeSearchArea);
                estimate = RefineMovement(_previousBand, currentBand, estimate);
                
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

                if (!_viewportDetected)
                {
                    _viewport = DetectViewport(_firstFrame, frame);
                    _viewportDetected = true;
                    _segments.Clear();
                    _segments.Add(new ScrollSegment(_firstFrame.Clone(ctx => ctx.Crop(_viewport)), 0, 0));
                    
                    currentSample.Dispose();
                    currentSample = SampleFrame.Create(frame, _viewport);
                    currentBand = BuildBand(frame, _viewport);
                    
                    _previousSample.Dispose();
                    _previousSample = SampleFrame.Create(_firstFrame, _viewport);
                    _previousBand = BuildBand(_firstFrame, _viewport);
                    estimate = EstimateMovement(_previousSample, currentSample);
                    estimate = RefineMovement(_previousBand, currentBand, estimate);
                }

                _offsetX += estimate.DeltaX;
                _offsetY += estimate.DeltaY;
                AddVisibleStrips(frame, _offsetX, _offsetY, estimate.DeltaX, estimate.DeltaY);

                _previousSample.Dispose();
                _previousSample = currentSample;
                _previousBand = currentBand;

                _lastFrame?.Dispose();
                _lastFrame = frame.Clone(x => { });

                AcceptedFrames++;
                return ScrollFrameStatus.Accepted;
            }
            finally
            {
                frame.Dispose();
            }
        }

        private Rectangle DetectViewport(Image<Bgra32> a, Image<Bgra32> b)
        {
            int top = 0, bottom = a.Height - 1;
            int left = 0, right = a.Width - 1;

            bool RowMatch(int y)
            {
                long diff = 0;
                a.ProcessPixelRows(b, (aa, bb) =>
                {
                    var spanA = aa.GetRowSpan(y);
                    var spanB = bb.GetRowSpan(y);
                    for (int x = 0; x < a.Width; x += 4)
                        diff += Math.Abs(spanA[x].R - spanB[x].R);
                });
                return diff < a.Width * 2;
            }

            bool ColMatch(int x, int t, int bot)
            {
                long diff = 0;
                a.ProcessPixelRows(b, (aa, bb) =>
                {
                    for (int y = t; y <= bot; y += 4)
                    {
                        diff += Math.Abs(aa.GetRowSpan(y)[x].R - bb.GetRowSpan(y)[x].R);
                    }
                });
                return diff < (bot - t) * 2;
            }

            for (int y = 0; y < a.Height; y++) if (!RowMatch(y)) { top = y; break; }
            for (int y = a.Height - 1; y > top; y--) if (!RowMatch(y)) { bottom = y; break; }
            for (int x = 0; x < a.Width; x++) if (!ColMatch(x, top, bottom)) { left = x; break; }
            for (int x = a.Width - 1; x > left; x--) if (!ColMatch(x, top, bottom)) { right = x; break; }

            int safeTop = a.Height / 5;
            int safeBottom = a.Height - a.Height / 10 - 1;
            int safeLeft = a.Width / 10;
            int safeRight = a.Width - a.Width / 10 - 1;

            if (bottom - top < a.Height / 3) { top = safeTop; bottom = safeBottom; }
            if (right - left < a.Width / 3) { left = safeLeft; right = safeRight; }

            return new Rectangle(left, top, right - left + 1, bottom - top + 1);
        }

        public Image<Bgra32> BuildImage(IProgress<double> progress = null)
        {
            if (AcceptedFrames < 2 || !_viewportDetected) return null;

            int minX = int.MaxValue, minY = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue;
            foreach (var segment in _segments)
            {
                minX = Math.Min(minX, segment.X);
                minY = Math.Min(minY, segment.Y);
                maxX = Math.Max(maxX, segment.X + segment.Image.Width);
                maxY = Math.Max(maxY, segment.Y + segment.Image.Height);
            }

            int vpW = maxX - minX;
            int vpH = maxY - minY;
            int width = _frameWidth;
            int height = vpH + _viewport.Top + (_frameHeight - _viewport.Bottom);

            long totalPixels = (long)width * height;
            if (width <= 0 || height <= 0 || totalPixels > MaxCompositePixels) return null;

            var result = new Image<Bgra32>(width, height);

            var header = _firstFrame.Clone(c => c.Crop(new Rectangle(0, 0, _frameWidth, _viewport.Top)));
            var footer = _lastFrame.Clone(c => c.Crop(new Rectangle(0, _viewport.Bottom, _frameWidth, _frameHeight - _viewport.Bottom)));
            var leftBar = _firstFrame.Clone(c => c.Crop(new Rectangle(0, _viewport.Top, _viewport.Left, _viewport.Height)));
            var rightBar = _firstFrame.Clone(c => c.Crop(new Rectangle(_viewport.Right, _viewport.Top, _frameWidth - _viewport.Right, _viewport.Height)));

            result.Mutate(ctx => 
            {
                ctx.DrawImage(header, new Point(0, 0), 1f);
                ctx.DrawImage(footer, new Point(0, height - footer.Height), 1f);
            });

            int currentY = _viewport.Top;
            while (currentY < height - footer.Height)
            {
                result.Mutate(ctx => ctx.DrawImage(leftBar, new Point(0, currentY), 1f));
                result.Mutate(ctx => ctx.DrawImage(rightBar, new Point(_viewport.Right, currentY), 1f));
                currentY += _viewport.Height;
            }

            int count = 0;
            foreach (var segment in _segments)
            {
                result.Mutate(ctx => ctx.DrawImage(segment.Image, new Point(segment.X - minX + _viewport.Left, segment.Y - minY + _viewport.Top), 1f));
                count++;
                progress?.Report((double)count / _segments.Count);
            }

            header.Dispose(); footer.Dispose(); leftBar.Dispose(); rightBar.Dispose();
            return result;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _previousSample?.Dispose();
            _firstFrame?.Dispose();
            _lastFrame?.Dispose();
            foreach (var segment in _segments) segment.Image.Dispose();
            _segments.Clear();
        }

        private void AddVisibleStrips(Image<Bgra32> frame, int offsetX, int offsetY, int deltaX, int deltaY)
        {
            int absY = Math.Abs(deltaY);
            int absX = Math.Abs(deltaX);

            if (absY >= absX && absY >= MinMovementPixels)
            {
                int h = Math.Min(absY, _viewport.Height);
                Rectangle crop = deltaY > 0
                    ? new Rectangle(_viewport.X, _viewport.Bottom - h, _viewport.Width, h)
                    : new Rectangle(_viewport.X, _viewport.Top, _viewport.Width, h);
                int y = deltaY > 0 ? offsetY + _viewport.Height - h : offsetY;
                _segments.Add(new ScrollSegment(frame.Clone(ctx => ctx.Crop(crop)), offsetX, y));
            }
            else if (absX > absY && absX >= MinMovementPixels)
            {
                int w = Math.Min(absX, _viewport.Width);
                Rectangle crop = deltaX > 0
                    ? new Rectangle(_viewport.Right - w, _viewport.Top, w, _viewport.Height)
                    : new Rectangle(_viewport.X, _viewport.Top, w, _viewport.Height);
                int x = deltaX > 0 ? offsetX + _viewport.Width - w : offsetX;
                _segments.Add(new ScrollSegment(frame.Clone(ctx => ctx.Crop(crop)), x, offsetY));
            }
        }

        private static MovementEstimate EstimateMovement(SampleFrame previous, SampleFrame current)
        {
            if (previous == null || current == null || previous.Width != current.Width || previous.Height != current.Height)
                return MovementEstimate.Failed;

            int width = previous.Width;
            int height = previous.Height;
            int maxDx = Math.Max(1, (int)(width * 0.70));
            int maxDy = Math.Max(1, (int)(height * 0.85));
            int minOverlap = Math.Max(32, (width * height) / 10);
            double bestScore = double.MaxValue;
            int bestDx = 0, bestDy = 0;

            for (int dy = -maxDy; dy <= maxDy; dy++)
            {
                for (int dx = -maxDx; dx <= maxDx; dx++)
                {
                    int xStart = Math.Max(0, -dx), yStart = Math.Max(0, -dy);
                    int xEnd = Math.Min(width, width - dx), yEnd = Math.Min(height, height - dy);
                    int overlapW = xEnd - xStart, overlapH = yEnd - yStart;
                    if (overlapW <= 0 || overlapH <= 0 || overlapW * overlapH < minOverlap) continue;

                    double score = AverageDiff(previous, current, dx, dy, xStart, yStart, xEnd, yEnd);
                    if (score < bestScore) { bestScore = score; bestDx = dx; bestDy = dy; }
                }
            }

            int originalDx = (int)Math.Round((double)bestDx * previous.Step);
            int originalDy = (int)Math.Round((double)bestDy * previous.Step);
            return new MovementEstimate(originalDx, originalDy, bestScore <= MaxAverageDiff);
        }

        private static double AverageDiff(SampleFrame previous, SampleFrame current, int dx, int dy, int xStart, int yStart, int xEnd, int yEnd)
        {
            long diff = 0; int count = 0;
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

        private byte[] BuildBand(Image<Bgra32> frame, Rectangle area)
        {
            _bandWidth = area.Width;
            _bandHeight = Math.Min(BandHeightPixels, area.Height);
            byte[] band = new byte[_bandWidth * _bandHeight];
            int top = area.Top + (area.Height - _bandHeight) / 2;
            frame.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < _bandHeight; y++)
                {
                    Span<Bgra32> row = accessor.GetRowSpan(top + y);
                    int target = y * _bandWidth;
                    for (int x = 0; x < _bandWidth; x++)
                    {
                        Bgra32 px = row[area.Left + x];
                        band[target + x] = (byte)((px.R * 30 + px.G * 59 + px.B * 11) / 100);
                    }
                }
            });
            return band;
        }

        private MovementEstimate RefineMovement(byte[] previous, byte[] current, MovementEstimate coarse)
        {
            if (previous == null || current == null || previous.Length != current.Length || _bandWidth <= 0 || _bandHeight <= 0)
                return coarse;

            int yRadius = Math.Min(24, Math.Max(8, _bandWidth / 160 + 4));
            int xRadius = 4;
            const int stride = 3;
            int bestDx = coarse.DeltaX, bestDy = coarse.DeltaY;
            double bestScore = double.MaxValue;

            for (int dy = coarse.DeltaY - yRadius; dy <= coarse.DeltaY + yRadius; dy++)
            {
                for (int dx = coarse.DeltaX - xRadius; dx <= coarse.DeltaX + xRadius; dx++)
                {
                    double score = BandDiff(previous, current, dx, dy, stride);
                    if (score < bestScore) { bestScore = score; bestDx = dx; bestDy = dy; }
                }
            }

            return new MovementEstimate(bestDx, bestDy, bestScore <= MaxRefinedDiff);
        }

        private double BandDiff(byte[] previous, byte[] current, int dx, int dy, int stride)
        {
            int xStart = Math.Max(0, -dx), yStart = Math.Max(0, -dy);
            int xEnd = Math.Min(_bandWidth, _bandWidth - dx), yEnd = Math.Min(_bandHeight, _bandHeight - dy);
            if (xEnd - xStart < 8 || yEnd - yStart < 8) return double.MaxValue;

            long diff = 0; int count = 0;
            for (int y = yStart; y < yEnd; y += stride)
            {
                int previousRow = (y + dy) * _bandWidth;
                int currentRow = y * _bandWidth;
                for (int x = xStart; x < xEnd; x += stride)
                {
                    diff += Math.Abs(previous[previousRow + x + dx] - current[currentRow + x]);
                    count++;
                }
            }
            return count == 0 ? double.MaxValue : (double)diff / count;
        }
    }

    internal sealed class ScrollSegment
    {
        public ScrollSegment(Image<Bgra32> image, int x, int y) { Image = image; X = x; Y = y; }
        public Image<Bgra32> Image { get; }
        public int X { get; }
        public int Y { get; }
    }

    internal readonly struct MovementEstimate
    {
        public MovementEstimate(int deltaX, int deltaY, bool reliable) { DeltaX = deltaX; DeltaY = deltaY; IsReliable = reliable; }
        public int DeltaX { get; }
        public int DeltaY { get; }
        public bool IsReliable { get; }
        public static MovementEstimate Failed => new MovementEstimate(0, 0, false);
    }

    internal sealed class SampleFrame : IDisposable
    {
        private SampleFrame(byte[] gray, int width, int height, int step) { Gray = gray; Width = width; Height = height; Step = step; }
        public byte[] Gray { get; private set; }
        public int Width { get; }
        public int Height { get; }
        public int Step { get; }

        public static SampleFrame Create(Image<Bgra32> image, Rectangle area)
        {
            int contentWidth = area.Width > 0 ? area.Width : image.Width;
            int contentHeight = area.Height > 0 ? area.Height : image.Height;
            
            if (area.Width == 0)
            {
                int marginX = Math.Max(0, image.Width / 10);
                int top = Math.Max(0, image.Height / 5);
                int bottomMargin = Math.Max(0, image.Height / 10);
                area = new Rectangle(marginX, top, Math.Max(1, image.Width - marginX * 2), Math.Max(1, image.Height - top - bottomMargin));
                contentWidth = area.Width;
                contentHeight = area.Height;
            }

            int step = Math.Max(1, Math.Max(contentWidth / 180, contentHeight / 140));
            int sampleWidth = Math.Max(1, contentWidth / step);
            int sampleHeight = Math.Max(1, contentHeight / step);
            byte[] gray = new byte[sampleWidth * sampleHeight];

            image.ProcessPixelRows(accessor =>
            {
                for (int sy = 0; sy < sampleHeight; sy++)
                {
                    int sourceY = Math.Min(image.Height - 1, area.Top + sy * step);
                    Span<Bgra32> row = accessor.GetRowSpan(sourceY);
                    int targetRow = sy * sampleWidth;
                    for (int sx = 0; sx < sampleWidth; sx++)
                    {
                        int sourceX = Math.Min(image.Width - 1, area.Left + sx * step);
                        Bgra32 px = row[sourceX];
                        gray[targetRow + sx] = (byte)((px.R * 30 + px.G * 59 + px.B * 11) / 100);
                    }
                }
            });

            return new SampleFrame(gray, sampleWidth, sampleHeight, step);
        }

        public void Dispose() { Gray = null; }
    }
}
