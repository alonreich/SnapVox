using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace snapvox.helpers;

[SupportedOSPlatform("windows")]
internal static class ShellLinkWriter
{
    public static async Task CreateAsync(string shortcutPath, string targetPath, string workingDirectory, string iconLocation, string description = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(shortcutPath)) throw new ArgumentException("shortcutPath must be provided.", nameof(shortcutPath));
        if (string.IsNullOrEmpty(targetPath)) throw new ArgumentException("targetPath must be provided.", nameof(targetPath));

        string directory = Path.GetDirectoryName(shortcutPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var script = new StringBuilder();
        script.Append("$ws = New-Object -ComObject WScript.Shell; ");
        script.AppendFormat("$s = $ws.CreateShortcut('{0}'); ", shortcutPath.Replace("'", "''"));
        script.AppendFormat("$s.TargetPath = '{0}'; ", targetPath.Replace("'", "''"));
        if (!string.IsNullOrEmpty(workingDirectory))
        {
            script.AppendFormat("$s.WorkingDirectory = '{0}'; ", workingDirectory.Replace("'", "''"));
        }
        if (!string.IsNullOrEmpty(description))
        {
            script.AppendFormat("$s.Description = '{0}'; ", description.Replace("'", "''"));
        }
        if (!string.IsNullOrEmpty(iconLocation))
        {
            script.AppendFormat("$s.IconLocation = '{0}'; ", iconLocation.Replace("'", "''"));
        }
        script.Append("$s.Save();");

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -Command \"{script}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        using var process = Process.Start(startInfo);
        if (process == null) throw new InvalidOperationException("Failed to start PowerShell for shortcut creation.");

        try
        {
            await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromMilliseconds(15000), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            try { process.Kill(); } catch { }
            throw new TimeoutException("PowerShell shortcut creation timed out.");
        }
        
        if (process.ExitCode != 0)
        {
            string error = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"PowerShell shortcut creation failed (ExitCode {process.ExitCode}): {error}");
        }

        if (!File.Exists(shortcutPath))
        {
            throw new FileNotFoundException("PowerShell reported success but shortcut file was not created.", shortcutPath);
        }
    }
}
