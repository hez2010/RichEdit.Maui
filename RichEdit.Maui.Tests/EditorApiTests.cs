using CodeEdit.Maui;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.UI.Text;

namespace RichEdit.Maui.Tests;

public class EditorApiTests
{
    private static Microsoft.UI.Xaml.Window? _selectionWindow;
    [Fact]
    public void SavePointsFollowUndoRedoAndSurviveHistoryClearing()
    {
        var document = RichTextDocument.FromPlainText("a");
        Assert.False(document.IsModified);
        document.Edit(edit => edit.InsertText(1, "b"), new(undoDescription: "Append b"));
        Assert.True(document.IsModified);
        Assert.Equal("Append b", document.UndoDescription);
        document.MarkSaved();
        var savedVersion = document.Version;
        document.Edit(edit => edit.InsertText(2, "c"));
        document.Undo();
        Assert.False(document.IsModified);
        Assert.True(document.Version > savedVersion);
        document.Redo();
        Assert.True(document.IsModified);
        document.ClearUndoHistory();
        Assert.True(document.IsModified);
        Assert.False(document.CanUndo);
        document.MarkSaved();
        Assert.False(document.IsModified);
    }

    [Fact]
    public void CompletingAnOlderSaveDoesNotMarkNewerEditsAsSaved()
    {
        var document = new CodeDocument("a");
        document.Edit(edit => edit.InsertText(1, "b"));
        var snapshot = document.CurrentSnapshot;
        var point = document.CreateSavePoint();
        document.Edit(edit => edit.InsertText(2, "c"));
        document.MarkSaved(point);
        Assert.Equal("ab", snapshot.Text);
        Assert.True(document.IsModified);
        document.Undo();
        Assert.False(document.IsModified);
        document.Edit(edit => edit.InsertText(2, "different"));
        Assert.True(document.IsModified);
        Assert.False(document.CanRedo);
        Assert.Throws<ArgumentException>(() => new CodeDocument().MarkSaved(point));
    }

    [Fact]
    public void CapturedSavePointsAreNotMergedAway()
    {
        var document = new CodeDocument("a");
        document.Edit(edit => edit.InsertText(1, "b"));
        var saved = document.CreateSavePoint();
        document.Edit(edit => edit.InsertText(2, "c"), new(RichTextUndoBehavior.MergeWithPrevious));
        document.MarkSaved(saved);
        document.Undo();
        Assert.Equal("ab", document.Text);
        Assert.False(document.IsModified);
    }

    [Fact]
    public void UndoGroupsWithNoNetChangePreserveTheSavedState()
    {
        var document = new CodeDocument("a");
        using (document.BeginUndoGroup())
        {
            document.Edit(edit => edit.InsertText(1, "b"));
            document.Edit(edit => edit.DeleteText(new(1, 1)));
        }
        Assert.Equal("a", document.Text);
        Assert.False(document.CanUndo);
        Assert.False(document.IsModified);
    }

