using RichEdit.Core;
using Microsoft.Maui;

namespace RichEdit.Maui.Tests;

public sealed class RichEditorControlTests
{
    [Fact]
    public Task PublicControlOwnsAnIndependentTypedSessionAndNativeEditingUsesTheRichDocument() => WindowsTestHost.RunAsync(() =>
    {
        var document = RichDocument.FromPlainText("abcd");
        var first = new RichEditor { Document = document, FontSize = 18 };
        var second = new RichEditor { Document = document };
        Assert.NotSame(first.Session, second.Session);
        var handler = new RichEditorHandler();
        handler.SetMauiContext(new MauiContext(WindowsTestHost.MauiApp.Services));
        first.NativeView.Handler = handler;
        try
        {
            RichTextSelectionItem Caret(int offset) => new(new(new(new(1), new(2), offset), new(new(1), new(2), offset)));
            var selection = new RichTextSelectionSet(document.CurrentSnapshot, (RichTextSelectionItem[])[Caret(1), Caret(3)], 1);
            first.Selection = selection;
            Assert.Equal(selection, first.GetValue(RichEditor.SelectionProperty));
            first.ToggleItalic();
            handler.PlatformView.Document.Selection.TypeText("X");
            Assert.Equal("aXbcXd", document.CurrentSnapshot.ProjectText().Text.ToString());
            Assert.Equal(2, Assert.IsType<RichTextSelectionSet>(first.Selection).Count);
            Assert.All(document.CurrentSnapshot.Nodes.Values.OfType<RichText>().Where(run => run.Content.ToString() == "X"),
                run => Assert.True(document.CurrentSnapshot.ResolveCharacter(run.Id).Italic));
            second.Undo();
            Assert.Equal("abcd", document.CurrentSnapshot.ProjectText().Text.ToString());
            Assert.Equal(selection, second.Selection);
            Assert.Equal(selection, second.GetValue(RichEditor.SelectionProperty));
            first.IsReadOnly = true;
            Assert.True(first.Session.IsReadOnly);
            Assert.False(first.ReplaceSelection("blocked").Accepted);
            first.Session.IsReadOnly = false;
            Assert.False(first.IsReadOnly);
            Assert.False(first.NativeView.IsReadOnly);
            second.ReplaceSelection("!");
            Assert.Equal("a!bc!d", document.CurrentSnapshot.ProjectText().Text.ToString());
        }
        finally { first.NativeView.Handler = null; ((IElementHandler)handler).DisconnectHandler(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task RebindingAndSaveAcknowledgementUpdatePublicPropertiesWithoutChangingTheOtherView() => WindowsTestHost.RunAsync(() =>
    {
        var document = RichDocument.FromPlainText("text");
        var editor = new RichEditor { Document = document };
        var peer = new RichEditor { Document = document };
        var dirtyNotifications = 0;
        editor.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(RichEditor.IsModified)) dirtyNotifications++; };
        editor.ReplaceSelection("X");
        Assert.True(editor.IsModified);
        var before = dirtyNotifications;
        document.MarkSaved(document.CaptureSave());
        Assert.False(editor.IsModified);
        Assert.True(dirtyNotifications > before);
        var session = editor.Session;
        RichDocumentReplacedEventArgs? replaced = null;
        editor.DocumentChanged += (_, args) => replaced = args;
        var replacement = RichDocument.FromPlainText("other");
        editor.SetValue(RichEditor.DocumentProperty, replacement);
        Assert.Same(replacement, editor.Document);
        Assert.Same(document, peer.Document);
        Assert.Same(document, replaced!.Previous);
        Assert.Same(replacement, replaced.Current);
        Assert.NotSame(session, editor.Session);
        Assert.Equal(editor.Selection, editor.GetValue(RichEditor.SelectionProperty));
        Assert.Throws<ObjectDisposedException>(() => session.ReplaceSelection("detached"));
        Assert.False(editor.IsModified);
        return Task.CompletedTask;
    });
}
