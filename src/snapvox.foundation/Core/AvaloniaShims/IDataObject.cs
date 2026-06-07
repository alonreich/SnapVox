using System;

namespace snapvox.foundation.core.AvaloniaShims
{
    public interface IDataObject
    {
        object GetData(string format);
        object GetData(string format, bool autoConvert);
        object GetData(Type format);
        void SetData(string format, object data);
        void SetData(Type format, object data);
        void SetData(object data);
        bool GetDataPresent(string format);
        bool GetDataPresent(string format, bool autoConvert);
        bool GetDataPresent(Type format);
        string[] GetFormats();
        string[] GetFormats(bool autoConvert);
    }
}
