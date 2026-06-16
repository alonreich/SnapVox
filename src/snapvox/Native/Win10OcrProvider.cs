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
        private static readonly object EngineSync = new object();
        private static readonly Dictionary<string, OcrEngine> CachedEngines = new Dictionary<string, OcrEngine>(StringComparer.OrdinalIgnoreCase);

        public Win10OcrProvider()
        {
            _queue = new OcrRequestQueue(RecognizeCoreAsync);
        }

        public string EngineId => "windows";

        public string DisplayName => "Windows Native OCR Engine";

        public bool HasRequiredLanguages() => IsEnglishLanguageAvailable();

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

        public static bool AreRequiredLanguagesAvailable()
        {
            return IsEnglishLanguageAvailable() && IsHebrewLanguageAvailable();
        }

        public static bool IsEnglishLanguageAvailable()
        {
            return ResolveLanguageTag("en") != null;
        }

        public static bool IsHebrewLanguageAvailable()
        {
            return ResolveLanguageTag("he") != null;
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
                return "Windows OCR is not available. Install English and Hebrew language packs with OCR.";
            }

            if (!IsEnglishLanguageAvailable())
            {
                return "Windows OCR is missing English language support.";
            }

            return "Windows OCR is missing Hebrew language support. English OCR is available.";
        }

        public static Task EnsureWindowsOcrInstalled() => Task.CompletedTask;

        internal static Task<OcrInformation> RecognizeEnglishOnlyAsync(Image image, CancellationToken cancellationToken)
        {
            return RecognizeCoreAsync(image, cancellationToken, includeHebrew: false);
        }

        private static Task<OcrInformation> RecognizeCoreAsync(Image image, CancellationToken cancellationToken)
        {
            return RecognizeCoreAsync(image, cancellationToken, includeHebrew: true);
        }

        private static async Task<OcrInformation> RecognizeCoreAsync(Image image, CancellationToken cancellationToken, bool includeHebrew)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string englishTag = ResolveLanguageTag("en");
            string hebrewTag = includeHebrew ? ResolveLanguageTag("he") : null;
            if (englishTag == null)
            {
                return null;
            }

            using OcrPreparedImage prepared = OcrImagePreprocessor.Prepare(image, OcrPreprocessingProfile.Windows);
            if (prepared?.Image == null)
            {
                return null;
            }

            using SoftwareBitmap bitmap = await CreateSoftwareBitmapAsync(prepared.Image, cancellationToken).ConfigureAwait(false);
            if (bitmap == null)
            {
                return null;
            }

            OcrInformation english = await RecognizeWithLanguageAsync(bitmap, englishTag, prepared, cancellationToken).ConfigureAwait(false);
            if (!includeHebrew || hebrewTag == null)
            {
                return english;
            }

            OcrInformation hebrew = await RecognizeWithLanguageAsync(bitmap, hebrewTag, prepared, cancellationToken).ConfigureAwait(false);
            return OcrTextLayout.MergeByLanguage(hebrew, english);
        }

        private static async Task<OcrInformation> RecognizeWithLanguageAsync(SoftwareBitmap bitmap, string languageTag, OcrPreparedImage prepared, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(languageTag))
            {
                return null;
            }

            OcrEngine engine = GetOrCreateEngine(languageTag);
            if (engine == null)
            {
                return null;
            }

            OcrResult result = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken).ConfigureAwait(false);
            return MapResult(result, prepared);
        }

        private static OcrEngine GetOrCreateEngine(string languageTag)
        {
            lock (EngineSync)
            {
                if (CachedEngines.TryGetValue(languageTag, out var cached))
                {
                    return cached;
                }

                var engine = OcrEngine.TryCreateFromLanguage(new Language(languageTag));
                if (engine != null)
                {
                    CachedEngines[languageTag] = engine;
                }

                return engine;
            }
        }

        private static string ResolveLanguageTag(string prefix)
        {
            try
            {
                return OcrEngine.AvailableRecognizerLanguages
                    .Select(language => language.LanguageTag)
                    .FirstOrDefault(tag => tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return null;
            }
        }

        private static async Task<SoftwareBitmap> CreateSoftwareBitmapAsync(Image image, CancellationToken cancellationToken)
        {
            using var stream = new MemoryStream();
            await image.SaveAsync(stream, new PngEncoder(), cancellationToken).ConfigureAwait(false);
            stream.Position = 0;
            using IRandomAccessStream randomAccessStream = stream.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(randomAccessStream).AsTask(cancellationToken).ConfigureAwait(false);
            return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied).AsTask(cancellationToken).ConfigureAwait(false);
        }

        private static OcrInformation MapResult(OcrResult result, OcrPreparedImage prepared)
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
                            Bounds = prepared.MapBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height)
                        });
                    }
                }
            }

            OcrTextLayout.NormalizeTextFromWordsWhenEmpty(information);
            return information;
        }
    }
}
