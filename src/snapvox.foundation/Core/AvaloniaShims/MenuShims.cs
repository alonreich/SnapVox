using System;
using System.Collections.Generic;
using snapvox.foundation.core.AvaloniaShims;

namespace snapvox.foundation.core.AvaloniaShims
{
    public class ToolStripItem
    {
        public string Name { get; set; }
    }

    public class ToolStripMenuItem : ToolStripItem
    {
        public string Text { get; set; }
        public Image Image { get; set; }
        public Keys ShortcutKeys { get; set; }
        public bool Checked { get; set; }
        public List<ToolStripItem> DropDownItems { get; } = new List<ToolStripItem>();
    }

    public class ContextMenuStrip
    {
        public List<ToolStripItem> Items { get; } = new List<ToolStripItem>();
    }
}
