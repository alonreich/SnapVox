using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Input;
using snapvox.editor.Services;
using Xunit;

namespace snapvox.tests
{
    public class VectorSnappingEngineTests
    {
        [Fact]
        public void SnapToEightDirectionsStrict_SnapsToAngles()
        {
            var anchor = new Point(100, 100);

            // Directly right: 0 deg
            var p0 = new Point(200, 100);
            var res0 = VectorSnappingEngine.SnapToEightDirectionsStrict(p0, anchor);
            Assert.Equal(200, res0.X, 2);
            Assert.Equal(100, res0.Y, 2);

            // 40 degrees: should snap to 45 deg
            double dist = 100.0;
            double rad40 = 40.0 * Math.PI / 180.0;
            var p40 = new Point(anchor.X + Math.Cos(rad40) * dist, anchor.Y + Math.Sin(rad40) * dist);
            var res40 = VectorSnappingEngine.SnapToEightDirectionsStrict(p40, anchor);
            double rad45 = 45.0 * Math.PI / 180.0;
            Assert.Equal(anchor.X + Math.Cos(rad45) * dist, res40.X, 2);
            Assert.Equal(anchor.Y + Math.Sin(rad45) * dist, res40.Y, 2);

            // Short length (< 1px) returns proposed unchanged
            var pShort = new Point(100.5, 100.5);
            var resShort = VectorSnappingEngine.SnapToEightDirectionsStrict(pShort, anchor);
            Assert.Equal(pShort.X, resShort.X);
            Assert.Equal(pShort.Y, resShort.Y);
        }

        [Fact]
        public void SoftSnapToEightDirections_HonorsTolerance()
        {
            var anchor = new Point(0, 0);
            double dist = 50.0;

            // Angle 0.02 rad off 0 deg (within 0.04 tolerance) -> snaps to 0 deg
            var pNear = new Point(Math.Cos(0.02) * dist, Math.Sin(0.02) * dist);
            var resNear = VectorSnappingEngine.SoftSnapToEightDirections(pNear, anchor);
            Assert.Equal(dist, resNear.X, 2);
            Assert.Equal(0, resNear.Y, 2);

            // Angle 0.10 rad off 0 deg (outside 0.04 tolerance) -> unchanged
            var pFar = new Point(Math.Cos(0.10) * dist, Math.Sin(0.10) * dist);
            var resFar = VectorSnappingEngine.SoftSnapToEightDirections(pFar, anchor);
            Assert.Equal(pFar.X, resFar.X, 4);
            Assert.Equal(pFar.Y, resFar.Y, 4);

            // Distance < 10px -> unchanged
            var pClose = new Point(5, 5);
            var resClose = VectorSnappingEngine.SoftSnapToEightDirections(pClose, anchor);
            Assert.Equal(pClose.X, resClose.X);
            Assert.Equal(pClose.Y, resClose.Y);
        }

        [Fact]
        public void PullBackFromTarget_AppliesGap()
        {
            var anchor = new Point(0, 0);
            var target = new Point(100, 0);

            // Default overload uses VectorSnapGap (12.0)
            var defaultResult = VectorSnappingEngine.PullBackFromTarget(anchor, target);
            Assert.Equal(88.0, defaultResult.X, 3);
            Assert.Equal(0.0, defaultResult.Y, 3);

            // Custom gap overload (12.0)
            var result = VectorSnappingEngine.PullBackFromTarget(anchor, target, 12.0);
            Assert.Equal(88.0, result.X, 3);
            Assert.Equal(0.0, result.Y, 3);

            // Inside gap (< 12px): unchanged
            var shortTarget = new Point(10.0, 0);
            var shortResult = VectorSnappingEngine.PullBackFromTarget(anchor, shortTarget, 12.0);
            Assert.Equal(10.0, shortResult.X);
            Assert.Equal(0.0, shortResult.Y);
        }

