using snapvox.native;
using snapvox.native.foundation;
using snapvox.native.graphics;
using snapvox.native.ui;
using System;

namespace snapvox.foundation.IniFile
{

    [AttributeUsage(AttributeTargets.Class)]
    public class IniSectionAttribute : Attribute
    {
        public IniSectionAttribute(string name)
        {
            Name = name;
        }

        public string Description;
        public string Name { get; set; }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class IniPropertyAttribute : Attribute
    {
        public IniPropertyAttribute()
        {
            Separator = ",";
        }

        public IniPropertyAttribute(string name) : this()
        {
            Name = name;
        }

        public string Description { get; set; }
        public string Separator { get; set; }
        public string DefaultValue { get; set; }

        public string LanguageKey { get; set; }

        public bool Encrypted { get; set; }
        public bool FixedValue { get; set; }
        public bool Expert { get; set; }
        public bool ExcludeIfNull { get; set; }

        public string Name { get; set; }
    }
}
