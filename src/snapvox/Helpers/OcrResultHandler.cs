using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using snapvox.foundation.core;
using snapvox.foundation.IniFile;
using snapvox.foundation.interfaces.Ocr;
using snapvox.editor.forms;
using snapvox.native;

namespace snapvox.helpers
{
    public class OcrResultHandler : IOcrResultHandler
    {
        private static readonly log4net.ILog Log = LogHelper.GetLogger(typeof(OcrResultHandler));

        public async Task HandleOcrResult(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                Log.Warn("OCR result was empty, nothing to handle.");
                Dispatcher.UIThread.Post(() => NotificationOverlayWindow.ShowNotification("No Text Found!", null));
                return;
            }

            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH_mm_ss_fff");
                string fileName = $"OCR_{timestamp}.txt";

                await snapvox.foundation.core.UiClipboard.SetTextAsync(text).ConfigureAwait(false);

                var config = IniConfig.GetIniSection<CoreConfiguration>();
                if (config.KeepBackup)
                {
                    string tempDir = Path.Combine(Path.GetTempPath(), "SnapVox");
                    Directory.CreateDirectory(tempDir);
                    string historyPath = Path.Combine(tempDir, fileName);
                    await File.WriteAllTextAsync(historyPath, text).ConfigureAwait(false);
                    Process.Start(new ProcessStartInfo("notepad.exe", historyPath) { UseShellExecute = true });

                    Dispatcher.UIThread.Post(() => {
                        NotificationOverlayWindow.ShowNotification("TEXT COPIED & SAVED", null);
                    });

                    Log.Info($"OCR completed. Text saved to {historyPath} and copied to clipboard.");
                    return;
                }

                Dispatcher.UIThread.Post(() => {
                    NotificationOverlayWindow.ShowNotification("TEXT COPIED", null);
                });

                Log.Info("OCR completed. Text copied to clipboard.");
            }
            catch (Exception ex)
            {
                Log.Error("Failed to handle OCR result.", ex);
            }
        }
    }
}
