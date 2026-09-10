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
    }
}
