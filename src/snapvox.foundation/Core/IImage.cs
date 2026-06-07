using snapvox.native;
using snapvox.native.foundation;
using snapvox.native.graphics;
using snapvox.native.ui;
using System;
using snapvox.foundation.core.AvaloniaShims;
using SixLabors.ImageSharp;

namespace snapvox.foundation.core
{

    public interface IImage : IDisposable
    {

        int Height { get; set; }

        int Width { get; set; }

        snapvox.foundation.core.AvaloniaShims.Size Size { get; }

        PixelFormat PixelFormat { get; }

        float VerticalResolution { get; }

        float HorizontalResolution { get; }

        Image Image { get; }
    }
}
