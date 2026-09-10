#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia.Controls;
using ImageSharpImage = SixLabors.ImageSharp.Image;

namespace snapvox.editor.Services
{
    public sealed class EditorSnapshot
    {
        private ImageSharpImage? _image;

        public ImageSharpImage? Image
        {
            get => Volatile.Read(ref _image);
            init => _image = value;
        }

        public List<Control> Annotations { get; init; } = new List<Control>();

        public ImageSharpImage? TakeImage() => Interlocked.Exchange(ref _image, null);

        public void ReleaseImage() => Interlocked.Exchange(ref _image, null)?.Dispose();
    }

    public sealed class EditorHistoryManager
    {
        public const int MaxStackSize = 40;
        public const long MaxUndoHistoryBytes = 384L * 1024 * 1024; // 384 MB

        private readonly LinkedList<EditorSnapshot> _undoStack = new LinkedList<EditorSnapshot>();
        private readonly LinkedList<EditorSnapshot> _redoStack = new LinkedList<EditorSnapshot>();
        private readonly Action<Control>? _disposeAnnotation;

        public EditorHistoryManager(Action<Control>? disposeAnnotation = null)
        {
            _disposeAnnotation = disposeAnnotation;
        }

        public int UndoCount => _undoStack.Count;
        public int RedoCount => _redoStack.Count;
        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        public void PushUndo(EditorSnapshot snapshot)
        {
            _undoStack.AddLast(snapshot);
            if (_undoStack.Count > MaxStackSize)
            {
                var oldest = _undoStack.First!.Value;
                _undoStack.RemoveFirst();
                DisposeSnapshot(oldest);
            }
            EnforceUndoMemoryBudget();
            ClearRedo();
        }

        public bool TryUndo(EditorSnapshot currentSnapshot, out EditorSnapshot? previousSnapshot)
        {
            if (_undoStack.Count == 0)
            {
                previousSnapshot = null;
                return false;
            }

            previousSnapshot = _undoStack.Last!.Value;
            _undoStack.RemoveLast();

            _redoStack.AddLast(currentSnapshot);
            if (_redoStack.Count > MaxStackSize)
            {
                var oldest = _redoStack.First!.Value;
                _redoStack.RemoveFirst();
                DisposeSnapshot(oldest);
            }

            EnforceUndoMemoryBudget();
            return true;
        }

        public bool TryRedo(EditorSnapshot currentSnapshot, out EditorSnapshot? nextSnapshot)
        {
            if (_redoStack.Count == 0)
            {
                nextSnapshot = null;
                return false;
            }

            nextSnapshot = _redoStack.Last!.Value;
            _redoStack.RemoveLast();

            _undoStack.AddLast(currentSnapshot);
            if (_undoStack.Count > MaxStackSize)
            {
                var oldest = _undoStack.First!.Value;
                _undoStack.RemoveFirst();
                DisposeSnapshot(oldest);
            }

            EnforceUndoMemoryBudget();
            return true;
        }

        public void ClearRedo()
        {
            foreach (var snapshot in _redoStack)
            {
                DisposeSnapshot(snapshot);
            }
            _redoStack.Clear();
        }

        public void Clear()
        {
            foreach (var snapshot in _undoStack)
            {
                DisposeSnapshot(snapshot);
            }
            _undoStack.Clear();

            ClearRedo();
        }

        public static long EstimateSnapshotBytes(EditorSnapshot? snapshot)
        {
            var image = snapshot?.Image;
            return image != null ? (long)image.Width * image.Height * 4 : 0;
        }

        public long TotalUndoHistoryBytes()
        {
            long total = 0;
            foreach (var snapshot in _undoStack) total += EstimateSnapshotBytes(snapshot);
            foreach (var snapshot in _redoStack) total += EstimateSnapshotBytes(snapshot);
            return total;
        }

        public void EnforceUndoMemoryBudget()
        {
            while (_undoStack.Count > 1 && TotalUndoHistoryBytes() > MaxUndoHistoryBytes)
            {
                var oldest = _undoStack.First!.Value;
                _undoStack.RemoveFirst();
                DisposeSnapshot(oldest);
            }
        }

        public void DisposeSnapshot(EditorSnapshot? snapshot)
        {
            if (snapshot == null) return;
            snapshot.ReleaseImage();
            if (snapshot.Annotations != null)
            {
                foreach (var annotation in snapshot.Annotations)
                {
                    _disposeAnnotation?.Invoke(annotation);
                }
                snapshot.Annotations.Clear();
            }
        }
    }
}
