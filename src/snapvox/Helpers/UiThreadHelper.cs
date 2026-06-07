using System;
using Avalonia.Threading;

namespace snapvox.helpers
{
    public static class UiThreadHelper
    {
        public static void Initialize(object syncContext) { }

        public static void Post(Action action)
        {
            Dispatcher.UIThread.Post(action);
        }

        public static System.Threading.Tasks.Task RunAsync(Action action)
        {
            return Dispatcher.UIThread.InvokeAsync(action).GetTask();
        }
    }
}
