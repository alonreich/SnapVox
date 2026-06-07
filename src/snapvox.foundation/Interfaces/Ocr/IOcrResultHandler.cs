using System.Threading.Tasks;

namespace snapvox.foundation.interfaces.Ocr
{
    public interface IOcrResultHandler
    {
        Task HandleOcrResult(string text);
    }
}
