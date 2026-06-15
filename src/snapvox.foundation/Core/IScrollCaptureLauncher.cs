using Avalonia.Controls;
using System.Threading.Tasks;

namespace snapvox.foundation.core
{
    public interface IScrollCaptureLauncher
    {
        Task StartAsync(Window ownerWindow = null);
    }
}
