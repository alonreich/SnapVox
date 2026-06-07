using snapvox.native;
using snapvox.native.foundation;
using snapvox.native.graphics;
using snapvox.native.ui;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using log4net;

namespace snapvox.foundation.IniFile
{
    public class IniConfig
    {
        private static readonly ILog Log = snapvox.foundation.core.LogHelper.GetLogger(typeof(IniConfig));
        private const string IniExtension = ".ini";
        private const string DefaultsPostfix = "-defaults";
        private const string FixedPostfix = "-fixed";
        private static readonly object IniLock = new object();
        private static readonly object SectionMapLock = new object();
        private static string _applicationName;
        private static string _configName;
        private static readonly IDictionary<string, IniSection> SectionMap = new Dictionary<string, IniSection>();
        private static IDictionary<string, IDictionary<string, string>> _sections = new Dictionary<string, IDictionary<string, string>>();
        private static IDictionary<string, IDictionary<string, string>> _fixedProperties;
        private static bool _portableCheckMade;
        public static bool IsPortable { get; private set; }
        private static string _iniDirectory;
        private static bool _iniDirectoryExplicitlySet;

        public static string IniDirectory
        {
            get => _iniDirectoryExplicitlySet ? _iniDirectory : null;
            set { _iniDirectory = value; _iniDirectoryExplicitlySet = !string.IsNullOrWhiteSpace(value); }
        }

        public static void Init(string appName, string configName)
        {
            _applicationName = appName;
            _configName = configName;
            Reload();
        }

        public static bool IsInitialized => _applicationName != null && _configName != null && SectionMap.Count > 0;

        public static void Init()
        {
            var attr = Assembly.GetEntryAssembly().GetCustomAttributes(typeof(AssemblyProductAttribute), false) as AssemblyProductAttribute[];
            if (attr is { Length: > 0 }) { Log.InfoFormat("Using ProductName {0}", attr[0].Product); Init(attr[0].Product, attr[0].Product); }
            else throw new InvalidOperationException("Assembly ProductName not set.");
        }

        public static string ConfigLocation => IsInitialized ? CreateIniLocation(_configName + IniExtension, false) : throw new InvalidOperationException("Ini configuration was not initialized!");

        private static string CreateIniLocation(string configFilename, bool isReadOnly)
        {
            if (_applicationName == null || _configName == null) throw new InvalidOperationException("IniConfig.Init not called!");
            
            if (_iniDirectoryExplicitlySet && !string.IsNullOrWhiteSpace(_iniDirectory))
            {
                return Path.Combine(_iniDirectory, configFilename);
            }

            string appPath = GetApplicationStartupPath();
            string iniFilePath = null;
            if (appPath != null)
            {
                string pafPath = Path.Combine(appPath, @"App\" + _applicationName);
                if (!IsPortable && !_portableCheckMade) { Log.Info("Checking for portable mode."); _portableCheckMade = true; if (Directory.Exists(pafPath)) { IsPortable = true; Log.Info("Portable mode active!"); } }
                if (IsPortable)
                {
                    string pafConfigPath = Path.Combine(appPath, @"Data\Settings");
                    try { if (!Directory.Exists(pafConfigPath)) Directory.CreateDirectory(pafConfigPath); iniFilePath = Path.Combine(pafConfigPath, configFilename); }
                    catch (Exception e) { Log.InfoFormat("Portable mode failed: {0}", e.Message); }
                }
            }
            if (iniFilePath == null)
            {
                iniFilePath = Path.Combine(appPath ?? ".", configFilename);
                if (!File.Exists(iniFilePath))
                {
                    string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), _applicationName);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    iniFilePath = Path.Combine(dir, configFilename);
                }
            }
            Log.InfoFormat("Using ini file {0}", iniFilePath);
            return iniFilePath;
        }

        public static void Reload()
        {
            lock (IniLock)
            {
                _sections = new Dictionary<string, IDictionary<string, string>>();
                Read(CreateIniLocation(_configName + DefaultsPostfix + IniExtension, true));
                Read(CreateIniLocation(_configName + IniExtension, false));
                _fixedProperties = Read(CreateIniLocation(_configName + FixedPostfix + IniExtension, true));
                lock (SectionMapLock)
                {
                    foreach (var section in SectionMap.Values)
                    {
                        if (section == null) continue;
                        try 
                        { 
                            section.Fill(PropertiesForSection(section)); 
                            FixProperties(section); 
                        }
                        catch (Exception ex) 
                        { 
                            Log.WarnFormat("Problem reading ini section {0}: {1}", section?.IniSectionAttribute?.Name ?? "unknown", ex.Message); 
                        }
                    }
                }
            }
        }

