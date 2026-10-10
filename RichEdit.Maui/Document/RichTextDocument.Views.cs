namespace RichEdit.Maui;

public sealed partial class RichTextDocument
{
    private readonly List<WeakReference<NativeEditorView>> _views = [];

    internal bool HasActiveComposition => GetAttachedEditors().Any(IsComposing);

    internal bool TryCompleteComposition()
    {
        VerifyAccess();
        foreach (var view in GetAttachedEditors())
            if (IsComposing(view) && (view.Handler is not IRichEditorHandler handler || !handler.TryCompleteComposition()))
                return false;
        return !HasActiveComposition;
    }

    private static bool IsComposing(NativeEditorView view) => view.Composition.IsActive ||
        view.Handler is IRichEditorHandler { IsComposing: true };

    private void VerifyCompositionAccess(object? nativeSourceToken)
    {
        foreach (var view in GetAttachedEditors())
            if (IsComposing(view) && (nativeSourceToken is null || view.Handler is not IRichEditorHandler handler ||
                !ReferenceEquals(handler.SourceToken, nativeSourceToken)))
                throw new InvalidOperationException("The document is being composed in an attached editor.");
    }

    internal void VerifyAccess()
    {
        SourceDocument?.VerifyAccess();
        StructuredDocument?.VerifyAccess();
        foreach (var view in _views)
            if (view.TryGetTarget(out var editor))
            {
                editor.VerifyAccess();
                return;
            }
    }

    internal bool CanAttachEditor(object editor)
    {
        VerifyAccess();
        return editor is NativeEditorView;
    }

    internal void VerifyCanAttachEditor(object editor)
    {
        VerifyNoActiveEdit();
        ArgumentNullException.ThrowIfNull(editor);
        if (editor is not NativeEditorView view)
            throw new ArgumentException("A document view must be a rich editor.", nameof(editor));

        view.VerifyAccess();
    }

    internal void AttachEditor(object editor)
    {
        VerifyCanAttachEditor(editor);
        var view = (NativeEditorView)editor;
        if (!GetAttachedEditors().Contains(view))
            _views.Add(new(view));
    }

    internal void DetachEditor(object editor)
    {
        VerifyAccess();
        _views.RemoveAll(reference => !reference.TryGetTarget(out var view) || ReferenceEquals(view, editor));
    }

    private NativeEditorView[] GetAttachedEditors()
    {
        VerifyAccess();
        _views.RemoveAll(static reference => !reference.TryGetTarget(out _));
        return _views.Select(static reference => reference.TryGetTarget(out var editor) ? editor : null)
            .OfType<NativeEditorView>().ToArray();
    }

    private NativeEditorView? FindEditingView(object? sourceToken)
    {
        if (sourceToken is null)
            return null;

        return GetAttachedEditors().FirstOrDefault(editor =>
            ReferenceEquals(editor.EditSourceToken, sourceToken) ||
            editor.Handler is IRichEditorHandler handler && ReferenceEquals(handler.SourceToken, sourceToken));
    }
}
