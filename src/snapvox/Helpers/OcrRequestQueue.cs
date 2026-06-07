using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using snapvox.foundation.core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace snapvox.helpers
{
    public sealed class OcrRequestQueue : IDisposable, IAsyncDisposable
    {
        private readonly Func<Image, CancellationToken, Task<snapvox.foundation.interfaces.Ocr.OcrInformation>> _recognize;
        private readonly Channel<WorkItem> _channel;
        private readonly Task _worker;
        private readonly CancellationTokenSource _shutdownCts = new CancellationTokenSource();
        private int _disposed;

        public OcrRequestQueue(Func<Image, CancellationToken, Task<snapvox.foundation.interfaces.Ocr.OcrInformation>> recognize)
        {
            _recognize = recognize ?? throw new ArgumentNullException(nameof(recognize));
            _channel = Channel.CreateBounded<WorkItem>(new BoundedChannelOptions(4)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
            _worker = Task.Run(ProcessAsync);
        }

        public Task<snapvox.foundation.interfaces.Ocr.OcrInformation> EnqueueAsync(Image image, CancellationToken cancellationToken)
        {
            if (image == null)
            {
                return Task.FromResult<snapvox.foundation.interfaces.Ocr.OcrInformation>(null);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<snapvox.foundation.interfaces.Ocr.OcrInformation>(cancellationToken);
            }

            return EnqueueCoreAsync(image, cancellationToken);
        }

        private async Task<snapvox.foundation.interfaces.Ocr.OcrInformation> EnqueueCoreAsync(Image image, CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var completion = new TaskCompletionSource<snapvox.foundation.interfaces.Ocr.OcrInformation>(TaskCreationOptions.RunContinuationsAsynchronously);
            Image owned = null;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownCts.Token);

            try
            {
                owned = await Task.Run(() => image.Clone(x => { }), linked.Token).ConfigureAwait(false);
                var item = new WorkItem(owned, completion, cancellationToken);
                owned = null;

                if (!_channel.Writer.TryWrite(item))
                {
                    return await EnqueueSlowAsync(item, cancellationToken).ConfigureAwait(false);
                }

                ExecutionTrace.SetQueueDepth("Ocr", 1);
                return await completion.Task.ConfigureAwait(false);
            }
            catch
            {
                owned?.Dispose();
                throw;
            }
        }

        private async Task<snapvox.foundation.interfaces.Ocr.OcrInformation> EnqueueSlowAsync(WorkItem item, CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownCts.Token);
            try
            {
                await _channel.Writer.WriteAsync(item, linked.Token).ConfigureAwait(false);
                ExecutionTrace.SetQueueDepth("Ocr", 1);
                return await item.Completion.Task.ConfigureAwait(false);
            }
            catch
            {
                item.Completion.TrySetCanceled(linked.Token);
                item.Image.Dispose();
                throw;
            }
        }

        private async Task ProcessAsync()
        {
            try
            {
                await foreach (var item in _channel.Reader.ReadAllAsync(_shutdownCts.Token).ConfigureAwait(false))
                {
                    ExecutionTrace.SetQueueDepth("Ocr", 0);
                    if (item.CancellationToken.IsCancellationRequested)
                    {
                        item.Completion.TrySetCanceled(item.CancellationToken);
                        item.Image.Dispose();
                        continue;
                    }

                    try
                    {
                        using var linked = CancellationTokenSource.CreateLinkedTokenSource(item.CancellationToken, _shutdownCts.Token);
                        var result = await _recognize(item.Image, linked.Token).ConfigureAwait(false);
                        item.Completion.TrySetResult(result);
                    }
                    catch (OperationCanceledException)
                    {
                        item.Completion.TrySetCanceled(item.CancellationToken);
                    }
                    catch (Exception ex)
                    {
                        item.Completion.TrySetException(ex);
                    }
                    finally
                    {
                        item.Image.Dispose();
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                DrainPendingItems();
            }
        }

        private void DrainPendingItems()
        {
            while (_channel.Reader.TryRead(out var item))
            {
                item.Completion.TrySetCanceled(_shutdownCts.Token);
                item.Image.Dispose();
            }

            ExecutionTrace.SetQueueDepth("Ocr", 0);
        }

        public void Dispose()
        {
            _ = DisposeAsync().AsTask();
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _shutdownCts.Cancel();
            _channel.Writer.TryComplete();
            try
            {
                await _worker.ConfigureAwait(false);
            }
            finally
            {
                _shutdownCts.Dispose();
            }
        }

        private sealed class WorkItem
        {
            public WorkItem(Image image, TaskCompletionSource<snapvox.foundation.interfaces.Ocr.OcrInformation> completion, CancellationToken cancellationToken)
            {
                Image = image;
                Completion = completion;
                CancellationToken = cancellationToken;
            }

            public Image Image { get; }
            public TaskCompletionSource<snapvox.foundation.interfaces.Ocr.OcrInformation> Completion { get; }
            public CancellationToken CancellationToken { get; }
        }
    }
}
