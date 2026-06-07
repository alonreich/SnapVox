using snapvox.native.foundation;
using System;
using System.Collections.Generic;
using snapvox.foundation.interfaces.Ocr;

namespace snapvox.foundation.Interfaces
{
    public interface ICaptureDetails
    {
        string Filename { get; set; }
        string Title { get; set; }
        DateTime DateTime { get; set; }
        OcrInformation OcrInformation { get; set; }
        RECT CaptureRect { get; set; }
    }
}
