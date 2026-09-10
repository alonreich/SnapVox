using System.IO;
using System.Linq;
using snapvox.foundation.IniFile;
using Xunit;

namespace snapvox.tests
{
    public class IniConfigAtomicSaveTests
    {
        private static string TempDir() => Path.Combine(Path.GetTempPath(), "SnapVox_IniAtomicTests_" + Path.GetRandomFileName());

        [Fact]
        public void SaveTo_WritesTargetFile_AndLeavesNoTempFileBehind()
        {
            string dir = TempDir(); Directory.CreateDirectory(dir);
            try
            {
                string ini = Path.Combine(dir, "test.ini");
                IniConfig.SaveTo(ini);
                Assert.True(File.Exists(ini));
                Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void SaveTo_SecondSave_KeepsPreviousContentAsBak_AndTargetStaysParseable()
        {
            string dir = TempDir(); Directory.CreateDirectory(dir);
            try
            {
                string ini = Path.Combine(dir, "test.ini");
                IniConfig.SaveTo(ini);
                string firstContent = File.ReadAllText(ini);

                IniConfig.SaveTo(ini);

                Assert.True(File.Exists(ini));
                Assert.True(File.Exists(ini + ".bak"), "Atomic save must leave the previous version as .bak");
                Assert.Equal(firstContent, File.ReadAllText(ini + ".bak"));
                // Torn writes are impossible if the target is always parseable:
                IniReader.Read(ini, System.Text.Encoding.UTF8);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void SaveTo_OutputIsDeterministic_TempIsFullyConsumed()
        {
            string dir = TempDir(); Directory.CreateDirectory(dir);
            try
            {
                string a = Path.Combine(dir, "a.ini");
                string b = Path.Combine(dir, "b.ini");
                IniConfig.SaveTo(a);
                IniConfig.SaveTo(b);
                Assert.Equal(File.ReadAllText(a), File.ReadAllText(b));
                Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
            }
            finally { Directory.Delete(dir, true); }
        }
    }
}
