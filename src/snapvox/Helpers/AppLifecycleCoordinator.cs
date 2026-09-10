using snapvox.foundation.core;
using snapvox.foundation.Interfaces;

namespace snapvox.helpers
{
    public class AppLifecycleCoordinator : IAppLifecycleCoordinator
    {
        public void StartServices()
        {
            ExecutionTrace.Start();
            RetentionHelper.Start();
            HotkeyManager.Start();
        }

        public void StopServices()
        {
            RetentionHelper.Stop();
            ExecutionTrace.Stop();
            HotkeyManager.Stop();
        }
    }
}