        [Fact]
        public void ApplyVectorConstraints_RespectsModifiersAndTargets()
        {
            var anchor = new Point(0, 0);
            var proposed = new Point(100, 10);
            var targets = new List<Point> { new Point(100, 12) };

            // Snapping disabled -> proposed unchanged
            var resDisabled = VectorSnappingEngine.ApplyVectorConstraints(proposed, anchor, KeyModifiers.None, false, true, targets);
            Assert.Equal(proposed, resDisabled);

            // Alt modifier -> bypass returns proposed unchanged
            var resAlt = VectorSnappingEngine.ApplyVectorConstraints(proposed, anchor, KeyModifiers.Alt, true, true, targets);
            Assert.Equal(proposed, resAlt);

            // Shift modifier -> strict 8 directions
            var resShift = VectorSnappingEngine.ApplyVectorConstraints(proposed, anchor, KeyModifiers.Shift, true, true, targets);
            Assert.NotEqual(proposed, resShift);

            // Target snap within threshold -> pulled back by VectorSnapGap (12.0)
            var resTarget = VectorSnappingEngine.ApplyVectorConstraints(proposed, anchor, KeyModifiers.None, true, true, targets);
            double distToTarget = Math.Sqrt(Math.Pow(resTarget.X - 100, 2) + Math.Pow(resTarget.Y - 12, 2));
            Assert.Equal(12.0, distToTarget, 2);
            Assert.Equal(VectorSnappingEngine.VectorSnapGap, distToTarget, 2);
        }

        [Fact]
        public void TrySnapResizeEdge_SnapsWithinThreshold()
        {
            var targets = new List<ShapeSnapTarget>
            {
                new ShapeSnapTarget(new Rect(100, 100, 200, 200)) // Left=100, Center=200, Right=300, Top=100, Center=200, Bottom=300
            };

            // 103 is within 6px of 100
            bool snapped = VectorSnappingEngine.TrySnapResizeEdge(103, targets, vertical: false, out double snapVal);
            Assert.True(snapped);
            Assert.Equal(100.0, snapVal);

            // 108 is outside 6px of 100
            bool notSnapped = VectorSnappingEngine.TrySnapResizeEdge(108, targets, vertical: false, out snapVal);
            Assert.False(notSnapped);
            Assert.Equal(108.0, snapVal);
        }

        [Fact]
        public void GeneratePerimeterPoints_RoundAndRectangular()
        {
            var roundBounds = new Rect(100, 100, 50, 50); // cx=125, cy=125, rx=25, ry=25
            var roundPoints = new List<Point>();
            // Active center near top edge
            VectorSnappingEngine.GeneratePerimeterPoints(roundBounds, isRound: true, activeCenterX: 125, activeCenterY: 80, (x, y) => roundPoints.Add(new Point(x, y)));
            Assert.Equal(3, roundPoints.Count);
            // Middle point should be top center (125, 100)
            Assert.Equal(125.0, roundPoints[1].X, 2);
            Assert.Equal(100.0, roundPoints[1].Y, 2);

            var rectBounds = new Rect(200, 200, 60, 40);
            var rectPoints = new List<Point>();
            // Active center near bottom edge
            VectorSnappingEngine.GeneratePerimeterPoints(rectBounds, isRound: false, activeCenterX: 230, activeCenterY: 260, (x, y) => rectPoints.Add(new Point(x, y)));
            Assert.Equal(3, rectPoints.Count);
            // Points should be (Left, Bottom), (Center, Bottom), (Right, Bottom)
            Assert.Equal(200.0, rectPoints[0].X);
            Assert.Equal(240.0, rectPoints[0].Y);
            Assert.Equal(230.0, rectPoints[1].X);
            Assert.Equal(240.0, rectPoints[1].Y);
            Assert.Equal(260.0, rectPoints[2].X);
            Assert.Equal(240.0, rectPoints[2].Y);
        }

