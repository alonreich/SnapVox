using System;
using System.IO;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using snapvox.editor.Services;
using Xunit;

namespace snapvox.tests
{
    public class EditorExportServiceTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\t\r\n")]
        public void SanitizeFileName_NullOrWhitespace_ReturnsNull(string? input)
        {
            var result = EditorExportService.SanitizeFileName(input);
            Assert.Null(result);
        }

        [Fact]
        public void SanitizeFileName_StripsInvalidCharsAndCollapsesSeparators()
        {
            string raw = "My  Document: Version*1 / Final? <Draft>";
            string result = EditorExportService.SanitizeFileName(raw)!;

            Assert.Equal("My_Document_Version_1_Final_Draft", result);
            Assert.DoesNotContain("__", result);
            Assert.False(result.StartsWith("_"));
            Assert.False(result.EndsWith("_"));
        }

        [Fact]
        public void SanitizeFileName_ClampsMaxLength()
        {
            string raw = new string('A', 50);
            string result = EditorExportService.SanitizeFileName(raw, maxLength: 40)!;

            Assert.Equal(40, result.Length);
            Assert.Equal(new string('A', 40), result);
        }

        [Fact]
        public void GenerateDownloadFileName_ProducesCorrectExtensionsAndFormat()
        {
            var time = new DateTime(2026, 9, 8, 14, 30, 45, 123);

            string jpgName = EditorExportService.GenerateDownloadFileName("Report?2026", allowPng: false, timestamp: time);
            Assert.Equal("Report_2026_2026-09-08_14-30-45_123.jpg", jpgName);

            string pngName = EditorExportService.GenerateDownloadFileName("Report?2026", allowPng: true, timestamp: time);
            Assert.Equal("Report_2026_2026-09-08_14-30-45_123.png", pngName);

            string defaultPrefix = EditorExportService.GenerateDownloadFileName(null, allowPng: false, timestamp: time);
            Assert.Equal("Capture_2026-09-08_14-30-45_123.jpg", defaultPrefix);
        }

        [Fact]
        public void GenerateClipboardBackupFileName_ProducesSpaceFormattedDate()
        {
            var time = new DateTime(2026, 9, 8, 14, 30, 45, 123);

            string jpgName = EditorExportService.GenerateClipboardBackupFileName(allowPng: false, timestamp: time);
            Assert.Equal("Capture_2026-09-08 14_30_45_123.jpg", jpgName);

            string pngName = EditorExportService.GenerateClipboardBackupFileName(allowPng: true, timestamp: time);
            Assert.Equal("Capture_2026-09-08 14_30_45_123.png", pngName);
        }

        [Fact]
        public async Task SaveImageAsync_EncodesValidJpegAndPngSignatures()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "SnapVox_ExportTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                using var image = new Image<Rgba32>(32, 32);
                string jpgPath = Path.Combine(tempDir, "test.jpg");
                string pngPath = Path.Combine(tempDir, "test.png");

                await EditorExportService.SaveImageAsync(image, jpgPath, allowPng: false, jpegQuality: 90);
                await EditorExportService.SaveImageAsync(image, pngPath, allowPng: true);

                Assert.True(File.Exists(jpgPath));
                Assert.True(File.Exists(pngPath));

                byte[] jpgBytes = await File.ReadAllBytesAsync(jpgPath);
                Assert.True(jpgBytes.Length > 2);
                Assert.Equal(0xFF, jpgBytes[0]);
                Assert.Equal(0xD8, jpgBytes[1]); // JPEG SOI marker

