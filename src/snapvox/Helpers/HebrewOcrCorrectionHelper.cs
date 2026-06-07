using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using snapvox.foundation.interfaces.Ocr;

namespace snapvox.helpers
{
    public static class HebrewOcrCorrectionHelper
    {
        private const char HebrewStart = (char)0x0590;
        private const char HebrewEnd = (char)0x05FF;

        public static void CorrectHebrewOcrInformation(OcrInformation ocrInfo)
        {
            if (ocrInfo?.Words == null || !ocrInfo.Words.Any()) return;

            ocrInfo.Text = string.Join(" ", ocrInfo.Words.Select(w => w.Text));
        }

        public static string BuildVisualSelectionText(IEnumerable<OcrWord> words)
        {
            if (words == null)
            {
                return string.Empty;
            }

            var normalizedWords = words
                .Where(word => word != null && !string.IsNullOrWhiteSpace(word.Text))
                .OrderBy(word => word.Bounds.Top)
                .ThenBy(word => word.Bounds.Left)
                .ToList();

            if (normalizedWords.Count == 0)
            {
                return string.Empty;
            }

            var lines = new List<List<OcrWord>>();
            foreach (var word in normalizedWords)
            {
                bool added = false;
                int wordCenterY = word.Bounds.Top + (word.Bounds.Height / 2);
                foreach (var line in lines)
                {
                    int lineCenterY = (int)line.Average(existingWord => existingWord.Bounds.Top + (existingWord.Bounds.Height / 2));
                    int tolerance = Math.Max(6, (int)line.Average(existingWord => Math.Max(1, existingWord.Bounds.Height / 2)));
                    if (System.Math.Abs(lineCenterY - wordCenterY) <= tolerance)
                    {
                        line.Add(word);
                        added = true;
                        break;
                    }
                }

                if (!added)
                {
                    lines.Add(new List<OcrWord> { word });
                }
            }

            var builder = new StringBuilder();
            foreach (var line in lines.OrderBy(currentLine => currentLine.Min(word => word.Bounds.Top)))
            {
                bool hasHebrew = line.Any(word => ContainsHebrew(word.Text));
                var orderedWords = hasHebrew
                    ? line.OrderByDescending(word => word.Bounds.Left)
                    : line.OrderBy(word => word.Bounds.Left);
                string lineText = string.Join(" ", orderedWords.Select(word => word.Text.Trim()));
                if (string.IsNullOrWhiteSpace(lineText))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.Append(lineText.Normalize(NormalizationForm.FormC));
            }

            return builder.ToString().Trim();
        }

        private static bool ContainsHebrew(string text)
        {
            return !string.IsNullOrEmpty(text) && text.Any(c => c >= HebrewStart && c <= HebrewEnd);
        }
    }
}
