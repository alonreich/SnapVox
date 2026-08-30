using System.Linq;
using System.Collections.Generic;
using snapvox.foundation.interfaces.Ocr;

namespace snapvox.helpers
{
    public static class HebrewOcrCorrectionHelper
    {
        public static string BuildVisualSelectionText(IEnumerable<OcrWord> words)
        {
            return OcrTextLayout.BuildVisualSelectionText(words);
        }
    }
}
