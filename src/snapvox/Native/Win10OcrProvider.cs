using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using snapvox.native.foundation;
using snapvox.foundation.Interfaces;
using snapvox.foundation.interfaces.Ocr;
using DomainOcrWord = snapvox.foundation.interfaces.Ocr.OcrWord;
using snapvox.helpers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace snapvox.native
{
    public sealed class Win10OcrProvider : IOcrProvider, IDisposable, IAsyncDisposable
    {
        private readonly OcrRequestQueue _queue;
        private int _disposed;
        private static OcrEngine _cachedEngine;
        private static readonly object EngineSync = new object();

        public Win10OcrProvider()
        {
            _queue = new OcrRequestQueue(RecognizeCoreAsync);
        }

        public string EngineId => "windows";

        public string DisplayName => "Windows Native OCR Engine";

        public bool HasRequiredLanguages() => AreRequiredLanguagesAvailable();

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

        public static bool AreRequiredLanguagesAvailable()
        {
            try
            {
                var languages = OcrEngine.AvailableRecognizerLanguages;
                bool english = languages.Any(language => language.LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase));
                bool hebrew = languages.Any(language => language.LanguageTag.StartsWith("he", StringComparison.OrdinalIgnoreCase));
                return english || hebrew;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsAnySupportedLanguageAvailable()
        {
            try
            {
                return OcrEngine.AvailableRecognizerLanguages.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        public static string GetAvailabilityMessage()
        {
            if (AreRequiredLanguagesAvailable())
            {
                return string.Empty;
            }

            if (!IsAnySupportedLanguageAvailable())
            {
                return "Windows OCR is not available. Install the English and Hebrew language packs with OCR.";
            }

            return "Windows OCR is missing English or Hebrew language support.";
        }

        public static Task EnsureWindowsOcrInstalled() => Task.CompletedTask;

        private static OcrEngine GetOrCreateEngine()
        {
            lock (EngineSync)
            {
                if (_cachedEngine != null)
                {
                    return _cachedEngine;
                }

                _cachedEngine = OcrEngine.TryCreateFromUserProfileLanguages() ?? OcrEngine.TryCreateFromLanguage(new Language("en-US"));
                return _cachedEngine;
            }
        }

        private static async Task<OcrInformation> RecognizeCoreAsync(Image image, CancellationToken cancellationToken)
        {
            if (!AreRequiredLanguagesAvailable())
            {
                return null;
            }

            Image preprocessed = OcrImagePreprocessor.Prepare(image);
            bool disposePreprocessed = !ReferenceEquals(preprocessed, image);
            (float scaleX, float scaleY) = OcrImagePreprocessor.GetScaleFactors(image, preprocessed);
            try
            {
                using SoftwareBitmap bitmap = await CreateSoftwareBitmapAsync(preprocessed, cancellationToken).ConfigureAwait(false);
                if (bitmap == null)
                {
                    return null;
                }

                OcrEngine engine = GetOrCreateEngine();
                if (engine == null)
                {
                    return null;
                }

                OcrResult result = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken).ConfigureAwait(false);
                return MapResult(result, scaleX, scaleY);
            }
            finally
            {
                if (disposePreprocessed && preprocessed != null)
                {
                    preprocessed.Dispose();
                }
            }
        }

        private static async Task<SoftwareBitmap> CreateSoftwareBitmapAsync(Image image, CancellationToken cancellationToken)
        {
            using var stream = new MemoryStream();
            await image.SaveAsync(stream, new PngEncoder(), cancellationToken).ConfigureAwait(false);
            stream.Position = 0;
            using IRandomAccessStream randomAccessStream = stream.AsRandomAccessStream();
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(randomAccessStream).AsTask(cancellationToken).ConfigureAwait(false);
            return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied).AsTask(cancellationToken).ConfigureAwait(false);
        }

        private static OcrInformation MapResult(OcrResult result, float scaleX, float scaleY)
        {
            if (result == null)
            {
                return null;
            }

            var information = new OcrInformation
            {
                Text = result.Text ?? string.Empty,
                Words = new List<DomainOcrWord>()
            };

            if (result.Lines != null)
            {
                foreach (var line in result.Lines)
                {
                    if (line?.Words == null)
                    {
                        continue;
                    }

                    foreach (var word in line.Words)
                    {
                        if (word == null || string.IsNullOrWhiteSpace(word.Text))
                        {
                            continue;
                        }

                        var bounds = word.BoundingRect;
                        information.Words.Add(new DomainOcrWord
                        {
                            Text = word.Text,
                            Bounds = RECT.FromXYWH(
                                (int)Math.Round(bounds.X * scaleX),
                                (int)Math.Round(bounds.Y * scaleY),
                                (int)Math.Round(bounds.Width * scaleX),
                                (int)Math.Round(bounds.Height * scaleY))
                        });
                    }
                }
            }

            HebrewOcrCorrectionHelper.CorrectHebrewOcrInformation(information);
            return information;
        }
    }
}