                byte[] pngBytes = await File.ReadAllBytesAsync(pngPath);
                Assert.True(pngBytes.Length > 4);
                Assert.Equal(0x89, pngBytes[0]);
                Assert.Equal(0x50, pngBytes[1]); // 'P'
                Assert.Equal(0x4E, pngBytes[2]); // 'N'
                Assert.Equal(0x47, pngBytes[3]); // 'G'
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Fact]
        public void EncodeImage_ReturnsExpectedEncodedBytes()
        {
            using var image = new Image<Rgba32>(16, 16);

            byte[] jpg = EditorExportService.EncodeImage(image, allowPng: false, jpegQuality: 85);
            Assert.NotEmpty(jpg);
            Assert.Equal(0xFF, jpg[0]);
            Assert.Equal(0xD8, jpg[1]);

            byte[] png = EditorExportService.EncodeImage(image, allowPng: true);
            Assert.NotEmpty(png);
            Assert.Equal(0x89, png[0]);
            Assert.Equal(0x50, png[1]);
        }

        [Fact]
        public async Task SaveToHistoryBackupAsync_RespectsKeepBackupFlag()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "SnapVox_BackupTests_" + Guid.NewGuid().ToString("N"));

            try
            {
                using var image = new Image<Rgba32>(16, 16);

                bool disabledResult = await EditorExportService.SaveToHistoryBackupAsync(
                    "backup1.jpg", image, keepBackup: false, allowPng: false, jpegQuality: 90, customBackupDir: tempDir);
                Assert.False(disabledResult);
                Assert.False(File.Exists(Path.Combine(tempDir, "backup1.jpg")));

                bool enabledResult = await EditorExportService.SaveToHistoryBackupAsync(
                    "backup2.jpg", image, keepBackup: true, allowPng: false, jpegQuality: 90, customBackupDir: tempDir);
                Assert.True(enabledResult);
                Assert.True(File.Exists(Path.Combine(tempDir, "backup2.jpg")));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Fact]
        public async Task ResolveDownloadTargetAsync_FollowsPrecedenceChain()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "SnapVox_TargetTests_" + Guid.NewGuid().ToString("N"));
            string userDir = Path.Combine(tempDir, "UserConfigured");
            string defaultDir = Path.Combine(tempDir, "ShellDownloads");
            string pickedDir = Path.Combine(tempDir, "UserPicked");

            Directory.CreateDirectory(userDir);
            Directory.CreateDirectory(defaultDir);
            Directory.CreateDirectory(pickedDir);

            try
            {
                // 1. User configured path exists -> picked first
                var t1 = await EditorExportService.ResolveDownloadTargetAsync(
                    userDownloadPath: userDir,
                    folderPicker: null,
                    customDefaultPath: defaultDir);
                Assert.Equal(userDir, t1.Path);
                Assert.True(t1.IsDownloadsFolder);

                // 2. User configured path null -> shell downloads path picked
                var t2 = await EditorExportService.ResolveDownloadTargetAsync(
                    userDownloadPath: null,
                    folderPicker: null,
                    customDefaultPath: defaultDir);
                Assert.Equal(defaultDir, t2.Path);
                Assert.True(t2.IsDownloadsFolder);

                // 3. User configured & shell downloads nonexistent -> folder picker called
                string? configuredPath = null;
                var t3 = await EditorExportService.ResolveDownloadTargetAsync(
                    userDownloadPath: "C:\\NonExistent_Dir_12345",
                    folderPicker: () => Task.FromResult<string?>(pickedDir),
                    onUserPathConfigured: p => configuredPath = p,
                    customDefaultPath: "C:\\NonExistent_Dir_67890");
                Assert.Equal(pickedDir, t3.Path);
                Assert.True(t3.IsDownloadsFolder);
                Assert.Equal(pickedDir, configuredPath);

                // 4. Everything missing and picker returns null -> fallback to temp
                var t4 = await EditorExportService.ResolveDownloadTargetAsync(
                    userDownloadPath: null,
                    folderPicker: () => Task.FromResult<string?>(null),
                    customDefaultPath: "C:\\NonExistent_Dir_67890");
                Assert.Equal(Path.Combine(Path.GetTempPath(), "SnapVox"), t4.Path);
                Assert.False(t4.IsDownloadsFolder);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }
    }
}
