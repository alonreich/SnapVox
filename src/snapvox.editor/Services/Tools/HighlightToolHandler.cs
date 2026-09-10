#nullable enable
using snapvox.editor.forms;

namespace snapvox.editor.Services.Tools
{
    public sealed class HighlightToolHandler : ShapeToolHandler
    {
        public override EditorTool Tool => EditorTool.Highlight;
    }
}
