using RichEdit.Maui;

namespace CodeEdit.Maui;

// Adapts public native APIs for viewport geometry and code-specific keyboard behavior.
// Document content, formatting, selection, clipboard, and history stay with NativeEditorView.
internal sealed partial class CodeEditorNativeAdapter : IDisposable
{
    private readonly CodeEditor Owner;
    private readonly RichEditorHandler _handler;
    private bool _disposed;
    private (int Size, string Font, double Points, bool Wrap, int Width)? _tabConfiguration;

    internal CodeEditorNativeAdapter(CodeEditor owner, RichEditorHandler handler)
    {
        Owner = owner;
        _handler = handler;
        Connect();
        UpdateConfiguration();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Disconnect();
        _disposed = true;
    }

    private partial void Connect();

    private partial void Disconnect();

    internal partial Color? GetTextColor();

    internal partial bool Post(Action action);

    internal void UpdateConfiguration()
    {
        UpdateConfigurationCore();
        Owner.TextView.SourceWordWrap = Owner.WordWrap;
        var requested = (Owner.TabSize, Owner.FontFamily, Owner.FontSize, Owner.WordWrap,
            Owner.WordWrap ? (int)Math.Ceiling(Math.Max(0, Owner.TextView.Width)) : 0);
        if (_tabConfiguration != requested && _handler.UpdateSourceTabSize(Owner.TabSize)) _tabConfiguration = requested;
    }

    private partial void UpdateConfigurationCore();

}
