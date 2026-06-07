using snapvox.native;
using snapvox.native.foundation;
using snapvox.native.graphics;
using snapvox.native.ui;
using System;
using Microsoft.Win32;

namespace snapvox.foundation.core
{

    public static class RegistryKeyExtensions
    {

        public static string ReadKey64Or32(this RegistryHive registryHive, string keyName, string value = null, string defaultValue = null)
        {
            string result = null;
            value ??= string.Empty;

            using var registryKey32 = RegistryKey.OpenBaseKey(registryHive, RegistryView.Registry32);

            if (!Environment.Is64BitOperatingSystem)
            {
                using var key = registryKey32.OpenSubKey($@"SOFTWARE\{keyName}", false);

                if (key != null)
                {
                    result = (string) key.GetValue(value);
                }

                if (string.IsNullOrEmpty(result))
                {
                    result = defaultValue;
                }

                return result;
            }

            using var registryKey64 = RegistryKey.OpenBaseKey(registryHive, RegistryView.Registry64);

            using (var key = registryKey64.OpenSubKey($@"SOFTWARE\{keyName}", false))
            {
                if (key != null)
                {
                    result = (string) key.GetValue(value);
                }

                if (!string.IsNullOrEmpty(result)) return result;
            }

            using (var key = registryKey32.OpenSubKey($@"SOFTWARE\{keyName}", false))
            {
                if (key != null)
                {
                    result = (string) key.GetValue(value);
                }
            }

            if (string.IsNullOrEmpty(result))
            {
                result = defaultValue;
            }

            return result;
        }

        public static string ReadKey(this RegistryHive registryHive, string keyName, string value = null, string defaultValue = null)
        {
            string result = null;
            value ??= string.Empty;

            using var registryKey = RegistryKey.OpenBaseKey(registryHive, RegistryView.Default);
            using var key = registryKey.OpenSubKey($@"SOFTWARE\{keyName}", false);

            if (key != null)
            {
                result = (string)key.GetValue(value);
            }

            if (string.IsNullOrEmpty(result))
            {
                result = defaultValue;
            }

            return result;
        }
    }
}
