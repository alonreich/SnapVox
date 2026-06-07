using snapvox.native;
using snapvox.native.foundation;
using snapvox.native.graphics;
using snapvox.native.ui;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using snapvox.foundation.IniFile;
using log4net;
using Microsoft.Win32;

namespace snapvox.foundation.core
{

    public class Language
    {
        private static readonly ILog Log = snapvox.foundation.core.LogHelper.GetLogger(typeof(Language));
        private static readonly List<string> LanguagePaths = new();
        private static readonly Dictionary<string, List<LanguageFile>> LanguageFiles = new();
        private static readonly Dictionary<string, string> HelpFiles = new();
        private const string DefaultLanguage = "en-US";
        private const string HelpFilenamePattern = @"help-*.html";
        private const string LanguageFilenamePattern = @"language*.xml";
        private static readonly Regex PrefixRegexp = new(@"language_([a-zA-Z0-9]+).*");
        private static readonly Regex IetfRegexp = new(@"^.*([a-zA-Z]{2,3}-([a-zA-Z]{1,2})|[a-zA-Z]{2,3}-x-[a-zA-Z]+)$");
        private const string LanguageGroupsKey = @"SYSTEM\CurrentControlSet\Control\Nls\Language Groups";
        private static readonly List<string> UnsupportedLanguageGroups = new();
        private static readonly Dictionary<string, string> Resources = new();
        private static string _currentLanguage;

        public static event LanguageChangedHandler LanguageChanged;

