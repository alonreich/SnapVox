using snapvox.native;
using snapvox.native.foundation;
using snapvox.native.graphics;
using snapvox.native.ui;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using snapvox.foundation.Interop;
using Kernel32Api = snapvox.foundation.Interop.Kernel32Api;
using User32Api = snapvox.foundation.Interop.User32Api;
using snapvox.foundation.IniFile;
using System.Linq;

namespace snapvox.foundation.core
{

    public static class EnvironmentInfo
    {
        private static bool? _isWindows;

        public static bool IsWindows
        {
            get
            {
                if (_isWindows.HasValue)
                {
                    return _isWindows.Value;
                }

                _isWindows = Environment.OSVersion.Platform.ToString().StartsWith("Win");
                return _isWindows.Value;
            }
        }

        public static bool IsNet45OrNewer()
        {

            return Type.GetType("System.Reflection.ReflectionContext", false) != null;
        }

        public static string GetsnapvoxVersion(bool shortVersion = false)
        {
            var executingAssembly = Assembly.GetExecutingAssembly();

            string snapvoxVersion = executingAssembly.GetName().Version.ToString();

            var assemblyFileVersionAttribute = executingAssembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
            if (!string.IsNullOrEmpty(assemblyFileVersionAttribute?.Version))
            {
                var assemblyFileVersion = new Version(assemblyFileVersionAttribute.Version);
                snapvoxVersion = assemblyFileVersion.ToString(2);
                try
                {
                    snapvoxVersion = assemblyFileVersion.ToString(3);
                }
                catch (Exception)
                {

                }
            }

            if (!shortVersion)
            {

                var informationalVersionAttribute = executingAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                if (!string.IsNullOrEmpty(informationalVersionAttribute?.InformationalVersion))
                {
                    snapvoxVersion = informationalVersionAttribute.InformationalVersion;
                }
            }

            return snapvoxVersion.Replace("+", " - ");
        }

        public static string EnvironmentToString(bool newline)
        {
            StringBuilder environment = new();
            environment.Append("Software version: " + GetsnapvoxVersion());
            if (IniConfig.IsPortable)
            {
                environment.Append(" Portable");
            }

            environment.Append(" (" + OsInfo.Bits + " bit)");

            if (newline)
            {
                environment.AppendLine();
            }
            else
            {
                environment.Append(", ");
            }

            environment.Append(".NET runtime version: " + Environment.Version);
            if (IsNet45OrNewer())
            {
                environment.Append("+");
            }

            if (newline)
            {
                environment.AppendLine();
            }
            else
            {
                environment.Append(", ");
            }

            environment.Append("Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));

            if (IsWindows)
            {
                if (newline)
                {
                    environment.AppendLine();
                }
                else
                {
                    environment.Append(", ");
                }

                environment.Append($"OS: {OsInfo.Name}");
                if (!string.IsNullOrEmpty(OsInfo.Edition))
                {
                    environment.Append($" {OsInfo.Edition}");
                }

                if (!string.IsNullOrEmpty(OsInfo.ServicePack))
                {
                    environment.Append($" {OsInfo.ServicePack}");
                }

                environment.Append($" x{OsInfo.Bits}");
                environment.Append($" {OsInfo.VersionString}");
                if (newline)
                {
                    environment.AppendLine();
                }
                else
                {
                    environment.Append(", ");
                }

                environment.AppendFormat("GDI object count: {0}", User32Api.GetGuiResourcesGdiCount());
                if (newline)
                {
                    environment.AppendLine();
                }
                else
                {
                    environment.Append(", ");
                }

                environment.AppendFormat("User object count: {0}", User32Api.GetGuiResourcesUserCount());
            }
            else
            {
                if (newline)
                {
                    environment.AppendLine();
                }
                else
                {
                    environment.Append(", ");
                }

                environment.AppendFormat("OS: {0}", Environment.OSVersion.Platform);
            }

            if (newline)
            {
                environment.AppendLine();
            }
            else
            {
                environment.Append(", ");
            }

            return environment.ToString();
        }

        public static string ExceptionToString(Exception ex)
        {
            if (ex == null)
                return "null\r\n";

            StringBuilder report = new();

            report.AppendLine("Exception: " + ex.GetType());
            report.AppendLine("Message: " + ex.Message);
            if (ex.Data.Count > 0)
            {
                report.AppendLine();
                report.AppendLine("Additional Information:");
                foreach (object key in ex.Data.Keys)
                {
                    object data = ex.Data[key];
                    if (data != null)
                    {
                        report.AppendLine(key + " : " + data);
                    }
                }
            }

            if (ex is ExternalException externalException)
            {

                report.AppendLine().AppendLine("ErrorCode: 0x" + externalException.ErrorCode.ToString("X"));
            }

            report.AppendLine().AppendLine("Stack:").AppendLine(ex.StackTrace);

            if (ex is ReflectionTypeLoadException reflectionTypeLoadException)
            {
                report.AppendLine().AppendLine("LoaderExceptions: ");
                foreach (Exception cbE in reflectionTypeLoadException.LoaderExceptions)
                {
                    report.AppendLine(cbE.Message);
                }
            }

            if (ex.InnerException != null)
            {
                report.AppendLine("--- InnerException: ---");
                report.AppendLine(ExceptionToString(ex.InnerException));
            }

            return report.ToString();
        }

        public static string BuildReport(Exception exception)
        {
            StringBuilder exceptionText = new();
            exceptionText.AppendLine(EnvironmentToString(true));
            exceptionText.AppendLine(ExceptionToString(exception));
            exceptionText.AppendLine("Configuration dump:");

            return exceptionText.ToString();
        }

        public static string GetApplicationFolder()
            => AppDomain.CurrentDomain.BaseDirectory;
    }

