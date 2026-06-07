using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Linq;

namespace snapvox.helpers
{
    public static class PayloadExtractor
    {
        private const string PayloadName = "payload.zip";
        private static string _nativeDependencyDirectory;

        public static bool HasEmbeddedPayload()
        {
            return Assembly.GetExecutingAssembly().GetManifestResourceNames().Any(n => n.EndsWith(PayloadName, StringComparison.OrdinalIgnoreCase));
        }

        public static string GetNativeDependencyDirectory()
        {
            if (!string.IsNullOrEmpty(_nativeDependencyDirectory))
            {
                return _nativeDependencyDirectory;
            }

            if (StartupTaskHelper.IsRunningFromInstallPath())
            {
                _nativeDependencyDirectory = AppContext.BaseDirectory;
                return _nativeDependencyDirectory;
            }

            _nativeDependencyDirectory = Path.Combine(
                DeploymentFootprint.TempAppFolder,
                "Runtime",
                Process.GetCurrentProcess().Id.ToString());
            Directory.CreateDirectory(_nativeDependencyDirectory);
            NativeDllSearchPath.Register(_nativeDependencyDirectory);
            return _nativeDependencyDirectory;
        }

        public static void ExtractCriticalDependencies()
        {
            string[] criticalDlls = { "libSkiaSharp.dll", "libHarfBuzzSharp.dll", "av_libglesv2.dll" };
            string targetDir = GetNativeDependencyDirectory();

            string resourceName = Assembly.GetExecutingAssembly().GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(PayloadName, StringComparison.OrdinalIgnoreCase));
            if (resourceName == null)
            {
                return;
            }

            try
            {
                using Stream zipStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
                if (zipStream == null)
                {
                    return;
                }

                using ZipArchive archive = new ZipArchive(zipStream);
                foreach (string dll in criticalDlls)
                {
                    string destination = Path.Combine(targetDir, dll);
                    if (File.Exists(destination))
                    {
                        continue;
                    }

                    ZipArchiveEntry entry = archive.GetEntry(dll);
                    if (entry == null)
                    {
                        continue;
                    }

                    try
                    {
                        entry.ExtractToFile(destination, true);
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                BootstrapDebug.Log($"Failed to extract critical dependencies: {ex.Message}");
            }
        }

        public static void ExtractTo(string targetDirectory)
        {
            string resourceName = Assembly.GetExecutingAssembly().GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(PayloadName, StringComparison.OrdinalIgnoreCase));
            if (resourceName == null)
            {
                return;
            }

            Directory.CreateDirectory(targetDirectory);
            using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                return;
            }

            using ZipArchive archive = new ZipArchive(stream);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string destinationPath = Path.GetFullPath(Path.Combine(targetDirectory, entry.FullName));
                if (!destinationPath.StartsWith(Path.GetFullPath(targetDirectory), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(destinationPath);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                    try
                    {
                        entry.ExtractToFile(destinationPath, true);
                    }
                    catch (IOException)
                    {
                    }
                }
            }
        }
    }
}