        static Language()
        {
            if (!LogHelper.IsInitialized)
            {
                Log.Warn("Log4net hasn't been initialized yet! (Design mode?)");
                LogHelper.InitializeLog4Net();
            }

            LoadEmbeddedLanguage();

            try
            {
                string applicationFolder = EnvironmentInfo.GetApplicationFolder();

                if (applicationFolder != null)
                {
                    AddPath(Path.Combine(applicationFolder, @"App\snapvox\Languages"));
                }

                string applicationDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                AddPath(Path.Combine(applicationDataFolder, @"snapvox\Languages\"));

                if (applicationFolder != null)
                {
                    AddPath(Path.Combine(applicationFolder, @"Languages"));
                }
            }
            catch (Exception pathException)
            {
                Log.Error(pathException);
            }

            try
            {
                using RegistryKey languageGroupsKey = Registry.LocalMachine.OpenSubKey(LanguageGroupsKey, false);
                if (languageGroupsKey != null)
                {
                    string[] groups = languageGroupsKey.GetValueNames();
                    foreach (string group in groups)
                    {
                        string groupValue = (string) languageGroupsKey.GetValue(group);
                        bool isGroupNotInstalled = "0".Equals(groupValue);
                        if (isGroupNotInstalled)
                        {
                            UnsupportedLanguageGroups.Add(group.ToLower());
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warn("Couldn't read the installed language groups.", e);
            }

            if (!IniConfig.IsInitialized)
            {
                Log.Warn("Language initialized before IniConfig. Using defaults.");
                ScanFiles();
                if (Resources.Count == 0) LoadEmbeddedLanguage();
                return;
            }

            var coreConfig = IniConfig.GetIniSection<CoreConfiguration>();
            ScanFiles();

            if (Resources.Count == 0) {
                LoadEmbeddedLanguage();
            }

            if (!string.IsNullOrEmpty(coreConfig.Language))
            {
                CurrentLanguage = coreConfig.Language;
                if (CurrentLanguage != null && CurrentLanguage != coreConfig.Language)
                {
                    coreConfig.Language = CurrentLanguage;
                }
            }

            if (CurrentLanguage == null)
            {
                Log.Warn("Couldn't set language from configuration, changing to default. Installation problem?");
                CurrentLanguage = DefaultLanguage;
                if (CurrentLanguage != null)
                {
                    coreConfig.Language = CurrentLanguage;
                }
            }

            if (CurrentLanguage == null)
            {
                Log.Error("Couldn't set language, installation problem?");
            }
        }

        private static void LoadEmbeddedLanguage()
        {
            try
            {
                var assembly = typeof(Language).Assembly;
                string resourceName = "snapvox.foundation.Languages.language-en-US.xml";
                using Stream stream = assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    XmlDocument xmlDocument = new XmlDocument();
                    xmlDocument.Load(stream);
                    XmlNodeList resourceNodes = xmlDocument.GetElementsByTagName("resource");
                    foreach (XmlNode resourceNode in resourceNodes)
                    {
                        string key = resourceNode.Attributes?["name"].Value;
                        if (string.IsNullOrEmpty(key)) continue;
                        string text = resourceNode.InnerText;
                        if (!string.IsNullOrEmpty(text)) text = text.Trim();
                        Resources[key] = text;
                    }
                    _currentLanguage = DefaultLanguage;
                    if (!LanguageFiles.ContainsKey(DefaultLanguage))
                    {
                        LanguageFiles.Add(DefaultLanguage, new List<LanguageFile> { new LanguageFile { Ietf = DefaultLanguage, Description = "English (United States)", Filepath = "embedded" } });
                    }
                    Log.Info("Loaded embedded English language resource.");
                }
            }
            catch (Exception e)
            {
                Log.Error("Could not load embedded language resource", e);
            }
        }

        private static bool AddPath(string path)
        {
            if (LanguagePaths.Contains(path))
            {
                return false;
            }
            if (Directory.Exists(path))
            {
                Log.DebugFormat("Adding language path {0}", path);
                LanguagePaths.Add(path);
                return true;
            }

            Log.InfoFormat("Not adding non existing language path {0}", path);

            return false;
        }

        public static bool AddLanguageFilePath(string path)
        {
            if (LanguagePaths.Contains(path))
            {
                return true;
            }
            Log.DebugFormat("New language path {0}", path);
            if (AddPath(path))
            {
                ScanFiles();
                Reload();
            }
            else
            {
                return false;
            }

            return true;
        }

        private static void LoadFiles(string ietf)
        {
            if (!LanguageFiles.ContainsKey(ietf))
            {
                Log.ErrorFormat("No language {0} available.", ietf);
                return;
            }

            foreach (var languageFile in LanguageFiles[ietf])
            {
                LoadResources(languageFile);
            }
        }

        private static void Reload()
        {
            Resources.Clear();
            if (!LanguageFiles.ContainsKey(DefaultLanguage))
            {
                LoadEmbeddedLanguage();
            }
            LoadFiles(DefaultLanguage);

            if (_currentLanguage != null && !_currentLanguage.Equals(DefaultLanguage))
            {
                LoadFiles(_currentLanguage);
            }
        }

        public static string CurrentLanguage
        {
            get => _currentLanguage;
            set
            {
                string ietf = FindBestIetfMatch(value);
                if (!LanguageFiles.ContainsKey(ietf))
                {
                    Log.WarnFormat("No match for language {0} found!", ietf);
                }
                else
                {
                    if (_currentLanguage == null || !_currentLanguage.Equals(ietf))
                    {
                        _currentLanguage = ietf;
                        Reload();
                        if (LanguageChanged == null)
                        {
                            return;
                        }

                        try
                        {
                            LanguageChanged(null, null);
                        }
                        catch
                        {

                        }

                        return;
                    }
                }

                Log.Debug("CurrentLanguage not changed!");
            }
        }

        private static string FindBestIetfMatch(string inputIetf)
        {
            string returnIetf = inputIetf;
            if (string.IsNullOrEmpty(returnIetf))
            {
                returnIetf = DefaultLanguage;
            }

            if (LanguageFiles.ContainsKey(returnIetf))
            {
                return returnIetf;
            }
            Log.WarnFormat("Unknown language {0}, trying best match!", returnIetf);
            if (returnIetf.Length == 5)
            {
                returnIetf = returnIetf.Substring(0, 2);
            }

            foreach (string availableIetf in LanguageFiles.Keys)
            {
                if (!availableIetf.StartsWith(returnIetf)) continue;

                Log.InfoFormat("Found language {0}, best match for {1}!", availableIetf, returnIetf);
                returnIetf = availableIetf;
                break;
            }

            return returnIetf;
        }

        public static IList<LanguageFile> SupportedLanguages
        {
            get
            {
                IList<LanguageFile> languages = new List<LanguageFile>();

                foreach (List<LanguageFile> langs in LanguageFiles.Values)
                {

                    foreach (LanguageFile langFile in langs)
                    {

                        if (langFile.Prefix != null) continue;
                        languages.Add(langFile);
                        break;
                    }
                }

                return languages;
            }
        }

        public static string HelpFilePath
        {
            get
            {
                if (HelpFiles.ContainsKey(_currentLanguage))
                {
                    return HelpFiles[_currentLanguage];
                }

                return HelpFiles[DefaultLanguage];
            }
        }

        private static void LoadResources(LanguageFile languageFile)
        {
            Log.InfoFormat("Loading language file {0}", languageFile.Filepath);
            try
            {
                XmlDocument xmlDocument = new XmlDocument();
                xmlDocument.Load(languageFile.Filepath);
                XmlNodeList resourceNodes = xmlDocument.GetElementsByTagName("resource");
                foreach (XmlNode resourceNode in resourceNodes)
                {
                    string key = resourceNode.Attributes?["name"].Value;
                    if (string.IsNullOrEmpty(key))
                    {
                        continue;
                    }
                    if (!string.IsNullOrEmpty(languageFile.Prefix))
                    {
                        key = languageFile.Prefix + "." + key;
                    }

                    string text = resourceNode.InnerText;
                    if (!string.IsNullOrEmpty(text))
                    {
                        text = text.Trim();
                    }
                    Resources[key] = text;
                }
            }
            catch (Exception e)
            {
                Log.Error("Could not load language file " + languageFile.Filepath, e);
            }
        }

        private static void ScanFiles()
        {
            LanguageFiles.Clear();
            HelpFiles.Clear();
            
            if (!LanguageFiles.ContainsKey(DefaultLanguage))
            {
                LanguageFiles.Add(DefaultLanguage, new List<LanguageFile> { new LanguageFile { Ietf = DefaultLanguage, Description = "English (United States)", Filepath = "embedded" } });
            }

            foreach (string languagePath in LanguagePaths)
            {
                if (!Directory.Exists(languagePath))
                {
                    Log.InfoFormat("Skipping non existing language path {0}", languagePath);
                    continue;
                }

                Log.InfoFormat("Searching language directory '{0}' for language files with pattern '{1}'", languagePath, LanguageFilenamePattern);
                try
                {
                    foreach (string languageFilepath in Directory.GetFiles(languagePath, LanguageFilenamePattern, SearchOption.AllDirectories))
                    {
                        string languageFilename = Path.GetFileNameWithoutExtension(languageFilepath);
                        string ietf = IetfRegexp.Replace(languageFilename, "$1");
                        if (string.IsNullOrEmpty(ietf))
                        {
                            continue;
                        }

                        LanguageFile languageFile = null;
                        bool loadDetails = false;
                        try
                        {
                            var cultureInfo = CultureInfo.GetCultureInfoByIetfLanguageTag(ietf);
                            if (cultureInfo == null)
                            {
                                continue;
                            }

                            languageFile = new LanguageFile
                            {
                                Filepath = languageFilepath,
                                Ietf = ietf,
                                Description = cultureInfo.NativeName
                            };

                            if (languageFile.Description.IndexOf(ietf, StringComparison.InvariantCultureIgnoreCase) >= 0)
                            {
                                loadDetails = true;
                            }
                        }
                        catch (Exception)
                        {
                            loadDetails = true;
                        }

                        if (loadDetails || languageFile == null)
                        {
                            try
                            {
                                languageFile = LoadFileInfo(languageFilepath);
                            }
                            catch (Exception ex)
                            {
                                Log.ErrorFormat($"Error trying to read language file {languageFilepath}, skipping.", ex);
                                continue;
                            }
                        }
                        if (PrefixRegexp.IsMatch(languageFilename))
                        {
                            languageFile.Prefix = PrefixRegexp.Replace(languageFilename, "$1")?.ToLower();
                        }

                        if (!string.IsNullOrEmpty(languageFile.LanguageGroup) && UnsupportedLanguageGroups.Contains(languageFile.LanguageGroup))
                        {
                            Log.InfoFormat("Skipping unsupported (not able to display) language {0} from file {1}", languageFile.Description, languageFilepath);
                            continue;
                        }

                        List<LanguageFile> currentFiles;
                        if (LanguageFiles.ContainsKey(languageFile.Ietf))
                        {
                            currentFiles = LanguageFiles[languageFile.Ietf];
                            currentFiles.Add(languageFile);
                        }
                        else
                        {
                            currentFiles = new List<LanguageFile>
                            {
                                languageFile
                            };
                            LanguageFiles.Add(languageFile.Ietf, currentFiles);
                            Log.DebugFormat("Added language definition {0} from: {1}", languageFile.Description, languageFile.Filepath);
                        }
                    }
                }
                catch (DirectoryNotFoundException)
                {
                    Log.InfoFormat("Non existing language directory: {0}", languagePath);
                }
                catch (Exception e)
                {
                    Log.Error("Error trying for read directory " + languagePath, e);
                }

                Log.InfoFormat("Searching language directory '{0}' for help files with pattern '{1}'", languagePath, HelpFilenamePattern);
                try
                {
                    foreach (string helpFilepath in Directory.GetFiles(languagePath, HelpFilenamePattern, SearchOption.AllDirectories))
                    {
                        Log.DebugFormat("Found help file: {0}", helpFilepath);
                        string helpFilename = Path.GetFileName(helpFilepath);
                        string ietf = helpFilename.Replace(".html", string.Empty).Replace("help-", "");
                        if (!HelpFiles.ContainsKey(ietf))
                        {
                            HelpFiles.Add(ietf, helpFilepath);
                        }
                        else
                        {
                            Log.WarnFormat("skipping help file {0}, already a file with the same IETF {1} found!", helpFilepath, ietf);
                        }
                    }
                }
                catch (DirectoryNotFoundException)
                {
                    Log.InfoFormat("Non existing language directory: {0}", languagePath);
                }
                catch (Exception e)
                {
                    Log.Error("Error trying for read directory " + languagePath, e);
                }
            }
        }

        private static LanguageFile LoadFileInfo(string languageFilePath)
        {
            Log.InfoFormat("Retrieving language details from file: {0}", languageFilePath);
            try
            {
                using var xmlReader = XmlReader.Create(languageFilePath);
                var languageFile = new LanguageFile
                {
                    Filepath = languageFilePath
                };

                while (xmlReader.Read())
                {
                    if (xmlReader.NodeType == XmlNodeType.Element)
                    {
                        break;
                    }
                }

                while (xmlReader.MoveToNextAttribute())
                {
                    switch (xmlReader.NodeType)
                    {
                        case XmlNodeType.Attribute:

                            switch (xmlReader.Name)
                            {
                                case "ietf":
                                    languageFile.Ietf = xmlReader.Value;
                                    break;
                                case "description":
                                    languageFile.Description = xmlReader.Value;
                                    break;
                                case "prefix":
                                    languageFile.Prefix = xmlReader.Value.ToLower();
                                    break;
                                case "languagegroup":
                                    languageFile.LanguageGroup = xmlReader.Value.ToLower();
                                    break;
                            }

                            break;
                    }
                }

                return languageFile;
            }
            catch (Exception e)
            {
                Log.Error("Could not load language file " + languageFilePath, e);
            }
            return null;
        }

        public static bool HasKey(string prefix, string key)
        {
            return HasKey(prefix + "." + key);
        }

        public static bool HasKey(string key)
        {
            if (key == null)
            {
                return false;
            }

            return Resources.ContainsKey(key);
        }

        public static bool TryGetString(string key, out string languageString)
        {
            return Resources.TryGetValue(key, out languageString);
        }

        public static bool TryGetString(string prefix, string key, out string languageString)
        {
            return Resources.TryGetValue(prefix + "." + key, out languageString);
        }

        public static bool TryGetString(string prefix, Enum key, out string languageString)
        {
            return Resources.TryGetValue(prefix + "." + key, out languageString);
        }

        public static string Translate(object key)
        {
            string typename = key.GetType().Name;
            string enumKey = typename + "." + key;
            if (HasKey(enumKey))
            {
                return GetString(enumKey);
            }

            return key.ToString();
        }

        public static string GetString(Enum key)
        {
            if (key == null)
            {
                return null;
            }

            return GetString(key.ToString());
        }

        public static string GetString(string prefix, Enum key)
        {
            if (key == null)
            {
                return null;
            }

            return GetString(prefix + "." + key);
        }

        public static string GetString(string prefix, string key)
        {
            return GetString(prefix + "." + key);
        }

        public static string GetString(string key)
        {
            if (key == null)
            {
                return null;
            }

            if (!Resources.TryGetValue(key, out var returnValue))
            {
                return "string ###" + key + "### not found";
            }

            return returnValue;
        }

        public static string GetFormattedString(Enum key, object param)
        {
            return GetFormattedString(key.ToString(), param);
        }

        public static string GetFormattedString(string prefix, Enum key, object param)
        {
            return GetFormattedString(prefix, key.ToString(), param);
        }

        public static string GetFormattedString(string prefix, string key, object param)
        {
            return GetFormattedString(prefix + "." + key, param);
        }

        public static string GetFormattedString(string key, object param)
        {
            if (!Resources.TryGetValue(key, out var returnValue))
            {
                return "string ###" + key + "### not found";
            }

            return string.Format(returnValue, param);
        }
    }
}
