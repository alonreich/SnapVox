using System;
using System.IO;
using System.Runtime.InteropServices;

namespace snapvox.helpers
{
    public static class ApplicationStartupHelper
    {
        public static void SetupEnvironment()
        {
            try
            {
                string programData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "snapvox");
                Directory.CreateDirectory(programData);
                GrantModifyRights(programData);
                
                string historyDir = Path.Combine(programData, "History");
                Directory.CreateDirectory(historyDir);
            }
            catch (Exception ex)
            {
                BootstrapDebug.Log($"SetupEnvironment failed: {ex.Message}");
            }
        }

        private static void GrantModifyRights(string path)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
            try
            {
                var directoryInfo = new DirectoryInfo(path);
                var directorySecurity = System.IO.FileSystemAclExtensions.GetAccessControl(directoryInfo);
                var rule = new System.Security.AccessControl.FileSystemAccessRule(
                    new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinUsersSid, null),
                    System.Security.AccessControl.FileSystemRights.Modify | System.Security.AccessControl.FileSystemRights.Synchronize,
                    System.Security.AccessControl.InheritanceFlags.ContainerInherit | System.Security.AccessControl.InheritanceFlags.ObjectInherit,
                    System.Security.AccessControl.PropagationFlags.None,
                    System.Security.AccessControl.AccessControlType.Allow);
                directorySecurity.AddAccessRule(rule);
                System.IO.FileSystemAclExtensions.SetAccessControl(directoryInfo, directorySecurity);
            }
            catch { /* Best effort */ }
        }

        public static void OnExternalCapture(string filePath) { }
    }
}