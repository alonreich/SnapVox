using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using snapvox.foundation.core;

namespace snapvox.helpers
{
    public static class RetentionHelper
    {
        private static readonly string TempStorage = Path.Combine(Path.GetTempPath(), "SnapVox");
        private static readonly object SyncRoot = new object();
        private static Timer _cleanupTimer;
        private static int _cleanupRunning;

        public static void Start()
        {
            lock (SyncRoot)
            {
                if (_cleanupTimer != null)
                {
                    return;
                }

                _cleanupTimer = new Timer(_ => RunCleanup(), null, TimeSpan.Zero, TimeSpan.FromHours(4));
            }
        }

        public static void Stop()
        {
            lock (SyncRoot)
            {
                _cleanupTimer?.Dispose();
                _cleanupTimer = null;
            }
        }

        public static void RunCleanup()
        {
            if (Interlocked.Exchange(ref _cleanupRunning, 1) != 0)
            {
                return;
            }

            if (!Directory.Exists(TempStorage))
            {
                Interlocked.Exchange(ref _cleanupRunning, 0);
                return;
            }

            try
            {
                var now = DateTime.Now;
                
                var allFiles = Directory.GetFiles(TempStorage, "*", SearchOption.AllDirectories);
                foreach (var file in allFiles)
                {
                    try
                    {
                        var creationTime = File.GetCreationTime(file);
                        if ((now - creationTime).TotalHours >= 24)
                        {
                            File.Delete(file);
                        }
                    }
                    catch { }
                }

                var allDirs = Directory.GetDirectories(TempStorage, "*", SearchOption.AllDirectories)
                    .OrderByDescending(d => d.Length);
                foreach (var dir in allDirs)
                {
                    try
                    {
                        if (!Directory.EnumerateFileSystemEntries(dir).Any())
                        {
                            var creationTime = Directory.GetCreationTime(dir);
                            if ((now - creationTime).TotalHours >= 24)
                            {
                                Directory.Delete(dir);
                            }
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                BootstrapDebug.Log($"Retention cleanup failed: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _cleanupRunning, 0);
            }
        }
    }
}
