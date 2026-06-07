using System;

namespace snapvox.foundation.core.AvaloniaShims
{
    public struct Message
    {
        public IntPtr HWnd { get; set; }
        public int Msg { get; set; }
        public IntPtr WParam { get; set; }
        public IntPtr LParam { get; set; }
        public IntPtr Result { get; set; }
    }
    
    public interface IMessageFilter
    {
        bool PreFilterMessage(ref Message m);
    }
}