    public static class OsInfo
    {

        public static int Bits => IntPtr.Size * 8;

        private static string _sEdition;

        public static string Edition
        {
            get
            {
                if (_sEdition != null)
                {
                    return _sEdition;
                }

                string edition = string.Empty;

                OperatingSystem osVersion = Environment.OSVersion;
                var osVersionInfo = snapvox.foundation.Interop.OsVersionInfoEx.Create();

                if (Kernel32Api.GetVersionEx(ref osVersionInfo))
                {
                    int majorVersion = osVersion.Version.Major;
                    int minorVersion = osVersion.Version.Minor;
                    var productType = osVersionInfo.wProductType;
                    var suiteMask = osVersionInfo.wSuiteMask;

                    if (majorVersion == 4)
                    {
                        if (productType == (byte)WindowsProductTypes.VER_NT_WORKSTATION)
                        {

                            edition = "Workstation";
                        }
                        else if (productType == (byte)WindowsProductTypes.VER_NT_SERVER)
                        {
                            edition = (suiteMask & (ushort)WindowsSuites.Enterprise) != 0 ? "Enterprise Server" : "Standard Server";
                        }
                    }

                    else if (majorVersion == 5)
                    {
                        if (productType == (byte)WindowsProductTypes.VER_NT_WORKSTATION)
                        {
                            if ((suiteMask & (ushort)WindowsSuites.Personal) != 0)
                            {

                                edition = "Home";
                            }
                            else
                            {

                                edition = "Professional";
                            }
                        }
                        else if (productType == (byte)WindowsProductTypes.VER_NT_SERVER)
                        {
                            if (minorVersion == 0)
                            {
                                if ((suiteMask & (ushort)WindowsSuites.DataCenter) != 0)
                                {

                                    edition = "Datacenter Server";
                                }
                                else if ((suiteMask & (ushort)WindowsSuites.Enterprise) != 0)
                                {

                                    edition = "Advanced Server";
                                }
                                else
                                {

                                    edition = "Server";
                                }
                            }
                            else
                            {
                                if ((suiteMask & (ushort)WindowsSuites.DataCenter) != 0)
                                {

                                    edition = "Datacenter";
                                }
                                else if ((suiteMask & (ushort)WindowsSuites.Enterprise) != 0)
                                {

                                    edition = "Enterprise";
                                    }
                                    else if ((suiteMask & (ushort)WindowsSuites.Blade) != 0)
                                    {

                                        edition = "Web Edition";
                                    }
                                    else
                                    {

                                        edition = "Standard";
                                    }
                                }
                            }
                        }

                        else if (majorVersion == 6)
                        {
                            if (Kernel32Api.GetProductInfo((uint)majorVersion, (uint)minorVersion, (uint)osVersionInfo.wServicePackMajor, (uint)osVersionInfo.wServicePackMinor, out var windowsProduct) != 0)
                            {
                                edition = windowsProduct.ToString();
                            }
                        }
                    }

                _sEdition = edition;
                return edition;
            }
        }

        private static string _name;

