using System.Collections.Generic;
using snapvox.native.foundation;

namespace snapvox.foundation.interfaces.Ocr
{
    public class OcrWord
    {
        public string Text { get; set; }
        public RECT Bounds { get; set; }
    }

    public class OcrInformation
    {
        public string Text { get; set; }
        public List<OcrWord> Words { get; set; } = new List<OcrWord>();

        public void Offset(int x, int y)
        {
            foreach (var word in Words)
            {
                word.Bounds = word.Bounds.Offset(x, y);
            }
        }
    }
}
