using System.Threading.Tasks;
using Avalonia.Controls;
using snapvox.forms;
using snapvox.foundation.core;

namespace snapvox.helpers
{
    internal sealed class ScrollCaptureLauncher : IScrollCaptureLauncher
    {
        public Task StartAsync(Window ownerWindow = null)
        {
            return ScrollCaptureWindow.StartAsync(ownerWindow);
        }
    }
}