        public static string Name
        {
            get
            {
                if (_name != null)
                {
                    return _name;
                }

                string name = "unknown";

                OperatingSystem osVersion = Environment.OSVersion;
                var osVersionInfo = snapvox.foundation.Interop.OsVersionInfoEx.Create();
                if (Kernel32Api.GetVersionEx(ref osVersionInfo))
                {
                    int majorVersion = osVersion.Version.Major;
                    int minorVersion = osVersion.Version.Minor;
                    var productType = osVersionInfo.wProductType;
                    var suiteMask = osVersionInfo.wSuiteMask;
                    switch (osVersion.Platform)
                    {
                        case PlatformID.Win32Windows:
                            if (majorVersion == 4)
                            {
                                string csdVersion = osVersionInfo.ServicePackVersion;
                                switch (minorVersion)
                                {
                                    case 0:
                                        if (csdVersion == "B" || csdVersion == "C")
                                        {
                                            name = "Windows 95 OSR2";
                                        }
                                        else
                                        {
                                            name = "Windows 95";
                                        }

                                        break;
                                    case 10:
                                        name = csdVersion == "A" ? "Windows 98 Second Edition" : "Windows 98";
                                        break;
                                    case 90:
                                        name = "Windows Me";
                                        break;
                                }
                            }

                            break;
                        case PlatformID.Win32NT:
                            switch (majorVersion)
                            {
                                case 3:
                                    name = "Windows NT 3.51";
                                    break;
                                case 4:
                                    switch ((WindowsProductTypes)productType)
                                    {
                                        case WindowsProductTypes.VER_NT_WORKSTATION:
                                            name = "Windows NT 4.0";
                                            break;
                                        case WindowsProductTypes.VER_NT_SERVER:
                                            name = "Windows NT 4.0 Server";
                                            break;
                                    }

                                    break;
                                case 5:
                                    switch (minorVersion)
                                    {
                                        case 0:
                                            name = "Windows 2000";
                                            break;
                                        case 1:
                                            name = (WindowsSuites)suiteMask switch
                                            {
                                                WindowsSuites.Personal => "Windows XP Professional",
                                                _ => "Windows XP"
                                            };
                                            break;
                                        case 2:
                                            name = (WindowsSuites)suiteMask switch
                                            {
                                                WindowsSuites.Personal => "Windows XP Professional x64",
                                                WindowsSuites.Enterprise => "Windows Server 2003 Enterprise",
                                                WindowsSuites.DataCenter => "Windows Server 2003 Data Center",
                                                WindowsSuites.Blade => "Windows Server 2003 Web Edition",
                                                WindowsSuites.WHServer => "Windows Home Server",
                                                _ => "Windows Server 2003"
                                            };
                                            break;
                                    }

                                    break;
                                case 6:
                                    switch (minorVersion)
                                    {
                                        case 0:
                                            name = (WindowsProductTypes)productType switch
                                            {
                                                WindowsProductTypes.VER_NT_SERVER => "Windows Server 2008",
                                                _ => "Windows Vista"
                                            };
                                            break;
                                        case 1:
                                            name = (WindowsProductTypes)productType switch
                                            {
                                                WindowsProductTypes.VER_NT_SERVER => "Windows Server 2008 R2",
                                                _ => "Windows 7"
                                            };
                                            break;
                                        case 2:
                                            name = "Windows 8";
                                            break;
                                        case 3:
                                            name = "Windows 8.1";
                                            break;
                                    }

                                    break;
                                case 10:
                                    if (osVersion.Version.Build < 22000)
                                    {
                                        name = "Windows 10";
                                    } else {
                                        name = "Windows 11";
                                    }
                                    break;
                            }

                            break;
                    }
                }

                _name = name;
                return name;
            }
        }

        public static string ServicePack
        {
            get
            {
                string servicePack = string.Empty;
                var osVersionInfo = snapvox.foundation.Interop.OsVersionInfoEx.Create();

                if (Kernel32Api.GetVersionEx(ref osVersionInfo))
                {
                    servicePack = osVersionInfo.ServicePackVersion;
                }

                return servicePack;
            }
        }

        public static string VersionString
        {
            get
            {
                if (WindowsVersion.IsWindows10OrLater)
                {
                    return $"build {Environment.OSVersion.Version.Build}";
                }

                if (Environment.OSVersion.Version.Revision != 0)
                {
                    return
                        $"{Environment.OSVersion.Version.Major}.{Environment.OSVersion.Version.Minor} build {Environment.OSVersion.Version.Build} revision {Environment.OSVersion.Version.Revision:X}";
                }

                return $"{Environment.OSVersion.Version.Major}.{Environment.OSVersion.Version.Minor} build {Environment.OSVersion.Version.Build}";
            }
        }
    }
}


