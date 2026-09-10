using System;
using Avalonia;
using snapvox.editor.Services;
using Xunit;

namespace snapvox.tests
{
    public class EditorZoomEngineTests
    {
        [Fact]
        public void ZoomFactor_ClampsToValidRange()
        {
            var engine = new EditorZoomEngine();

            engine.ZoomFactor = 0.01;
            Assert.Equal(EditorZoomEngine.MinZoomFactor, engine.ZoomFactor);

            engine.ZoomFactor = 10.0;
            Assert.Equal(EditorZoomEngine.MaxZoomFactor, engine.ZoomFactor);

            engine.ZoomFactor = 2.5;
            Assert.Equal(2.5, engine.ZoomFactor);
        }

        [Fact]
        public void StepInterpolation_AdvancesCorrectly()
        {
            // Initial step: 20% of difference
            bool step1 = EditorZoomEngine.StepInterpolation(1.0, 2.0, out double next1, out bool completed1);
            Assert.True(step1);
            Assert.False(completed1);
            Assert.Equal(1.20, next1, 2);

            // Convergence step: diff < 0.0005 snaps and completes
            bool step2 = EditorZoomEngine.StepInterpolation(1.9998, 2.0, out double next2, out bool completed2);
            Assert.True(step2);
            Assert.True(completed2);
            Assert.Equal(2.0, next2);
        }

        [Fact]
        public void CalculateViewportPadding_CentersSmallImages_ProvidesOverscrollForLarge()
        {
            // Small image: centered
            EditorZoomEngine.CalculateViewportPadding(
                zoom: 1.0,
                imageWidth: 400,
                imageHeight: 300,
                viewportWidth: 800,
                viewportHeight: 600,
                windowWidth: 900,
                windowHeight: 700,
                out double padX,
                out double padY,
                out double scaledW,
                out double scaledH);

            Assert.Equal(400.0, scaledW);
            Assert.Equal(300.0, scaledH);
            Assert.Equal(200.0, padX);
            Assert.Equal(150.0, padY);

            // Large image: overscroll padding
            EditorZoomEngine.CalculateViewportPadding(
                zoom: 1.0,
                imageWidth: 1000,
                imageHeight: 800,
                viewportWidth: 800,
                viewportHeight: 600,
                windowWidth: 900,
                windowHeight: 700,
                out padX,
                out padY,
                out scaledW,
                out scaledH);

            Assert.Equal(1000.0, scaledW);
            Assert.Equal(800.0, scaledH);
            Assert.Equal(EditorZoomEngine.OverscrollPadding, padX);
            Assert.Equal(EditorZoomEngine.OverscrollPadding, padY);
        }

        [Fact]
        public void CalculateFocalPoints_ClampsCorrectly()
        {
            EditorZoomEngine.CalculateFocalPoints(
                pointerInScroller: new Point(400, 300),
                scrollOffsetX: 0,
                scrollOffsetY: 0,
                zoom: 1.0,
                imageWidth: 500,
                imageHeight: 500,
                viewportWidth: 800,
                viewportHeight: 600,
                currentPadX: 100,
                currentPadY: 100,
                out Point vpPoint,
                out Point unscaledPoint);

            Assert.Equal(400.0, vpPoint.X);
            Assert.Equal(300.0, vpPoint.Y);
            // (0 + 400 - 100) / 1.0 = 300
            Assert.Equal(300.0, unscaledPoint.X);
            // (0 + 300 - 100) / 1.0 = 200
            Assert.Equal(200.0, unscaledPoint.Y);

            // With scroll offset:
            EditorZoomEngine.CalculateFocalPoints(
                pointerInScroller: new Point(400, 300),
                scrollOffsetX: 50,
                scrollOffsetY: 80,
                zoom: 1.0,
                imageWidth: 500,
                imageHeight: 500,
                viewportWidth: 800,
                viewportHeight: 600,
                currentPadX: 100,
                currentPadY: 100,
                out Point vpPoint2,
                out Point unscaledPoint2);

            Assert.Equal(400.0, vpPoint2.X);
            Assert.Equal(300.0, vpPoint2.Y);
            // (50 + 400 - 100) / 1.0 = 350
            Assert.Equal(350.0, unscaledPoint2.X);
            // (80 + 300 - 100) / 1.0 = 280
            Assert.Equal(280.0, unscaledPoint2.Y);
        }

        [Fact]
        public void CalculateDesiredOffset_FitsInViewportZeroOffset()
        {
            var offset = EditorZoomEngine.CalculateDesiredOffset(
                zoom: 1.0,
                padX: 200,
                padY: 150,
                totalW: 800,
                totalH: 600,
                viewportWidth: 800,
                viewportHeight: 600,
                focalVp: null,
                focalUnscaled: null);

            Assert.Equal(0.0, offset.X);
            Assert.Equal(0.0, offset.Y);
        }
    }
}
