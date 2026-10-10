using Microsoft.Maui;

namespace RichEdit.Maui.Tests;

internal static partial class EditorContractTests
{
    private static IEnumerable<Case> SharedDocumentCases()
    {
        yield return new("sharing edits synchronize every native view before observers", editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("abcdef");
            using var peer = new SharedEditor(editor);
            var other = peer.Editor;
            var document = editor.Document;
            var notifications = 0;
            document.Changed += (_, _) =>
            {
                Equal(document.Text, NativeText(editor), "origin native content");
                Equal(document.Text, NativeText(other), "peer native content");
                notifications++;
            };

            editor.SelectionState = new(1, 3);
            other.SelectionState = new(5, 5);
            editor.Selection.ReplaceText("X");
            Equal("aXdef", document.Text);
            Equal(new TextSelectionState(4, 4), other.SelectionState, "peer selection maps independently");

            other.Undo();
            Equal("abcdef", document.Text);
            Equal(new TextSelectionState(1, 3), other.SelectionState, "invoker restores history selection");
            Equal(new TextSelectionState(3, 3), editor.SelectionState, "other pane maps current caret");

            other.Redo();
            Equal("aXdef", document.Text);
            Equal(new TextSelectionState(2, 2), other.SelectionState);
            Equal(3, notifications, "one shared commit per operation");
            return Task.CompletedTask;
        });

        yield return new("sharing native input from either view uses one document history", editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("abc");
            using var peer = new SharedEditor(editor);
            editor.SelectedRange = new(0, 0);
            peer.Editor.SelectedRange = new(3, 0);
            NativeReplace(editor, "x");
            Equal("xabc", NativeText(peer.Editor));
            Equal(new TextSpan(4, 0), peer.Editor.SelectedRange);
            NativeReplace(peer.Editor, "y");
            Equal("xabcy", NativeText(editor));
            editor.Undo();
            Equal("xabc", NativeText(editor));
            Equal("xabc", NativeText(peer.Editor));
            editor.Undo();
            Equal("abc", NativeText(peer.Editor));
            Equal(false, editor.Document.CanUndo, "view changes break native typing coalescing");
            return Task.CompletedTask;
        });

        yield return new("sharing detach preserves history and document undo maps selections", editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("abc");
            using var peer = new SharedEditor(editor);
            var shared = editor.Document;
            editor.SelectedRange = new(1, 0);
            editor.Selection.ReplaceText("X");
            peer.Editor.SelectedRange = new(4, 0);
            shared.Undo();
            Equal(new TextSpan(3, 0), peer.Editor.SelectedRange, "document undo does not restore another view's selection");
            editor.Document = RichTextDocument.FromPlainText("separate");
            peer.Editor.Redo();
            Equal("aXbc", shared.Text);
            Equal("aXbc", NativeText(peer.Editor));
            Equal("separate", NativeText(editor));
            return Task.CompletedTask;
        });
    }

    private sealed partial class SharedEditor : IDisposable
    {
        internal NativeEditorView Editor { get; }
        private readonly RichEditorHandler _handler;

        internal SharedEditor(NativeEditorView first)
        {
            Editor = new NativeEditorView { Document = first.Document };
            _handler = new RichEditorHandler();
            _handler.SetMauiContext(first.Handler!.MauiContext!);
            Editor.Handler = _handler;
        }

        public void Dispose()
        {
            Editor.Handler = null;
            ((IElementHandler)_handler).DisconnectHandler();
            Editor.Document = new RichTextDocument();
        }
    }
}
