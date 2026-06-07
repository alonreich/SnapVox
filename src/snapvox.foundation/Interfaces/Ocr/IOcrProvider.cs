using SixLabors.ImageSharp;
using System.Threading;
using System.Threading.Tasks;

namespace snapvox.foundation.interfaces.Ocr
{
    public interface IOcrProvider
    {
        string EngineId { get; }
        string DisplayName { get; }
        bool HasRequiredLanguages();
        Task<OcrInformation> DoOcrAsync(Image image, CancellationToken cancellationToken);
        Task<OcrInformation> DoOcrAsync(Image image);
    }
}