        private static void FixProperties(IniSection section)
        {
            if (section?.IniSectionAttribute == null || _fixedProperties == null) return;
            if (!_fixedProperties.TryGetValue(section.IniSectionAttribute.Name, out var fixedProps)) return;
            if (fixedProps == null) return;
            foreach (string key in fixedProps.Keys) 
            {
                if (key != null && section.Values.TryGetValue(key, out var val) && val != null) val.IsFixed = true;
            }
        }

        private static bool IsOptionalIniFile(string iniLocation)
        {
            return iniLocation.EndsWith(DefaultsPostfix + IniExtension, StringComparison.OrdinalIgnoreCase)
                || iniLocation.EndsWith(FixedPostfix + IniExtension, StringComparison.OrdinalIgnoreCase);
        }

        private static IDictionary<string, IDictionary<string, string>> Read(string iniLocation)
        {
            if (!File.Exists(iniLocation))
            {
                if (IsOptionalIniFile(iniLocation))
                {
                    Log.Debug("Optional ini file not present: " + iniLocation);
                }
                else
                {
                    Log.Info("Can't find file: " + iniLocation);
                }

                return null;
            }
            Log.InfoFormat("Loading ini-file: {0}", iniLocation);
            var newSections = IniReader.Read(iniLocation, Encoding.UTF8);
            foreach (string section in newSections.Keys)
            {
                if (!_sections.ContainsKey(section)) _sections.Add(section, newSections[section]);
                else
                {
                    var curr = _sections[section];
                    foreach (var kv in newSections[section]) if (curr.ContainsKey(kv.Key)) curr[kv.Key] = kv.Value; else curr.Add(kv.Key, kv.Value);
                }
            }
            return newSections;
        }

        public static T GetIniSection<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] T>() where T : IniSection => GetIniSection<T>(true);

        public static T GetIniSection<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] T>(bool allowSave) where T : IniSection
        {
            Type type = typeof(T);
            string name = IniSection.GetIniSectionAttribute(type).Name;
            T section;
            lock (SectionMapLock)
            {
                if (SectionMap.TryGetValue(name, out var s)) section = (T)s;
                else { section = Activator.CreateInstance<T>(); SectionMap.Add(name, section); section.Fill(PropertiesForSection(section)); FixProperties(section); }
            }
            if (allowSave && section.IsDirty) { Log.DebugFormat("Section {0} dirty, saving!", name); Save(); }
            return section;
        }

        public static IDictionary<string, string> PropertiesForSection(IniSection section)
        {
            string name = section.IniSectionAttribute.Name;
            if (!_sections.TryGetValue(name, out var props)) { props = new Dictionary<string, string>(); _sections.Add(name, props); }
            return props;
        }

        public static void Save()
        {
            if (Monitor.TryEnter(IniLock, TimeSpan.FromMilliseconds(200)))
            {
                try { SaveInternally(CreateIniLocation(_configName + IniExtension, false)); }
                catch (Exception ex) { Log.Error("Failed to save ini", ex); }
                finally { Monitor.Exit(IniLock); }
            }
        }

        public static void SaveTo(string path)
        {
            SaveInternally(path);
        }

        private static void SaveInternally(string iniLocation)
        {
            Log.Info("Saving configuration to: " + iniLocation);
            string dir = Path.GetDirectoryName(iniLocation);
            if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            
            using (var fs = new FileStream(iniLocation, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(fs, Encoding.UTF8))
            {
                foreach (var section in SectionMap.Values) { section.Write(writer, false); writer.WriteLine(); section.IsDirty = false; }
                writer.WriteLine();
                foreach (string name in _sections.Keys)
                {
                    if (SectionMap.ContainsKey(name)) continue;
                    writer.WriteLine("; Section {0} unclaimed", name);
                    writer.WriteLine("[{0}]", name);
                    foreach (var kv in _sections[name]) writer.WriteLine("{0}={1}", kv.Key, kv.Value);
                    writer.WriteLine();
                }
                writer.Flush();
            }
        }

        private static string GetApplicationStartupPath() => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
