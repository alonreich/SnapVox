using snapvox.native;
using snapvox.native.foundation;
using snapvox.native.graphics;
using snapvox.native.ui;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using snapvox.foundation.core.AvaloniaShims;
using System.Linq;
using snapvox.foundation.core;
using log4net;

namespace snapvox.helpers
{
    public static class OcrInstallationHelper
    {
        private static readonly ILog Log = snapvox.foundation.core.LogHelper.GetLogger(typeof(OcrInstallationHelper));
        private static string TessDataPath => GetTessDataDirectory();

        public static string GetTessDataDirectory(string installFolder = null)
        {
            return Path.Combine(ResolveOcrStorageRoot(installFolder), "tessdata");
        }

        private static string ResolveOcrStorageRoot(string explicitRoot)
        {
            if (!string.IsNullOrWhiteSpace(explicitRoot))
            {
                return explicitRoot;
            }

            if (StartupTaskHelper.IsRunningFromInstallPath())
            {
                return StartupTaskHelper.ConfigurationFolder;
            }

            return Path.Combine(DeploymentFootprint.TempAppFolder, "Brain");
        }

#if USE_TESSERACT
        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        private static readonly object SyncRoot = new object();

        private static bool HasTessData(string fileName)
        {
            try
            {
                if (!Directory.Exists(TessDataPath)) return false;
                string path = Path.Combine(TessDataPath, fileName);
                return File.Exists(path) && new FileInfo(path).Length >= 128 * 1024;
            }
            catch { return false; }
        }

        public static void EnsureBinariesExtracted(string installFolder = null)
        {
            try
            {
                string targetPath = ResolveOcrStorageRoot(installFolder);
                var asm = System.Reflection.Assembly.GetEntryAssembly();
                if (asm == null) asm = typeof(OcrInstallationHelper).Assembly;
                var resources = asm.GetManifestResourceNames();
                string[] libs = { "leptonica-1.82.0.dll", "tesseract50.dll" };

                if (!Directory.Exists(targetPath)) Directory.CreateDirectory(targetPath);
                foreach (var lib in libs)
                {
                    string destPath = Path.Combine(targetPath, lib);
                    if (File.Exists(destPath)) continue;

                    string resName = resources.FirstOrDefault(r => r.EndsWith(lib, StringComparison.OrdinalIgnoreCase));

                    if (resName != null)
                    {
                        using var s = asm.GetManifestResourceStream(resName);
                        if (s != null) { using var fs = new FileStream(destPath, FileMode.Create); s.CopyTo(fs); }
                    }
                }
                foreach (var lib in libs)
                {
                    LoadLibrary(Path.Combine(targetPath, lib));
                }
            }
            catch (Exception ex) { Log.Error("Binary extraction failed", ex); }
        }

        public static void EnsureOfflineTessDataExtracted(string installFolder = null)
        {
            lock (SyncRoot)
            {
                string tessDataPath = null;
                try
                {
                    tessDataPath = Path.Combine(ResolveOcrStorageRoot(installFolder), "tessdata");
                    Directory.CreateDirectory(tessDataPath);
                    var assembly = System.Reflection.Assembly.GetEntryAssembly();
                    if (assembly == null) assembly = typeof(OcrInstallationHelper).Assembly;
                    var resources = assembly.GetManifestResourceNames();
                    foreach (var languageFile in new[] { "heb.traineddata", "eng.traineddata" })
                    {
                        string destinationPath = Path.Combine(tessDataPath, languageFile);
                        if (File.Exists(destinationPath) && new FileInfo(destinationPath).Length >= 128 * 1024)
                        {
                            continue;
                        }

                        string resourceName = resources.FirstOrDefault(resource => resource.EndsWith(languageFile, StringComparison.OrdinalIgnoreCase));
                        if (string.IsNullOrEmpty(resourceName))
                        {
                            ExecutionTrace.LogEvent("TesseractOcr", "MissingEmbeddedData", languageFile);
                            continue;
                        }

                        using (var resourceStream = assembly.GetManifestResourceStream(resourceName))
                        {
                            if (resourceStream == null)
                            {
                                ExecutionTrace.LogEvent("TesseractOcr", "MissingResourceStream", languageFile);
                                continue;
                            }

                            using (var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                            {
                                resourceStream.CopyTo(fileStream);
                            }
                        }

                        ExecutionTrace.LogEvent("TesseractOcr", "ExtractedData", destinationPath);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Offline OCR data extraction failed", ex);
                    ExecutionTrace.LogException("TesseractOcr.ExtractData", ex, tessDataPath);
                }
            }
        }
#endif

        public static bool IsHebrewOcrInstalled()
        {
#if USE_TESSERACT
            return HasTessData("heb.traineddata") && HasTessData("eng.traineddata");
#else
            return native.Win10OcrProvider.IsHebrewLanguageAvailable();
#endif
        }

        public static bool IsOcrUsable()
        {
#if USE_TESSERACT
            return HasTessData("eng.traineddata") && HasTessData("heb.traineddata");
#else
            return native.Win10OcrProvider.AreRequiredLanguagesAvailable();
#endif
        }

        public static string GetMissingOcrMessage()
        {
#if USE_TESSERACT
            bool hasHebrew = HasTessData("heb.traineddata");
            bool hasEnglish = HasTessData("eng.traineddata");
            if (hasHebrew && hasEnglish) return string.Empty;
            if (hasEnglish) return "Hebrew offline OCR data is missing. English OCR is available.";
            if (hasHebrew) return "English offline OCR data is missing. Hebrew OCR is available.";
            return "Offline OCR data missing. heb.traineddata and eng.traineddata needed.";
#else
            return native.Win10OcrProvider.GetAvailabilityMessage();
#endif
        }

        public static void InstallHebrewOcr()
        {
#if USE_TESSERACT
            EnsureBinariesExtracted();
            EnsureOfflineTessDataExtracted();
#else
            native.Win10OcrProvider.EnsureWindowsOcrInstalled();
#endif
        }
    }
}
