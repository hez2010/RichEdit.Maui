namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    // WinUI collapses hidden characters in its native layout. The view-owned
    // decoration layer restores the authored Hidden value on native readback.
    private partial void ApplyFoldingCore() { }
}