        [Fact]
        public void VectorSnappingEngine_GuardsAgainstNonFiniteInputs()
        {
            var nanPoint = new Point(double.NaN, 100);
            var infPoint = new Point(100, double.PositiveInfinity);
            var validPoint = new Point(100, 100);

            // SnapToEightDirectionsStrict should safely return proposed without throwing or NaN propagation
            var res1 = VectorSnappingEngine.SnapToEightDirectionsStrict(nanPoint, validPoint);
            Assert.True(double.IsNaN(res1.X));

            var res2 = VectorSnappingEngine.SnapToEightDirectionsStrict(validPoint, infPoint);
            Assert.Equal(100, res2.X);
            Assert.Equal(100, res2.Y);

            // SoftSnapToEightDirections should safely return proposed
            var res3 = VectorSnappingEngine.SoftSnapToEightDirections(nanPoint, validPoint);
            Assert.True(double.IsNaN(res3.X));

            // PullBackFromTarget should return target safely
            var res4 = VectorSnappingEngine.PullBackFromTarget(validPoint, infPoint);
            Assert.Equal(infPoint.X, res4.X);
            Assert.Equal(infPoint.Y, res4.Y);

            // TrySnapResizeEdge should return false on NaN
            bool snappedEdge = VectorSnappingEngine.TrySnapResizeEdge(double.NaN, new List<ShapeSnapTarget>(), false, out double edgeVal);
            Assert.False(snappedEdge);

            // TrySnapAxis should return false on NaN
            bool snappedAxis = VectorSnappingEngine.TrySnapAxis(false, new Rect(double.NaN, 0, 10, 10), new List<ShapeSnapTarget>(), out _, out _);
            Assert.False(snappedAxis);
        }

        [Fact]
        public void TryFindMatchingSnapPair_DetectsDirectDockingPair()
        {
            var recipient = new List<Point>
            {
                new Point(100, 50),
                new Point(100, 100),
                new Point(100, 150)
            };
            var held = new List<Point>
            {
                new Point(100, 100),
                new Point(100, 120),
                new Point(100, 140)
            };

            bool matched = VectorSnappingEngine.TryFindMatchingSnapPair(recipient, held, out var pair);
            Assert.True(matched);
            Assert.Equal(new Point(100, 100), pair.Recipient);
            Assert.Equal(new Point(100, 100), pair.Held);
            Assert.Equal(0.0, pair.Distance, 2);
        }

        [Fact]
        public void TryFindMatchingSnapPair_DetectsAxisAlignmentPair()
        {
            var recipient = new List<Point>
            {
                new Point(200, 50),
                new Point(200, 100),
                new Point(200, 150)
            };
            var held = new List<Point>
            {
                new Point(150, 100),
                new Point(150, 120),
                new Point(150, 140)
            };

            // Held dot (150, 100) aligns on Y=100 with recipient dot (200, 100) across a 50px gap
            bool matched = VectorSnappingEngine.TryFindMatchingSnapPair(recipient, held, out var pair);
            Assert.True(matched);
            Assert.Equal(new Point(200, 100), pair.Recipient);
            Assert.Equal(new Point(150, 100), pair.Held);
            Assert.Equal(50.0, pair.Distance, 2);
        }

        [Fact]
        public void TryFindMatchingSnapPair_ReturnsFalseWhenNoMatch()
        {
            var recipient = new List<Point>
            {
                new Point(200, 50),
                new Point(200, 100)
            };
            var held = new List<Point>
            {
                new Point(150, 77),
                new Point(150, 123)
            };

            bool matched = VectorSnappingEngine.TryFindMatchingSnapPair(recipient, held, out _);
            Assert.False(matched);
        }

        [Fact]
        public void GenerateFacingPerimeterPoints_GeneratesFacingPointsOnBothShapes()
        {
            var heldBounds = new Rect(0, 0, 50, 50);       // Center (25, 25)
            var recipientBounds = new Rect(100, 0, 50, 50); // Center (125, 25)

            var recPoints = new List<Point>();
            var heldPoints = new List<Point>();

            VectorSnappingEngine.GenerateFacingPerimeterPoints(
                heldBounds, isHeldRound: false,
                recipientBounds, isRecipientRound: false,
                (rx, ry) => recPoints.Add(new Point(rx, ry)),
                (hx, hy) => heldPoints.Add(new Point(hx, hy)));

            Assert.Equal(3, recPoints.Count);
            Assert.Equal(3, heldPoints.Count);

            // Recipient's left edge faces held shape (X = 100)
            Assert.All(recPoints, p => Assert.Equal(100.0, p.X));

            // Held shape's right edge faces recipient shape (X = 50)
            Assert.All(heldPoints, p => Assert.Equal(50.0, p.X));
        }

