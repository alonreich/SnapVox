using snapvox.native;
using snapvox.native.foundation;
using snapvox.native.graphics;
using snapvox.native.ui;
using snapvox.foundation.core.AvaloniaShims;
using System.Threading.Tasks;

namespace snapvox.foundation.core
{
    public delegate Task<Bitmap> CaptureScreenRectangleHandler(RECT captureBounds);

    public static class CaptureHandler
    {
        public static CaptureScreenRectangleHandler CaptureScreenRectangle { get; set; }
    }
}


