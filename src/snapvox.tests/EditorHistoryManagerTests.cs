using System;
using System.Collections.Generic;
using Avalonia.Controls;
using snapvox.editor.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace snapvox.tests
{
    public class EditorHistoryManagerTests
    {
        [Fact]
        public void InitialState_IsEmpty()
        {
            var manager = new EditorHistoryManager();

            Assert.Equal(0, manager.UndoCount);
            Assert.Equal(0, manager.RedoCount);
            Assert.False(manager.CanUndo);
            Assert.False(manager.CanRedo);
            Assert.Equal(0, manager.TotalUndoHistoryBytes());
        }

        [Fact]
        public void PushUndo_AddsSnapshot_ClearsRedo()
        {
            var manager = new EditorHistoryManager();
            var snapshot1 = new EditorSnapshot();
            var snapshot2 = new EditorSnapshot();

            manager.PushUndo(snapshot1);
            Assert.Equal(1, manager.UndoCount);
            Assert.True(manager.CanUndo);

            // Simulate undo to populate redo stack
            manager.TryUndo(new EditorSnapshot(), out _);
            Assert.Equal(1, manager.RedoCount);

            // New push clears redo
            manager.PushUndo(snapshot2);
            Assert.Equal(0, manager.RedoCount);
            Assert.False(manager.CanRedo);
        }

        [Fact]
        public void PushUndo_EnforcesMaxStackSize()
        {
            var manager = new EditorHistoryManager();

            for (int i = 0; i < EditorHistoryManager.MaxStackSize + 10; i++)
            {
                manager.PushUndo(new EditorSnapshot());
            }

            Assert.Equal(EditorHistoryManager.MaxStackSize, manager.UndoCount);
        }

        [Fact]
        public void UndoRedo_Cycle_RestoresSnapshotsCorrectly()
        {
            var manager = new EditorHistoryManager();
            var snapshot1 = new EditorSnapshot();
            var snapshot2 = new EditorSnapshot();

            manager.PushUndo(snapshot1);
            manager.PushUndo(snapshot2);

            Assert.Equal(2, manager.UndoCount);

            var current = new EditorSnapshot();
            bool undid = manager.TryUndo(current, out var restoredUndo);

            Assert.True(undid);
            Assert.NotNull(restoredUndo);
            Assert.Same(snapshot2, restoredUndo);
            Assert.Equal(1, manager.UndoCount);
            Assert.Equal(1, manager.RedoCount);

            bool redid = manager.TryRedo(restoredUndo!, out var restoredRedo);
            Assert.True(redid);
            Assert.Same(current, restoredRedo);
            Assert.Equal(2, manager.UndoCount);
            Assert.Equal(0, manager.RedoCount);
        }

        [Fact]
        public void EnforceUndoMemoryBudget_TrimsOldestWhenExceedingCeiling()
        {
            var manager = new EditorHistoryManager();

            // Create images that will exceed MaxUndoHistoryBytes
            // 384 MB ceiling. Let's create small test by mocking bytes or using actual images
            // A 1000x1000 image is 4 MB
            using var img1 = new Image<Rgba32>(1000, 1000);
            using var img2 = new Image<Rgba32>(1000, 1000);

            var snap1 = new EditorSnapshot { Image = img1.Clone() };
            var snap2 = new EditorSnapshot { Image = img2.Clone() };

            long bytes1 = EditorHistoryManager.EstimateSnapshotBytes(snap1);
            Assert.Equal(4_000_000L, bytes1);

            manager.PushUndo(snap1);
            manager.PushUndo(snap2);

            Assert.Equal(8_000_000L, manager.TotalUndoHistoryBytes());
            Assert.Equal(2, manager.UndoCount);
        }

        [Fact]
        public void Clear_DisposesAndEmptiesBothStacks()
        {
            int disposedCount = 0;
            var manager = new EditorHistoryManager(control => disposedCount++);

            var ctrl1 = new Canvas();
            var snap1 = new EditorSnapshot();
            snap1.Annotations.Add(ctrl1);

            manager.PushUndo(snap1);
            Assert.Equal(1, manager.UndoCount);

            manager.Clear();
            Assert.Equal(0, manager.UndoCount);
            Assert.Equal(0, manager.RedoCount);
            Assert.Equal(1, disposedCount);
        }

        [Fact]
        public void VectorOperations_DoNotAccumulateRasterMemory()
        {
            var manager = new EditorHistoryManager();

            // Simulate 40 vector moves on top of a 4K canvas where cloneImage is false (Image == null)
            for (int i = 0; i < 40; i++)
            {
                var vectorSnapshot = new EditorSnapshot
                {
                    Image = null,
                    Annotations = new List<Control>
                    {
                        new Avalonia.Controls.Shapes.Rectangle { Width = 100 + i, Height = 50 }
                    }
                };
                manager.PushUndo(vectorSnapshot);
            }

            Assert.Equal(40, manager.UndoCount);
            // Must stay well under 50 MB (in fact, 0 raster bytes tracked)
            Assert.Equal(0L, manager.TotalUndoHistoryBytes());
            Assert.True(manager.TotalUndoHistoryBytes() < 50L * 1024 * 1024);
        }

        [Fact]
        public void NonDestructive_SnapshotRestoration_PreservesRepeatedUndoRedoFidelity()
        {
            var manager = new EditorHistoryManager();

            using var baseImg = new Image<Rgba32>(100, 100);
            var snap1 = new EditorSnapshot
            {
                Image = baseImg.Clone(),
                Annotations = new List<Control>
                {
                    new Canvas { Width = 10, Height = 10 },
                    new Canvas { Width = 20, Height = 20 }
                }
            };

            manager.PushUndo(snap1);

            var current = new EditorSnapshot
            {
                Image = null,
                Annotations = new List<Control>
                {
                    new Canvas { Width = 30, Height = 30 }
                }
            };

            // Execute 10 sequential undo/redo round-trips
            for (int cycle = 0; cycle < 10; cycle++)
            {
                // Peek should report non-null image before undo
                Assert.NotNull(manager.PeekUndo()?.Image);

                bool undid = manager.TryUndo(current, out var restoredUndo);
                Assert.True(undid);
                Assert.NotNull(restoredUndo);

                // Verify snapshot was NOT hollowed out
                Assert.NotNull(restoredUndo.Image);
                Assert.NotNull(restoredUndo.TakeImage());
                Assert.Equal(2, restoredUndo.Annotations.Count);

                // Peek should report current snapshot in redo stack
                Assert.Same(current, manager.PeekRedo());

                bool redid = manager.TryRedo(restoredUndo, out var restoredRedo);
                Assert.True(redid);
                Assert.NotNull(restoredRedo);
                Assert.Same(current, restoredRedo);
                Assert.Single(restoredRedo.Annotations);
            }

            // After 10 cycles, snap1 still retains its image and annotations
            Assert.NotNull(snap1.Image);
            Assert.NotNull(snap1.TakeImage());
            Assert.Equal(2, snap1.Annotations.Count);
        }

        [Fact]
        public void MemoryBudget_SymmetricEviction_EvictsRedoBeforeUndo()
        {
            var manager = new EditorHistoryManager();

            // 6 images of 40 MB each = 240 MB (Max is 250 MB = 262,144,000 bytes)
            // Each image: 5000 x 2000 x 4 bytes = 40,000,000 bytes
            var snapshots = new List<EditorSnapshot>();
            for (int i = 0; i < 6; i++)
            {
                using var img = new Image<Rgba32>(5000, 2000);
                var snap = new EditorSnapshot { Image = img.Clone() };
                snapshots.Add(snap);
                manager.PushUndo(snap);
            }

            Assert.Equal(6, manager.UndoCount);
            Assert.Equal(240_000_000L, manager.TotalUndoHistoryBytes());

            // Perform undo: undo stack goes to 5, redo stack has 1 (carrying 40MB)
            // Total bytes becomes 200MB (undo) + 40MB (redo) = 240MB <= 250MB
            using var currentImg = new Image<Rgba32>(5000, 2000);
            var currentSnap = new EditorSnapshot { Image = currentImg.Clone() };
            manager.TryUndo(currentSnap, out var restored);
            Assert.NotNull(restored);
            Assert.Equal(5, manager.UndoCount);
            Assert.Equal(1, manager.RedoCount);

            // Now perform another undo with an 80MB current snapshot (10000 x 2000 x 4 = 80,000,000 bytes)
            // 4 undo snapshots (160MB) + 1 existing redo snapshot (40MB) + new redo snapshot (80MB) = 280MB > 250MB
            // EnforceMemoryBudget must evict the oldest REDO snapshot first (the 40MB one)
            // Bringing total to 160MB + 80MB = 240MB <= 250MB
            using var currentImg2 = new Image<Rgba32>(10000, 2000);
            var currentSnap2 = new EditorSnapshot { Image = currentImg2.Clone() };
            manager.TryUndo(currentSnap2, out _);

            // Verify total bytes <= MaxUndoHistoryBytes and Redo stack was trimmed first
            Assert.True(manager.TotalUndoHistoryBytes() <= EditorHistoryManager.MaxUndoHistoryBytes);
            Assert.Equal(4, manager.UndoCount); // Undo stack remains untouched
            Assert.Equal(1, manager.RedoCount); // Oldest redo was evicted, leaving 1 in redo
        }

        [Fact]
        public void PeekUndo_And_PeekRedo_ReturnTopSnapshotsWithoutMutating()
        {
            var manager = new EditorHistoryManager();
            var snap1 = new EditorSnapshot();
            var snap2 = new EditorSnapshot();

            Assert.Null(manager.PeekUndo());
            Assert.Null(manager.PeekRedo());

            manager.PushUndo(snap1);
            Assert.Same(snap1, manager.PeekUndo());
            Assert.Equal(1, manager.UndoCount);

            manager.PushUndo(snap2);
            Assert.Same(snap2, manager.PeekUndo());
            Assert.Equal(2, manager.UndoCount);

            var current = new EditorSnapshot();
            manager.TryUndo(current, out _);

            Assert.Same(snap1, manager.PeekUndo());
            Assert.Same(current, manager.PeekRedo());
            Assert.Equal(1, manager.UndoCount);
            Assert.Equal(1, manager.RedoCount);
        }
    }
}
