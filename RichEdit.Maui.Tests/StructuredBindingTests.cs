using RichEdit.Core;
using Microsoft.Maui;
using Microsoft.UI.Text;

namespace RichEdit.Maui.Tests;

public sealed class StructuredBindingTests
{
    [Fact]
    public Task NativeTypingPreservesRichStylesAcrossMultipleCaretsAndSharedUndo() => WindowsTestHost.RunAsync(async () =>
    {
        var document = new RichDocument(RichDocumentSnapshot.Build(edit =>
        {
            edit.SetStyles(new((RichStyleDefinition[])[new(new(1), "Strong", RichStyleKind.Paragraph) { Character = new() { Bold = true } }]));
            edit.SetParagraphStyle(new(2), new(1));
            edit.ReplaceText(new(edit.Body, new(2), 0), 0, "abcd");
        }));
        var view = new NativeEditorView();
        view.BindStructuredDocument(document);
        var handler = new RichEditorHandler();
        handler.SetMauiContext(new MauiContext(WindowsTestHost.MauiApp.Services));
        view.Handler = handler;
        try
        {
            view.Selections = new((TextSelection[])[new(1, 1), new(3, 3)], 1);
            handler.PlatformView.Document.Selection.TypeText("X");
            await Task.Delay(30);
            Assert.Equal("aXbcXd", document.CurrentSnapshot.ProjectText().Text.ToString());
            Assert.All(document.CurrentSnapshot.Nodes.Values.OfType<RichText>(), run => Assert.Equal(new RichCharacterStyle(), run.Format));
            Assert.True(document.CurrentSnapshot.ResolveCharacter(document.CurrentSnapshot.Get<RichParagraph>(new(2)).Inlines[0]).Bold);
            view.Undo();
            Assert.Equal("abcd", document.CurrentSnapshot.ProjectText().Text.ToString());
            Assert.Equal(2, view.Selections.Count);
            Assert.Equal(1, view.Selections[0].Active);
            Assert.Equal(3, view.Selections[1].Active);
            handler.PlatformView.Document.GetText(TextGetOptions.None, out var native);
            Assert.Equal("abcd\r", native);
            view.SelectedRange = new(4, 0);
            view.StructuredSession!.TypingCharacterFormat = new() { Italic = true, Bold = false };
            handler.PlatformView.Document.Selection.TypeText("Y");
            await Task.Delay(30);
            var typed = document.CurrentSnapshot.Nodes.Values.OfType<RichText>().Single(run => run.Content.ToString() == "Y");
            Assert.Equal(new RichCharacterStyle { Italic = true, Bold = false }, typed.Format);
        }
        finally { view.Handler = null; ((IElementHandler)handler).DisconnectHandler(); }
    });

    [Fact]
    public Task NativeCompositionStagesRichRootsThenPublishesOneCommit() => WindowsTestHost.RunAsync(async () =>
    {
        var document = RichDocument.FromPlainText("ab");
        var view = new NativeEditorView();
        view.BindStructuredDocument(document);
        var handler = new RichEditorHandler();
        handler.SetMauiContext(new MauiContext(WindowsTestHost.MauiApp.Services));
        view.Handler = handler;
        var composing = typeof(RichEditorHandler).GetField("_isComposing", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var observe = typeof(RichEditorHandler).GetMethod("UpdateCompositionState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        try
        {
            view.SelectedRange = new(1, 0);
            var published = 0;
            document.Changed += (_, _) => published++;
            composing.SetValue(handler, true);
            observe.Invoke(handler, null);
            handler.PlatformView.Document.Selection.TypeText("日");
            await Task.Delay(30);
            Assert.Equal("ab", document.CurrentSnapshot.ProjectText().Text.ToString());
            Assert.Equal("a日b", view.StructuredSession!.Composition!.Snapshot.ProjectText().Text.ToString());
            Assert.Equal(0, published);
            typeof(RichEditorHandler).GetMethod("ReconcileCompositionSource", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(handler, null);
            composing.SetValue(handler, false);
            observe.Invoke(handler, null);
            Assert.Equal("a日b", document.CurrentSnapshot.ProjectText().Text.ToString());
            Assert.Equal(1, published);
            view.Undo();
            Assert.Equal("ab", document.CurrentSnapshot.ProjectText().Text.ToString());
            Assert.False(document.CanUndo);
        }
        finally { composing.SetValue(handler, false); view.DiscardComposition(); view.Handler = null; ((IElementHandler)handler).DisconnectHandler(); }
    });

    [Fact]
    public Task NativeSurfaceUsesOneRichAuthorityAndMapsPeerCaretsBeforePublication() => WindowsTestHost.RunAsync(() =>
    {
        var document = RichDocument.FromPlainText("abc");
        var first = new NativeEditorView();
        var second = new NativeEditorView();
        first.BindStructuredDocument(document);
        second.BindStructuredDocument(document);
        first.SelectedRange = new(1, 0);
        second.SelectedRange = new(3, 0);
        var publications = 0;
        document.Changed += (_, _) =>
        {
            publications++;
            Assert.Equal("aXbc", first.Document.Text);
            Assert.Equal("aXbc", second.Document.Text);
            Assert.Equal(new TextSpan(4, 0), second.SelectedRange);
        };
        first.ReplaceSelections("X");
        Assert.Equal(1, publications);
        Assert.Equal("aXbc", document.CurrentSnapshot.ProjectText().Text.ToString());
        Assert.True(document.CanUndo);
    });

    [Fact]
    public Task RichSessionSelectionAndGlobalHistoryDriveNativeSurfaceSelection() => WindowsTestHost.RunAsync(() =>
    {
        var document = RichDocument.FromPlainText("abc");
        var first = new NativeEditorView();
        var second = new NativeEditorView();
        first.BindStructuredDocument(document);
        second.BindStructuredDocument(document);
        first.StructuredSession!.Selection = new RichEdit.Core.RichTextSelection(new(new(1), new(2), 1), new(new(1), new(2), 2));
        Assert.Equal(new TextSpan(1, 1), first.SelectedRange);
        first.ReplaceSelections("Q\nR");
        Assert.Equal("aQ\nRc", document.CurrentSnapshot.ProjectText().Text.ToString());
        second.Undo();
        Assert.Equal("abc", first.Document.Text);
        Assert.Equal(new TextSpan(1, 1), second.SelectedRange);
        Assert.False(document.CanUndo);
        second.Redo();
        Assert.Equal("aQ\nRc", second.Document.Text);
        Assert.Equal(new TextSpan(4, 0), second.SelectedRange);
    });

    [Fact]
    public Task RichCompositionFreezesCanonicalStateAndSharesTheLease() => WindowsTestHost.RunAsync(() =>
    {
        var document = RichDocument.FromPlainText("abc");
        var view = new NativeEditorView();
        view.BindStructuredDocument(document);
        view.SelectedRange = new(1, 0);
        view.SetCompositionState(new(true, new(1, 1)));
        Assert.NotNull(view.StructuredSession!.Composition);
        Assert.Throws<InvalidOperationException>(() => document.CaptureSave());
        Assert.False(document.TryEdit(document.Revision, edit => edit.ReplaceText(new(edit.Body, new(2), 0), 0, "blocked")).Accepted);
        view.SetCompositionState(default);
        Assert.Null(view.StructuredSession.Composition);
        Assert.Equal("abc", document.CurrentSnapshot.ProjectText().Text.ToString());
        Assert.False(document.CanUndo);
    });
}
