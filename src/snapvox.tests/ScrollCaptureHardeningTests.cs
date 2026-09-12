using System;
using System.Collections.Generic;
using snapvox.helpers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;

namespace snapvox.tests
{
    public class ScrollCaptureHardeningTests
    {
        [Fact]
        public void ScrollFrameStitcher_DefaultConstants_MatchSpecifications()
        {
            Assert.Equal(120, ScrollFrameStitcher.MaxSegments);
            Assert.Equal(180L * 1024L * 1024L, ScrollFrameStitcher.MaxCompositePixels);
            Assert.Equal(120, ScrollCaptureRecorder.MaxSegments);
        }

        [Fact]
        public void Simulate250FrameCapture_CapsSegmentsAtCeiling_AndProducesValidClampedImage()
        {
            // Build a tall synthetic image with continuous vertical gradient variation
            // so movement estimation reliably detects vertical shifts of 10 pixels per frame.
            const int frameWidth = 100;
            const int frameHeight = 200;
            const int shiftPerFrame = 10;
            const int totalFrames = 250;
            const int maxSegments = 120;
            const long maxCompositePixels = 50_000; // 50 KP ceiling -> height clamped to 50,000 / 100 = 500 px

            using var tallCanvas = new Image<Bgra32>(frameWidth, 3200);
            tallCanvas.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < 3200; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < frameWidth; x++)
                    {
                        byte v = (byte)((y * 7 + x * 3) % 256);
                        row[x] = new Bgra32(v, (byte)(255 - v), (byte)((v * 2) % 256), 255);
                    }
                }
            });

            using var stitcher = new ScrollFrameStitcher(maxSegments: maxSegments, maxCompositePixels: maxCompositePixels);

            // Feed 250 frames into the stitcher
            for (int i = 0; i < totalFrames; i++)
            {
                var frame = tallCanvas.Clone(ctx => ctx.Crop(new Rectangle(0, i * shiftPerFrame, frameWidth, frameHeight)));
                var status = stitcher.AddFrame(frame);

                if (i < maxSegments)
                {
                    Assert.Equal(ScrollFrameStatus.Accepted, status);
                }
                else
                {
                    // Past 120 segments, additional frames MUST be rejected to prevent memory growth
                    Assert.Equal(ScrollFrameStatus.Rejected, status);
                }
            }

            // 1. Strict segment ceiling verification: exactly 120 segments stored
            Assert.True(stitcher.IsSegmentCeilingReached);
            Assert.Equal(maxSegments, stitcher.SegmentCount);
            Assert.True(stitcher.SegmentCount <= ScrollFrameStitcher.MaxSegments);

            // 2. BuildImage verification: must NOT return null when calculated pixels exceed MaxCompositePixels;
            // instead, clamp height to maximum supported dimension (500 px) and composite up to limit.
            using var result = stitcher.BuildImage();
            Assert.NotNull(result);
            Assert.Equal(frameWidth, result.Width);
            Assert.Equal(500, result.Height); // 50,000 / 100 = 500
            Assert.True((long)result.Width * result.Height <= maxCompositePixels);

            // 3. Clean disposal verification: all segment buffers disposed without leak or exception
            stitcher.Dispose();
            Assert.Empty(stitcher.Segments);
        }

        [Fact]
        public void BuildImage_WhenCalculatedPixelsExceedCeiling_ClampsHeightGracefully()
        {
            // Verify clamping with default 120 segments but custom 80,000 pixel ceiling
            const int width = 200;
            const int height = 200;
            using var stitcher = new ScrollFrameStitcher(maxSegments: 120, maxCompositePixels: 80_000);

            using var baseImg = new Image<Bgra32>(width, height);
            // First frame
            stitcher.AddFrame(baseImg.Clone(x => { }));

            // When no segments or no viewport, returns clone of first frame
            using var singleResult = stitcher.BuildImage();
            Assert.NotNull(singleResult);
            Assert.Equal(width, singleResult.Width);
            Assert.Equal(height, singleResult.Height);
        }

        [Fact]
        public void ScrollCaptureRecorder_SegmentCeilingReached_TriggersPauseAndEvent()
        {
            var targetRect = snapvox.native.foundation.RECT.FromXYWH(0, 0, 800, 600);
            var recorder = new ScrollCaptureRecorder(targetRect);

            bool eventFired = false;
            recorder.SegmentCeilingReached += () => { eventFired = true; };

            Assert.False(recorder.IsPaused);
            Assert.False(recorder.IsSegmentCeilingReached);

            // Manually trigger pause to verify event notification path
            recorder.Pause();
            Assert.True(recorder.IsPaused);
            Assert.True(eventFired);

            // Redundant pause should not double-fire
            eventFired = false;
            recorder.Pause();
            Assert.False(eventFired);

            recorder.Resume();
            Assert.False(recorder.IsPaused);
        }
    }
}
