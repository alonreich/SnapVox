using System.Xml.Linq;
using snapvox.helpers;

namespace snapvox.tests;

public class UpgradeSafetyTests
{
    [Theory]
    [InlineData(@"C:\Program Files\SnapVox\SnapVox.exe", true)]
    [InlineData(@"C:\Program Files\SnapVox\Uninstall.exe", true)]
    [InlineData(@"C:\Program Files\OtherApp\Uninstall.exe", false)]
    [InlineData(@"C:\Program Files\SnapVoxOther\Uninstall.exe", false)]
    [InlineData(@"C:\Program Files\SnapVox\..\OtherApp\Uninstall.exe", false)]
    [InlineData(@"C:\Users\User\Downloads\SnapVox.exe", false)]
    public void ProcessOwnership_RequiresActualInstallDirectory(string executable, bool expected)
        => Assert.Equal(expected, StartupTaskHelper.IsInstalledExecutable(executable, @"C:\Program Files\SnapVox"));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Startup_HasNoBatteryIdleNetworkOrTimeLimitRestrictions(bool elevated)
    {
        string executable = @"C:\Apps & Tools\SnapVox.exe";
        var xml = XDocument.Parse(StartupTaskDefinition.Create(executable, "S-1-5-21-123", elevated));
        XNamespace ns = StartupTaskDefinition.Namespace;
        var settings = xml.Root!.Element(ns + "Settings")!;
        foreach (string flag in new[] { "DisallowStartIfOnBatteries", "StopIfGoingOnBatteries", "RunOnlyIfIdle", "RunOnlyIfNetworkAvailable", "AllowHardTerminate" })
            Assert.Equal("false", settings.Element(ns + flag)!.Value);
        Assert.Equal("true", settings.Element(ns + "StartWhenAvailable")!.Value);
        Assert.Equal("PT0S", settings.Element(ns + "ExecutionTimeLimit")!.Value);
        Assert.Equal("InteractiveToken", xml.Descendants(ns + "LogonType").Single().Value);
        Assert.Equal(elevated ? "HighestAvailable" : "LeastPrivilege", xml.Descendants(ns + "RunLevel").Single().Value);
        Assert.Equal(executable, xml.Descendants(ns + "Command").Single().Value);
        Assert.Equal("--autorun", xml.Descendants(ns + "Arguments").Single().Value);
        Assert.Equal("S-1-5-21-123", xml.Descendants(ns + "LogonTrigger").Single().Element(ns + "UserId")!.Value);
    }

    [Fact]
    public async Task SettingsBackup_RestoresExactSettingsIncludingElevation()
    {
        using var files = new TestFiles();
        string original = "[Core]\r\nRunAsAdministratorOnStartup=True\r\nRegionHotkey=Alt + A\r\n";
        await File.WriteAllTextAsync(files.Settings, original);
        string backup = await UpgradeSettingsBackup.CreateAsync(new[] { files.Settings, files.Settings.ToUpperInvariant() }, files.BackupRoot);
        Assert.Single(Directory.GetFiles(backup, "*.ini"));
        File.Delete(files.Settings);
        await UpgradeSettingsBackup.RestoreAsync(backup, new[] { files.Settings });
        Assert.Equal(original, await File.ReadAllTextAsync(files.Settings));
        Assert.True(File.Exists(Path.Combine(backup, "manifest.txt")));
    }

    [Fact]
    public async Task FailedBackup_StopsWithoutChangingOriginalSettings()
    {
        using var files = new TestFiles();
        await File.WriteAllTextAsync(files.Settings, "preserve me");
        using var locked = new FileStream(files.Settings, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        await Assert.ThrowsAsync<IOException>(() => UpgradeSettingsBackup.CreateAsync(new[] { files.Settings }, files.BackupRoot));
        Assert.Equal(11, locked.Length);
    }

    [Fact]
    public async Task DamagedBackup_IsRejectedBeforeReplacingAnySettings()
    {
        using var files = new TestFiles();
        await File.WriteAllTextAsync(files.Settings, "original");
        string backup = await UpgradeSettingsBackup.CreateAsync(new[] { files.Settings }, files.BackupRoot);
        await File.WriteAllTextAsync(files.Settings, "new installation");
        await File.WriteAllTextAsync(Path.Combine(backup, "settings_0.ini"), "damaged");
        await Assert.ThrowsAsync<IOException>(() => UpgradeSettingsBackup.RestoreAsync(backup, new[] { files.Settings }));
        Assert.Equal("new installation", await File.ReadAllTextAsync(files.Settings));
        Assert.True(Directory.Exists(backup));
    }

    [Fact]
    public async Task FailedRestore_KeepsRecoveryCopyForRetry()
    {
        using var files = new TestFiles();
        await File.WriteAllTextAsync(files.Settings, "original settings");
        string backup = await UpgradeSettingsBackup.CreateAsync(new[] { files.Settings }, files.BackupRoot);
        using (var locked = new FileStream(files.Settings, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var error = await Record.ExceptionAsync(() => UpgradeSettingsBackup.RestoreAsync(backup, new[] { files.Settings }));
            Assert.True(error is IOException or UnauthorizedAccessException);
        }
        Assert.Equal("original settings", await File.ReadAllTextAsync(Path.Combine(backup, "settings_0.ini")));
        await UpgradeSettingsBackup.RestoreAsync(backup, new[] { files.Settings });
        Assert.Equal("original settings", await File.ReadAllTextAsync(files.Settings));
    }

    private sealed class TestFiles : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "SnapVoxUpgradeTests_" + Guid.NewGuid().ToString("N"));
        public string Settings => Path.Combine(_root, "snapvox.ini");
        public string BackupRoot => Path.Combine(_root, "recovery");
        public TestFiles() => Directory.CreateDirectory(_root);
        public void Dispose() => Directory.Delete(_root, true);
    }
}
