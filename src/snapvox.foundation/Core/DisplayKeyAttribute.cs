using snapvox.native;
using snapvox.native.foundation;
using snapvox.native.graphics;
using snapvox.native.ui;
using System;

namespace snapvox.foundation.core
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class DisplayKeyAttribute : Attribute
    {
        public string Value { get; }

        public DisplayKeyAttribute(string v)
        {
            Value = v;
        }

        public DisplayKeyAttribute()
        {
        }
    }
}
