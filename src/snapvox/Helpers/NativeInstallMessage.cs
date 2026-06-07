using System;
using System.Runtime.InteropServices;

namespace snapvox.helpers;

internal static class NativeInstallMessage
{
    private const uint MbOk = 0x00000000;
    private const uint MbIconInformation = 0x00000040;
    private const uint MbIconError = 0x00000010;
    private const uint MbTopmost = 0x00040000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    public static void ShowOutcome(bool success, string detail, string logPath)
    {
        string caption = success ? "snapvox Setup" : "snapvox Setup — Failed";
        string body = success
            ? "Installation completed successfully." + Environment.NewLine + Environment.NewLine +
              "The application has been installed to:" + Environment.NewLine +
              StartupTaskHelper.InstallFolder + Environment.NewLine + Environment.NewLine +
              "Log: " + logPath
            : "Installation did not complete." + Environment.NewLine + Environment.NewLine +
              (string.IsNullOrWhiteSpace(detail) ? "See the log file for details." : detail) + Environment.NewLine +
              Environment.NewLine + "Log: " + logPath;

        uint flags = MbOk | MbTopmost | (success ? MbIconInformation : MbIconError);
        try
        {
            MessageBoxW(IntPtr.Zero, body, caption, flags);
        }
        catch
        {
        }
    }
}