    [Fact]
    public Task DocumentReplacementDuringNotificationsIsRejectedBeforeDetaching() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new CodeEditor();
        var original = editor.Document;
        original.Changed += (_, _) => Assert.Throws<InvalidOperationException>(() => editor.Document = new("replacement"));
        original.Edit(edit => edit.InsertText(0, "committed"));
        Assert.Same(original, editor.Document);
        Assert.Equal("committed", editor.Document.Text);
    });

    [Fact]
    public Task SelectionDirectionRoundTripsThroughTheControlAndNativeEditor() => WindowsTestHost.RunAsync(async () =>
    {
        using var fixture = new RichFixture(RichTextDocument.FromPlainText("abcdef"));
        var window = _selectionWindow ??= new Microsoft.UI.Xaml.Window();
        window.Content = fixture.Handler.PlatformView;
        try
        {
            window.Activate();
            await Task.Delay(60);
            fixture.Handler.PlatformView.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            fixture.Editor.SelectionState = new(5, 1);
            Assert.Equal(new RichTextRange(1, 4), fixture.Editor.SelectedRange);
            Assert.True((fixture.Handler.PlatformView.Document.Selection.Options & SelectionOptions.StartActive) != 0);
            var native = fixture.Handler.PlatformView.Document.Selection;
            native.SetRange(2, 4);
            native.Options |= SelectionOptions.StartActive;
            await Task.Delay(40);
            Assert.Equal(new RichTextSelectionState(4, 2), fixture.Editor.SelectionState);
            fixture.Editor.SelectionState = new(1, 5);
            Assert.False((native.Options & SelectionOptions.StartActive) != 0);
            Assert.Equal(5, fixture.Editor.Selection.Active);
        }
        finally { window.Content = null; }
    });

    [Fact]
    public Task CodeSelectionBindingsPreserveTheActiveEndpoint() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new CodeEditor { Document = new CodeDocument("abc\nxyz") };
        editor.SelectionState = new(6, 1);
        Assert.Equal(new CodePosition(1, 2), editor.CaretPosition);
        Assert.Equal(new RichTextSelectionState(6, 1), editor.GetValue(CodeEditor.SelectionStateProperty));
        editor.SetValue(CodeEditor.SelectionStateProperty, new RichTextSelectionState(1, 6));
        Assert.Equal(new CodePosition(2, 3), editor.CaretPosition);
        Assert.Equal(new RichTextRange(1, 5), editor.SelectedRange);
        Assert.Equal(6, editor.GetOffset(editor.GetPosition(6)));
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.GetOffset(new CodePosition(2, 5)));
    });

    [Fact]
    public Task DocumentHistoryRestoresSelectionAndWorksAfterDetachment() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new CodeEditor { Document = new CodeDocument("abcdef") };
        var document = editor.Document;
        editor.SelectionState = new(4, 1);
        editor.Selection.ReplaceText("X");
        Assert.Equal(new RichTextSelectionState(2, 2), editor.SelectionState);
        editor.SelectionState = new(0, 0);
        document.Undo();
        Assert.Equal("abcdef", document.Text);
        Assert.Equal(new RichTextSelectionState(4, 1), editor.SelectionState);
        editor.SelectionState = new(6, 6);
        document.Redo();
        Assert.Equal(new RichTextSelectionState(2, 2), editor.SelectionState);
        editor.Document = new CodeDocument("other");
        document.Undo();
        Assert.Equal("abcdef", document.Text);
        Assert.Equal("other", editor.Document.Text);
    });

    [Fact]
    public Task InvalidResultingSelectionRollsBackTheWholeEdit() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new RichEditor { Document = RichTextDocument.FromPlainText("abc") };
        var before = editor.Document.CurrentSnapshot;
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.Selection.Edit(
            edit => edit.InsertText(3, "x"), new RichTextSelectionState(50, 50)));
        Assert.Same(before, editor.Document.CurrentSnapshot);
        Assert.False(editor.Document.CanUndo);
        Assert.False(editor.Document.IsModified);
    });

    [Fact]
    public Task DocumentObserversSeeSynchronizedNativeAndSelectionStateInDefinedOrder() => WindowsTestHost.RunAsync(() =>
    {
        var document = RichTextDocument.FromPlainText("abcdef");
        var events = new List<string>();
        RichFixture? fixture = null;
        document.Changed += (_, _) =>
        {
            events.Add("document");
            Assert.Equal("f", fixture!.NativeText);
            Assert.Equal(new RichTextSelectionState(0, 1), fixture.Editor.SelectionState);
            Assert.Throws<InvalidOperationException>(() => document.Edit(edit => edit.InsertText(0, "recursive")));
        };
        using (fixture = new RichFixture(document))
        {
            fixture.Editor.SelectionState = new(4, 6);
            fixture.Editor.ContentChanged += (_, _) => events.Add("content");
            fixture.Editor.TextChanged += (_, _) => events.Add("text");
            document.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(document.Text)) events.Add("property"); };
            fixture.Editor.SelectionChanged += (_, _) => events.Add("selection");
            fixture.Editor.SelectionFormatChanged += (_, _) => events.Add("format");
            document.Edit(edit => edit.DeleteText(new RichTextRange(0, 5)));
            Assert.Equal(["document", "content", "text", "property", "selection", "format"], events);
        }
    });

    [Fact]
    public Task WorkerThreadMutationFailsBeforeChangingManagedOrNativeState() => WindowsTestHost.RunAsync(async () =>
    {
        using var fixture = new RichFixture(RichTextDocument.FromPlainText("abc"));
        var document = fixture.Editor.Document;
        var before = document.CurrentSnapshot;
        await Task.Run(() =>
        {
            Assert.Equal("abc", before.Text);
            Assert.Throws<InvalidOperationException>(() => document.Edit(edit => edit.DeleteText(new(0, 3))));
            Assert.Throws<InvalidOperationException>(document.MarkSaved);
            Assert.Throws<InvalidOperationException>(() => fixture.Editor.SelectionState = new(1, 2));
        });
        Assert.Same(before, document.CurrentSnapshot);
        Assert.Equal("abc", fixture.NativeText);
        Assert.False(document.IsModified);
    });

    [Fact]
    public Task DecorationsComposeAndNeverEnterContentOrSavedState() => WindowsTestHost.RunAsync(async () =>
    {
        using var fixture = new RichFixture(RichTextDocument.FromPlainText("abc"));
        var editor = fixture.Editor;
        var document = editor.Document;
        var snapshot = document.CurrentSnapshot;
        var rtf = document.RtfText;
        var changes = 0;
        document.Changed += (_, _) => changes++;
        using var foreground = editor.Decorations.CreateLayer();
        using var background = editor.Decorations.CreateLayer();
        foreground.Set([new(new(0, 3), new() { ForegroundColor = Colors.Green })]);
        background.Set([new(new(1, 1), new() { BackgroundColor = Colors.Yellow })]);
        Assert.Equal(Colors.Green, fixture.Foreground(1));
        Assert.Equal(Windows.UI.Color.FromArgb(255, 255, 255, 0), fixture.Handler.PlatformView.Document.GetRange(1, 2).CharacterFormat.BackgroundColor);
        editor.SelectionState = new(2, 0);
        await Task.Delay(80);
        Assert.Same(snapshot, document.CurrentSnapshot);
        Assert.Equal(rtf, document.RtfText);
        Assert.Equal(0, changes);
        Assert.False(document.IsModified);
        Assert.False(document.CanUndo);
        foreground.Clear();
        Assert.NotEqual(Colors.Green, fixture.Foreground(1));
        Assert.Equal(Windows.UI.Color.FromArgb(255, 255, 255, 0), fixture.Handler.PlatformView.Document.GetRange(1, 2).CharacterFormat.BackgroundColor);
    });

    [Fact]
    public Task NativeTypingWithinADecorationStaysPlainAndRetainsPresentation() => WindowsTestHost.RunAsync(async () =>
    {
        using var fixture = new RichFixture(RichTextDocument.FromPlainText("abc"));
        var editor = fixture.Editor;
        using var layer = editor.Decorations.CreateLayer();
        layer.Set([new(new(0, 3), new() { ForegroundColor = Colors.Green })]);
        editor.SelectionState = new(1, 1);
        fixture.Handler.PlatformView.Document.Selection.TypeText("x");
        await Task.Delay(100);
        Assert.Equal("axbc", editor.Document.Text);
        Assert.Equal(Colors.Green, fixture.Foreground(1));
        Assert.All(editor.Document.CurrentSnapshot.Runs, run => Assert.Null(run.Format.ForegroundColor));
        editor.Document.Undo();
        Assert.Equal("abc", editor.Document.Text);
        Assert.False(editor.Document.IsModified);
    });

    [Fact]
    public Task ClipboardCommandExceptionsPropagateUnchanged()
    {
        var expected = new InvalidOperationException("Clipboard failure");
        return Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            try { await RichEditorCommands.ExecuteAsync(() => Task.FromException(expected)); }
            catch (Exception actual) { Assert.Same(expected, actual); throw; }
        });
    }

    private sealed class RichFixture : IDisposable
    {
        internal RichEditor Editor { get; }
        internal RichEditorHandler Handler { get; } = new();
        internal RichFixture(RichTextDocument document)
        {
            Editor = new RichEditor { Document = document };
            Handler.SetMauiContext(new MauiContext(WindowsTestHost.MauiApp.Services));
            Editor.Handler = Handler;
        }
        internal string NativeText
        {
            get
            {
                Handler.PlatformView.Document.GetText(TextGetOptions.None, out var text);
                return text.TrimEnd('\r').Replace('\r', '\n');
            }
        }
        internal Color Foreground(int position)
        {
            var color = Handler.PlatformView.Document.GetRange(position, position + 1).CharacterFormat.ForegroundColor;
            return Color.FromRgba(color.R, color.G, color.B, color.A);
        }
        public void Dispose()
        {
            Editor.Handler = null;
            ((IElementHandler)Handler).DisconnectHandler();
        }
    }
}
