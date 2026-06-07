using System;

namespace snapvox.foundation.core.AvaloniaShims
{
    public enum DialogResult { None, OK, Cancel, Abort, Retry, Ignore, Yes, No }
    public enum MessageBoxButtons { OK, OKCancel, AbortRetryIgnore, YesNoCancel, YesNo, RetryCancel }
    public enum MessageBoxIcon { None, Hand, Question, Exclamation, Asterisk, Stop, Error, Warning, Information }
    public static class MessageBox
    {
        public static DialogResult Show(string text, string caption) => DialogResult.OK;
        public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon) => DialogResult.OK;
        public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon) => DialogResult.OK;
    }
}