        [Fact]
        public void TryFindMatchingSnapPair_GuardsAgainstInvalidInputs()
        {
            bool matchNull = VectorSnappingEngine.TryFindMatchingSnapPair(null, null, out _);
            Assert.False(matchNull);

            bool matchEmpty = VectorSnappingEngine.TryFindMatchingSnapPair(new List<Point>(), new List<Point>(), out _);
            Assert.False(matchEmpty);

            var nanPoints = new List<Point> { new Point(double.NaN, double.NaN) };
            bool matchNan = VectorSnappingEngine.TryFindMatchingSnapPair(nanPoints, nanPoints, out _);
            Assert.False(matchNan);

            // GenerateFacingPerimeterPoints with NaN bounds safely does nothing
            var dummyPoints = new List<Point>();
            VectorSnappingEngine.GenerateFacingPerimeterPoints(
                new Rect(double.NaN, 0, 10, 10), false,
                new Rect(0, 0, 10, 10), false,
                (x, y) => dummyPoints.Add(new Point(x, y)),
                (x, y) => dummyPoints.Add(new Point(x, y)));
            Assert.Empty(dummyPoints);
        }

        [Fact]
        public void GetCircleKeypoints_GeneratesNineContourPoints()
        {
            var bounds = new Rect(100, 100, 100, 100); // cx=150, cy=150, rx=50, ry=50
            var points = new List<Point>();
            VectorSnappingEngine.GetCircleKeypoints(bounds, points);

            Assert.Equal(9, points.Count);

            // Center is (150, 150)
            Assert.Equal(new Point(150, 150), points[8]);

            // Tangents: Top=(150, 100), Right=(200, 150), Bottom=(150, 200), Left=(100, 150)
            Assert.Equal(new Point(150, 100), points[0]);
            Assert.Equal(new Point(200, 150), points[1]);
            Assert.Equal(new Point(150, 200), points[2]);
            Assert.Equal(new Point(100, 150), points[3]);

            // 45-degree points
            const double cos45 = 0.7071067811865475;
            Assert.Equal(150 + 50 * cos45, points[4].X, 2);
            Assert.Equal(150 - 50 * cos45, points[4].Y, 2);
        }

        [Fact]
        public void TrySnapKeypointsToTargets_SnapsClosestPointWithinThreshold()
        {
            var moving = new List<Point>
            {
                new Point(50, 50),
                new Point(102, 103)
            };
            var targets = new List<Point>
            {
                new Point(100, 100),
                new Point(500, 500)
            };

            // Point (102, 103) is ~3.6px away from (100, 100) -> should snap with dx = -2, dy = -3
            bool snapped = VectorSnappingEngine.TrySnapKeypointsToTargets(
                moving, targets, out double snapDx, out double snapDy, out Point snappedMoving, out Point snappedTarget);

            Assert.True(snapped);
            Assert.Equal(-2.0, snapDx, 2);
            Assert.Equal(-3.0, snapDy, 2);
            Assert.Equal(new Point(102, 103), snappedMoving);
            Assert.Equal(new Point(100, 100), snappedTarget);
        }

        [Fact]
        public void TrySnapKeypointSingleAxis_SnapsKeypointToTargetAxis()
        {
            var keypoints = new List<Point>
            {
                new Point(102, 200) // 102 is within 6px of 100
            };
            var targets = new List<ShapeSnapTarget>
            {
                new ShapeSnapTarget(new Rect(100, 0, 50, 50)) // Left = 100
            };

            bool snappedX = VectorSnappingEngine.TrySnapKeypointSingleAxis(
                vertical: false, keypoints, targets, out double delta, out var snapResult);

            Assert.True(snappedX);
            Assert.Equal(-2.0, delta, 2);
            Assert.Equal(100.0, snapResult.Coordinate, 2);
        }

