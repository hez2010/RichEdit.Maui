using RichEdit.Maui;

namespace CodeEdit.Maui;

// Adapts public native APIs for viewport geometry and code-specific keyboard behavior.
// Document content, formatting, selection, clipboard, and history stay with RichEditor.
internal sealed partial class CodeEditorNativeAdapter : IDisposable
{
    private readonly CodeEditor Owner;
    private readonly RichEditorHandler _handler;
    private bool _disposed;

    internal CodeEditorNativeAdapter(CodeEditor owner, RichEditorHandler handler)
    {
        Owner = owner;
        _handler = handler;
        Connect();
        UpdateConfiguration();
    }

    public void Dispose()
    {
        if (_disposed) return;
        Disconnect();
        _disposed = true;
    }
    private partial void Connect();
    private partial void Disconnect();
    internal partial bool IsComposing { get; }
    internal partial Color? GetTextColor();
    internal partial bool Post(Action action);
    internal partial void UpdateConfiguration();
    internal partial void ScrollToSelection();
    internal partial IReadOnlyList<VisibleCodeLine> GetVisibleLines();

    private int? GetVisibleLineStart(int index)
    {
        var range = Owner.Lines.GetRange(index);
        if (Owner.Folding.GetCollapsedRange(range.Start) is not { } folded) return range.Start;
        return folded.End <= range.End ? folded.End : null;
    }
}
