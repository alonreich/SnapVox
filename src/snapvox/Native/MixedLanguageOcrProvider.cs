#if USE_TESSERACT
using System;
using System.Threading;
using System.Threading.Tasks;
using snapvox.foundation.interfaces.Ocr;
using snapvox.helpers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace snapvox.native
{
    public sealed class MixedLanguageOcrProvider : IOcrProvider, IDisposable, IAsyncDisposable
    {
        private readonly TesseractOcrProvider _tesseract;
        private readonly OcrRequestQueue _queue;
        private int _disposed;

        public MixedLanguageOcrProvider(TesseractOcrProvider tesseract)
        {
            _tesseract = tesseract ?? throw new ArgumentNullException(nameof(tesseract));
            _queue = new OcrRequestQueue(RecognizeCoreAsync);
        }

        public string EngineId => "windows";

        public string DisplayName => "Windows Native OCR Engine";

        public bool HasRequiredLanguages()
        {
            return Win10OcrProvider.IsEnglishLanguageAvailable() && _tesseract.HasRequiredLanguages();
        }

        public Task<OcrInformation> DoOcrAsync(Image image) => DoOcrAsync(image, CancellationToken.None);

        public Task<OcrInformation> DoOcrAsync(Image image, CancellationToken ct) => _queue.EnqueueAsync(image, ct);

        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            await _queue.DisposeAsync().ConfigureAwait(false);
        }

        private async Task<OcrInformation> RecognizeCoreAsync(Image image, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using Image windowsImage = image.Clone(x => { });
            using Image tesseractImage = image.Clone(x => { });
            Task<OcrInformation> englishTask = Win10OcrProvider.RecognizeEnglishOnlyAsync(windowsImage, cancellationToken);
            Task<OcrInformation> tesseractTask = _tesseract.DoOcrAsync(tesseractImage, cancellationToken);
            await Task.WhenAll(englishTask, tesseractTask).ConfigureAwait(false);
            OcrInformation english = await englishTask.ConfigureAwait(false);
            OcrInformation tesseract = await tesseractTask.ConfigureAwait(false);
            return OcrTextLayout.MergeByLanguage(tesseract, english);
        }
    }
}
#endif