        [Fact]
        public void BuildArrowContourPoints_ReturnsExpected7PointsAndEnclosesArrow()
        {
            var start = new Point(100, 100);
            var end = new Point(300, 100);
            double thickness = 3.0;
            double padding = 2.5;

            var points = snapvox.editor.forms.ImageEditorWindow.BuildArrowContourPoints(start, end, thickness, padding);

            Assert.NotNull(points);
            Assert.Equal(7, points.Count);

            // Tip should extend beyond end by padding along direction
            var tip = points[0];
            Assert.True(tip.X > end.X);
            Assert.Equal(end.Y, tip.Y, 2);

            // Wings should have X behind tip and Y wider than halfShaft
            var wing1 = points[1];
            var wing2 = points[6];
            Assert.True(wing1.X < tip.X);
            Assert.True(wing2.X < tip.X);
            Assert.True(wing1.Y > end.Y);
            Assert.True(wing2.Y < end.Y);

            // Neck points should be between wings and tip on X, with Y = halfShaft
            var neck1 = points[2];
            var neck2 = points[5];
            double expectedHalfShaft = (thickness / 2.0) + padding;
            Assert.Equal(start.Y + expectedHalfShaft, neck1.Y, 2);
            Assert.Equal(start.Y - expectedHalfShaft, neck2.Y, 2);

            // Tail points should extend behind start by padding with Y = halfShaft
            var tail1 = points[3];
            var tail2 = points[4];
            Assert.True(tail1.X < start.X);
            Assert.True(tail2.X < start.X);
            Assert.Equal(start.Y + expectedHalfShaft, tail1.Y, 2);
            Assert.Equal(start.Y - expectedHalfShaft, tail2.Y, 2);
        }

        [Fact]
        public void BuildArrowContourPoints_HandlesZeroLength_ReturnsEmpty()
        {
            var p = new Point(50, 50);
            var points = snapvox.editor.forms.ImageEditorWindow.BuildArrowContourPoints(p, p, 2.0, 2.5);
            Assert.NotNull(points);
            Assert.Empty(points);
        }

        [Theory]
        [InlineData(100, 100, 100, 200)] // Vertical down
        [InlineData(200, 200, 100, 100)] // Diagonal up-left
        [InlineData(300, 100, 100, 100)] // Horizontal left
        public void BuildArrowContourPoints_VariousOrientations_Generates7Points(double sx, double sy, double ex, double ey)
        {
            var start = new Point(sx, sy);
            var end = new Point(ex, ey);
            var points = snapvox.editor.forms.ImageEditorWindow.BuildArrowContourPoints(start, end, 2.0, 2.5);
            Assert.Equal(7, points.Count);
        }

        [Fact]
        public void BuildLineContourPoints_ReturnsExpected14PointsAndEnclosesLine()
        {
            var start = new Point(100, 100);
            var end = new Point(300, 100);
            double thickness = 4.0;
            double padding = 2.5;
            double expectedRadius = (thickness / 2.0) + padding;

            var points = snapvox.editor.forms.ImageEditorWindow.BuildLineContourPoints(start, end, thickness, padding);

            Assert.NotNull(points);
            Assert.Equal(14, points.Count);

            // Apex of end cap should be at end.X + expectedRadius
            var maxEndPt = points.OrderByDescending(p => p.X).First();
            Assert.Equal(end.X + expectedRadius, maxEndPt.X, 2);
            Assert.Equal(end.Y, maxEndPt.Y, 2);

            // Apex of start cap should be at start.X - expectedRadius
            var minStartPt = points.OrderBy(p => p.X).First();
            Assert.Equal(start.X - expectedRadius, minStartPt.X, 2);
            Assert.Equal(start.Y, minStartPt.Y, 2);

            // Verify bounds enclose the thickness and padding
            double maxY = points.Max(p => p.Y);
            double minY = points.Min(p => p.Y);
            Assert.Equal(start.Y + expectedRadius, maxY, 2);
            Assert.Equal(start.Y - expectedRadius, minY, 2);
        }

        [Fact]
        public void BuildLineContourPoints_HandlesZeroLength_ReturnsEmpty()
        {
            var p = new Point(50, 50);
            var points = snapvox.editor.forms.ImageEditorWindow.BuildLineContourPoints(p, p, 4.0, 2.5);
            Assert.NotNull(points);
            Assert.Empty(points);
        }

        [Theory]
        [InlineData(100, 100, 100, 300)] // Vertical down
        [InlineData(300, 300, 100, 100)] // Diagonal up-left
        [InlineData(300, 100, 100, 100)] // Horizontal left
        [InlineData(150, 250, 350, 150)] // Diagonal arbitrary
        public void BuildLineContourPoints_VariousOrientations_Generates14Points(double sx, double sy, double ex, double ey)
        {
            var start = new Point(sx, sy);
            var end = new Point(ex, ey);
            var points = snapvox.editor.forms.ImageEditorWindow.BuildLineContourPoints(start, end, 4.0, 2.5);
            Assert.Equal(14, points.Count);
        }
    }
}
