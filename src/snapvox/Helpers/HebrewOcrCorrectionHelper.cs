using System.Linq;
using System.Collections.Generic;
using snapvox.foundation.interfaces.Ocr;

namespace snapvox.helpers
{
    public static class HebrewOcrCorrectionHelper
    {
        public static void CorrectHebrewOcrInformation(OcrInformation ocrInfo)
        {
            if (ocrInfo?.Words == null || !ocrInfo.Words.Any()) return;

            foreach (var word in ocrInfo.Words)
            {
                if (OcrTextLayout.ContainsHebrew(word.Text))
                {
                    word.Text = OcrTextLayout.SwapParentheses(word.Text);
                }
            }

            OcrTextLayout.NormalizeTextFromWordsWhenEmpty(ocrInfo);
        }

        public static string BuildVisualSelectionText(IEnumerable<OcrWord> words)
        {
            return OcrTextLayout.BuildVisualSelectionText(words);
        }
    }
}
