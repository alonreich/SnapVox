using Avalonia.Controls;
using snapvox.editor.forms;

namespace snapvox.editor.helpers
{
    public static class OverlayHelper
    {
        public static void ShowNotification(string message, Window ownerWindow)
        {
            NotificationOverlayWindow.ShowNotification(message, ownerWindow);
        }

        public static void ShowLightToast(string message, Window ownerWindow)
        {
            NotificationOverlayWindow.ShowLightToast(message, ownerWindow);
        }
    }
}
