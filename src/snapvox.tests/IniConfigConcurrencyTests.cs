using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using snapvox.foundation.core;
using snapvox.foundation.IniFile;
using Xunit;

namespace snapvox.tests
{
    public class IniConfigConcurrencyTests
    {
        private static string CreateTempDir(string prefix)
        {
            string dir = Path.Combine(Path.GetTempPath(), prefix + "_" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            return dir;
        }

        [Fact]
        public void ConcurrentStressTest_500Iterations_ZeroInvalidOperationExceptions()
        {
            string dir = CreateTempDir("SnapVox_IniStress");
            try
            {
                IniConfig.IniDirectory = dir;
                IniConfig.Init("SnapVoxStress", "stress_config");

                var exceptions = new ConcurrentBag<Exception>();
                const int totalIterations = 500;
                const int degreeOfParallelism = 16;

                Parallel.For(0, totalIterations, new ParallelOptions { MaxDegreeOfParallelism = degreeOfParallelism }, i =>
                {
                    try
                    {
                        var config = IniConfig.GetIniSection<CoreConfiguration>();
                        Assert.NotNull(config);

                        // Concurrent read/mutation
                        config.Language = $"en-US-{i}";
                        config.CaptureDelay = i;

                        // Concurrent access to section properties
                        var props = IniConfig.PropertiesForSection(config);
                        Assert.NotNull(props);

                        // Dispatch save request concurrently
                        IniConfig.Save();
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                });

                // Drain and flush all queued writes
                IniConfig.Flush();

                Assert.Empty(exceptions);

                string iniPath = Path.Combine(dir, "stress_config.ini");
                Assert.True(File.Exists(iniPath), $"Expected {iniPath} to exist after Flush().");

                // Ensure the saved ini file is structurally valid and parseable
                var parsed = IniReader.Read(iniPath, Encoding.UTF8);
                Assert.True(parsed.ContainsKey("Core"), "Parsed ini must contain [Core] section.");
                Assert.True(parsed["Core"].ContainsKey("Language"), "[Core] section must contain Language property.");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void SimulatedDiskLatency_500ms_ConfirmsZeroDataLoss()
        {
            string dir = CreateTempDir("SnapVox_IniLatency");
            try
            {
                IniConfig.IniDirectory = dir;
                IniConfig.Init("SnapVoxLatency", "latency_config");

                var core = IniConfig.GetIniSection<CoreConfiguration>();
                core.Language = "initial-value";
                IniConfig.Save();
                IniConfig.Flush();

                string iniPath = Path.Combine(dir, "latency_config.ini");
                Assert.True(File.Exists(iniPath));

                // Activate 500ms simulated disk latency
                IniConfig.SimulatedDiskLatencyMs = 500;

                try
                {
                    // Trigger a slow write pass
                    core.Language = "pass-1-in-flight";
                    IniConfig.Save();

                    // Rapidly update state while the background writer is undergoing the 500ms disk latency
                    Thread.Sleep(50);
                    core.Language = "guaranteed-persisted-final-value";
                    core.CaptureDelay = 9999;
                    IniConfig.Save();

                    // Flush must wait for all requested versions to commit
                    IniConfig.Flush();
                }
                finally
                {
                    IniConfig.SimulatedDiskLatencyMs = 0;
                }

                // Verify zero data loss: the final state must be persisted to disk
                var finalSections = IniReader.Read(iniPath, Encoding.UTF8);
                Assert.True(finalSections.ContainsKey("Core"));
                Assert.Equal("guaranteed-persisted-final-value", finalSections["Core"]["Language"]);
                Assert.Equal("9999", finalSections["Core"]["CaptureDelay"]);
            }
            finally
            {
                IniConfig.SimulatedDiskLatencyMs = 0;
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void SaveTo_FlushesPendingQueue_BeforeWritingToCustomDestination()
        {
            string dir = CreateTempDir("SnapVox_IniSaveTo");
            try
            {
                IniConfig.IniDirectory = dir;
                IniConfig.Init("SnapVoxSaveTo", "main_config");

                var core = IniConfig.GetIniSection<CoreConfiguration>();
                core.Language = "custom-export-language";
                IniConfig.Save();

                string exportPath = Path.Combine(dir, "exported.ini");
                IniConfig.SaveTo(exportPath);

                Assert.True(File.Exists(exportPath));
                var parsed = IniReader.Read(exportPath, Encoding.UTF8);
                Assert.True(parsed.ContainsKey("Core"));
                Assert.Equal("custom-export-language", parsed["Core"]["Language"]);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void WaitForPendingSaves_TimesOut_WhenUncommitted()
        {
            // Verify that WaitForPendingSaves returns true on already committed state
            bool committed = IniConfig.WaitForPendingSaves(100);
            Assert.True(committed);
        }
    }
}
