using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using snapvox.native.foundation;
using snapvox.foundation.Interfaces;
using snapvox.foundation.interfaces.Ocr;
using snapvox.helpers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using Tesseract;

namespace snapvox.native
{
    public sealed class TesseractOcrProvider : IOcrProvider, IDisposable, IAsyncDisposable
    {
        private readonly OcrRequestQueue _queue;
        private int _disposed;
        private static readonly SemaphoreSlim InitGate = new SemaphoreSlim(1, 1);
        private static volatile bool _initialized;

        public TesseractOcrProvider()
        {
            _queue = new OcrRequestQueue(RecognizeCoreAsync);
        }

        public string EngineId => "tesseract";

        public string DisplayName => "Tesseract (External OCR Engine)";

        public bool HasRequiredLanguages()
        {
            string tessDataPath = OcrInstallationHelper.GetTessDataDirectory();
            return HasTessData(tessDataPath, "eng.traineddata") && HasTessData(tessDataPath, "heb.traineddata");
        }

        public Task<OcrInformation> DoOcrAsync(Image image) => DoOcrAsync(image, CancellationToken.None);

        public Task<OcrInformation> DoOcrAsync(Image image, CancellationToken ct) => _queue.EnqueueAsync(image, ct);

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

            await _queue.DisposeAsync().ConfigureAwait(false);
        }

        private static async Task EnsureInitializedAsync(CancellationToken cancellationToken)
        {
            if (_initialized)
            {
                return;
            }

            await InitGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_initialized)
                {
                    return;
                }

                OcrInstallationHelper.EnsureBinariesExtracted();
                OcrInstallationHelper.EnsureOfflineTessDataExtracted();
                _initialized = true;
            }
            finally
            {
                InitGate.Release();
            }
        }

        private static bool HasTessData(string tessDataPath, string fileName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(tessDataPath) || !Directory.Exists(tessDataPath))
                {
                    return false;
                }

                string path = Path.Combine(tessDataPath, fileName);
                return File.Exists(path) && new FileInfo(path).Length >= 128 * 1024;
            }
            catch
            {
                return false;
            }
        }

        private async Task<OcrInformation> RecognizeCoreAsync(Image image, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

            string tessDataPath = OcrInstallationHelper.GetTessDataDirectory();
            if (!HasTessData(tessDataPath, "eng.traineddata") || !HasTessData(tessDataPath, "heb.traineddata"))
            {
                return null;
            }

            using OcrPreparedImage prepared = OcrImagePreprocessor.Prepare(image, OcrPreprocessingProfile.Tesseract);
            if (prepared?.Image == null)
            {
                return null;
            }

            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var buffer = new MemoryStream();
                prepared.Image.Save(buffer, new PngEncoder());
                using var engine = CreateEngine(tessDataPath);
                using Pix pix = Pix.LoadFromMemory(buffer.ToArray());
                using Page page = engine.Process(pix, PageSegMode.Auto);
                return MapPage(page, prepared);
            }, cancellationToken).ConfigureAwait(false);
        }

        private static TesseractEngine CreateEngine(string tessDataPath)
        {
            var engine = new TesseractEngine(tessDataPath, "heb+eng", EngineMode.LstmOnly);
            engine.DefaultPageSegMode = PageSegMode.Auto;
            engine.SetVariable("preserve_interword_spaces", "1");
            engine.SetVariable("load_system_dawg", false);
            engine.SetVariable("load_freq_dawg", false);
            engine.SetVariable("classify_enable_learning", false);
            engine.SetVariable("user_defined_dpi", "300");
            engine.SetVariable("textord_tabfind_find_tables", false);
            return engine;
        }

        private static OcrInformation MapPage(Page page, OcrPreparedImage prepared)
        {
            if (page == null)
            {
                return null;
            }

            if (page.GetMeanConfidence() < 0.15f)
            {
                return new OcrInformation { Text = string.Empty, Words = new List<OcrWord>() };
            }

            var information = new OcrInformation
            {
                Text = page.GetText() ?? string.Empty,
                Words = new List<OcrWord>()
            };

            using var iterator = page.GetIterator();
            iterator.Begin();
            do
            {
                if (!iterator.TryGetBoundingBox(PageIteratorLevel.Word, out var rect))
                {
                    continue;
                }

                string text = iterator.GetText(PageIteratorLevel.Word);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                information.Words.Add(new OcrWord
                {
                    Text = text.Trim(),
                    Bounds = prepared.MapBounds(rect.X1, rect.Y1, rect.X2 - rect.X1, rect.Y2 - rect.Y1)
                });
            }
            while (iterator.Next(PageIteratorLevel.Word));

            OcrTextLayout.NormalizeTextFromWordsWhenEmpty(information);
            return information;
        }
    }
}
