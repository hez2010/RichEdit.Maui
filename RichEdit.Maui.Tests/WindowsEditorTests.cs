using Microsoft.Maui;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Graphics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Clipboard = Windows.ApplicationModel.DataTransfer.Clipboard;
using DataPackage = Windows.ApplicationModel.DataTransfer.DataPackage;

namespace RichEdit.Maui.Tests;

[Collection("Native editor")]
public class WindowsEditorTests
{
    private static Microsoft.UI.Xaml.Window? _sampleWindow;
    [Fact]
    public Task FocusingTheSampleDocumentDoesNotCreateHistory() => WindowsTestHost.RunAsync(async () =>
    {
        using var source = typeof(WindowsEditorTests).Assembly.GetManifestResourceStream("EditorSampleSource")!;
        using var reader = new StreamReader(source);
        var sample = System.Text.RegularExpressions.Regex.Match(reader.ReadToEnd(), "(?s)Editor\\.Document = RichTextDocument\\.FromRtf\\(\"\"\"\\r?\\n(?<rtf>.*?)\\r?\\n(?<indent>[ \\t]*)\"\"\"\\);");
        Assert.True(sample.Success);
        var indent = sample.Groups["indent"].Value;
        var rtf = string.Join("\n", sample.Groups["rtf"].Value.Split('\n')
            .Select(line => line.StartsWith(indent, StringComparison.Ordinal) ? line[indent.Length..] : line));
        using var fixture = new EditorFixture();
        var editor = fixture.Editor;
        editor.FontSize = 17;
        editor.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("#212121");
        editor.Document = RichTextDocument.FromRtf(rtf);
        var focusTarget = new Microsoft.UI.Xaml.Controls.Button { Content = "Focus target" };
        var grid = new Microsoft.UI.Xaml.Controls.Grid();
        grid.RowDefinitions.Add(new Microsoft.UI.Xaml.Controls.RowDefinition());
        grid.RowDefinitions.Add(new Microsoft.UI.Xaml.Controls.RowDefinition { Height = Microsoft.UI.Xaml.GridLength.Auto });
        Microsoft.UI.Xaml.Controls.Grid.SetRow(focusTarget, 1);
        grid.Children.Add(fixture.Handler.PlatformView);
        grid.Children.Add(focusTarget);
        var window = _sampleWindow ??= new Microsoft.UI.Xaml.Window();
        window.Content = grid;
        var notifications = new List<string>();
        fixture.Handler.PlatformView.TextChanging += (_, args) => notifications.Add($"IsContentChanging={args.IsContentChanging}, version={editor.Document.Version}");
        try
        {
            window.Activate();
            focusTarget.Focus(FocusState.Programmatic);
            await Task.Delay(100);
            editor.ClearUndoHistory();
            var before = editor.Document.CurrentSnapshot;
            fixture.Handler.PlatformView.Focus(FocusState.Pointer);
            await Task.Delay(100);
            Assert.False(editor.CanUndo, string.Join("; ", notifications));
            Assert.True(before.ContentEquals(editor.Document.CurrentSnapshot), $"Before: {before.RtfText}\nAfter: {editor.Document.RtfText}");
        }
        finally { window.Content = null; }
    });

    [Fact]
    public Task ReplacingClipboardTextInvalidatesTheRichFragmentCache() => WindowsTestHost.RunClipboardAsync(async () =>
    {
        using var fixture = new EditorFixture();
        var editor = fixture.Editor;
        editor.Selection.ToggleBold();
        editor.Selection.ReplaceText("same text");
        editor.SelectAll();
        await editor.CopyAsync();
        var replacement = new DataPackage();
        replacement.SetText("same text");
        Clipboard.SetContent(replacement);
        editor.Document = new RichTextDocument();
        await editor.PasteAsync();
        Assert.False(Assert.Single(editor.Document.CurrentSnapshot.Runs).Format.Bold);
    });
    [Fact]
    public Task ClipboardWithinTheProcessPreservesFormattingNotRepresentableInRtf() => WindowsTestHost.RunClipboardAsync(async () =>
    {
        using var fixture = new EditorFixture();
        var editor = fixture.Editor;
        editor.Selection.ReplaceText("styled");
        editor.SelectAll();
        editor.Selection.UpdateCharacterFormat(format => format with
        {
            StyleName = "Custom style", Ligatures = RichTextFeatureMode.Disabled,
            Strikethrough = RichTextStrikethroughStyle.Double, StrikethroughColor = Colors.Red,
        });
        var expected = editor.Document.CurrentSnapshot.Runs[0].Format;
        await editor.CopyAsync();
        editor.Document = new RichTextDocument();
        await editor.PasteAsync();
        Assert.Equal(expected, Assert.Single(editor.Document.CurrentSnapshot.Runs).Format);
    });
    [Fact]
    public Task ImageCroppingIsProjectedThroughNativeRtf() => WindowsTestHost.RunAsync(() =>
    {
        using var fixture = new EditorFixture();
        var data = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        fixture.Editor.Selection.InsertImage(RichTextImage.FromBytes(0, "image/png", data, 12, 12) with { Crop = new RichTextImageCrop(1, 0, 0, 0) });
        fixture.Handler.PlatformView.Document.GetText(TextGetOptions.FormatRtf, out var rtf);
        Assert.Contains(@"\piccropl20", rtf);
        fixture.Editor.Undo();
        Assert.Empty(fixture.NativeText);
        fixture.Editor.Redo();
        fixture.Handler.PlatformView.Document.GetText(TextGetOptions.FormatRtf, out rtf);
        Assert.Contains(@"\piccropl20", rtf);
    });

    [Fact]
    public Task BareObjectCharactersKeepTheirLogicalPositions() => WindowsTestHost.RunAsync(() =>
    {
        using var fixture = new EditorFixture();
        fixture.Editor.Selection.ReplaceText("a\uFFFCb");
        Assert.Equal(fixture.Editor.Document.Text, fixture.NativeText);
        fixture.Editor.SelectedRange = new RichTextRange(3, 0);
        fixture.Handler.PlatformView.Document.Selection.SetText(TextSetOptions.None, "c");
        Assert.Equal("a\uFFFCbc", fixture.Editor.Document.Text);
    });
    [Fact]
    public Task NativePngImageKeepsItsObjectPosition() => WindowsTestHost.RunAsync(() =>
    {
        using var fixture = new EditorFixture();
        var data = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        fixture.Editor.Selection.InsertImage(RichTextImage.FromBytes(0, "image/png", data, 12, 12));
        Assert.Equal('\uFFFC', fixture.Handler.PlatformView.Document.GetRange(0, 1).Character);
        Assert.Equal(fixture.Editor.Document.Text, fixture.NativeText);
    });
    [Fact]
    public Task NativeFlyoutCopyAndCutUsePortableFields() => WindowsTestHost.RunClipboardAsync(async () =>
    {
        using var fixture = new EditorFixture();
        var editor = fixture.Editor;
        editor.Selection.InsertField("DATE", "today");
        editor.SelectAll();
        var flyout = new Microsoft.UI.Xaml.Controls.TextCommandBarFlyout();
        var copy = new Microsoft.UI.Xaml.Controls.AppBarButton { Command = new Microsoft.UI.Xaml.Input.StandardUICommand { Kind = Microsoft.UI.Xaml.Input.StandardUICommandKind.Copy } };
        var cut = new Microsoft.UI.Xaml.Controls.AppBarButton { Command = new Microsoft.UI.Xaml.Input.StandardUICommand { Kind = Microsoft.UI.Xaml.Input.StandardUICommandKind.Cut } };
        flyout.PrimaryCommands.Add(copy);
        flyout.PrimaryCommands.Add(cut);
        fixture.Handler.ConfigureTextFlyoutCommands(flyout);
        copy.Command.Execute(null);
        var copied = await RichTextClipboard.GetAsync();
        Assert.Equal("DATE", Assert.Single(copied!.Snapshot.Fields).Instruction);
        editor.ClearUndoHistory();
        cut.Command.Execute(null);
        Assert.Empty(editor.Document.Text);
        editor.Undo();
        Assert.Equal("today", fixture.NativeText);
        Assert.Single(editor.Document.CurrentSnapshot.Fields);
        Assert.False(editor.CanUndo);
    });

    [Theory]
    [InlineData("•")]
    [InlineData("✓")]
    public Task ListsAndImagesSurviveNativeProjectionAndHistory(string marker) => WindowsTestHost.RunAsync(() =>
    {
        using var fixture = new EditorFixture();
        var editor = fixture.Editor;
        editor.Selection.ReplaceText("one\ntwo\n");
        editor.SelectAll();
        editor.Selection.SetList(new RichTextListDefinition([
            new RichTextListLevelDefinition
            {
                Marker = new RichTextListMarker.Bullet(marker), Prefix = "", Suffix = "",
                LeadingIndent = 24, FirstLineIndent = -12, MarkerTab = 24,
            }]));
        Assert.Equal(editor.Document.Text, fixture.NativeText);
        editor.SelectedRange = new RichTextRange(3, 0);
        editor.Selection.InsertImage(RichTextImage.FromBytes(0, "image/png", [1, 2, 3], 12, 12));
        Assert.Equal('\uFFFC', fixture.Handler.PlatformView.Document.GetRange(3, 4).Character);
        Assert.Equal(editor.Document.Text, fixture.NativeText);
        var withImage = editor.Document.CurrentSnapshot;
        editor.Undo();
        Assert.Empty(editor.Document.CurrentSnapshot.Images);
        editor.Redo();
        Assert.True(withImage.ContentEquals(editor.Document.CurrentSnapshot));
        Assert.Equal(editor.Document.Text, fixture.NativeText);
        editor.SelectAll();
        editor.Selection.ClearList();
        Assert.All(editor.Document.CurrentSnapshot.Paragraphs, paragraph => Assert.Null(paragraph.Format.List));
        editor.Undo();
        Assert.True(withImage.ContentEquals(editor.Document.CurrentSnapshot));
    });

    [Fact]
    public Task NativeSelectionAndEditsMapHyperlinkPositions() => WindowsTestHost.RunAsync(() =>
    {
        using var fixture = new EditorFixture();
        var editor = fixture.Editor;
        editor.Selection.ReplaceText("before link after");
        editor.SelectedRange = new RichTextRange(7, 4);
        editor.Selection.SetLink("https://example.com", "tooltip");
        Assert.Equal("link", fixture.Handler.PlatformView.Document.Selection.Text);
        editor.SelectedRange = new RichTextRange(17, 0);
        fixture.Handler.PlatformView.Document.Selection.SetText(TextSetOptions.None, "!");
        Assert.Equal("before link after!", editor.Document.Text);
        Assert.Equal("tooltip", Assert.Single(editor.Document.CurrentSnapshot.Links).ToolTip);
        editor.SelectedRange = new RichTextRange(7, 4);
        Assert.Equal("link", fixture.Handler.PlatformView.Document.Selection.Text);
    });

    [Fact]
    public Task NativeProjectionMatchesMixedIncrementalTransactions() => WindowsTestHost.RunAsync(() =>
    {
        using var fixture = new EditorFixture();
        var editor = fixture.Editor;
        var random = new Random(8149);
        string[] insertions = ["a", "abc", "漢字", "\n", "\n\n", "\t", ""];
        for (var iteration = 0; iteration < 80; iteration++)
        {
            editor.Document.Edit(edit =>
            {
                for (var operation = 0; operation < 3; operation++)
                {
                    var start = random.Next(edit.Snapshot.Length + 1);
                    var length = random.Next(edit.Snapshot.Length - start + 1);
                    edit.ReplaceText(new RichTextRange(start, length), insertions[random.Next(insertions.Length)]);
                }
                if (edit.Snapshot.Length > 0)
                {
                    edit.UpdateCharacterFormat(new RichTextRange(0, edit.Snapshot.Length), format => format with { Italic = iteration % 2 == 0 });
                }
            });
            Assert.Equal(editor.Document.Text, fixture.NativeText);
        }
        while (editor.CanUndo)
        {
            editor.Undo();
            Assert.Equal(editor.Document.Text, fixture.NativeText);
        }
        while (editor.CanRedo)
        {
            editor.Redo();
            Assert.Equal(editor.Document.Text, fixture.NativeText);
        }
    });

    [Fact]
    public Task UndoRestoresFieldsAndMetadata() => WindowsTestHost.RunAsync(() =>
    {
        using var fixture = new EditorFixture();
        var editor = fixture.Editor;
        editor.Selection.InsertField("DATE", "today");
        editor.Document.Edit(edit => edit.SetMetadata("author", "test"));
        editor.SelectedRange = new RichTextRange(1, 3);
        editor.ClearUndoHistory();
        var before = editor.Document.CurrentSnapshot;
        editor.Selection.ReplaceText("X");
        var after = editor.Document.CurrentSnapshot;

        editor.SelectedRange = new RichTextRange(0, 1);
        editor.Undo();
        Assert.True(before.ContentEquals(editor.Document.CurrentSnapshot));
        Assert.Equal(new RichTextRange(0, 1), editor.SelectedRange);
        editor.Redo();
        Assert.True(after.ContentEquals(editor.Document.CurrentSnapshot));
        Assert.Equal(new RichTextRange(0, 1), editor.SelectedRange);
        Assert.Equal(editor.Document.Text, fixture.NativeText);
    });

    [Fact]
    public Task FieldOnlyEditsHaveUndoUnits() => WindowsTestHost.RunAsync(() =>
    {
        using var fixture = new EditorFixture();
        var editor = fixture.Editor;
        editor.Selection.InsertField("DATE", "today");
        var field = Assert.Single(editor.Document.CurrentSnapshot.Fields);
        editor.ClearUndoHistory();
        editor.Document.Edit(edit => edit.UpdateField(field.Id, "TIME", "today"));
        Assert.True(editor.CanUndo);
        editor.Undo();
        Assert.Equal("DATE", Assert.Single(editor.Document.CurrentSnapshot.Fields).Instruction);
        editor.Redo();
        Assert.Equal("TIME", Assert.Single(editor.Document.CurrentSnapshot.Fields).Instruction);
    });

    [Fact]
    public Task NativeTypingPreservesFieldsThroughUndoAndRedo() => WindowsTestHost.RunAsync(() =>
    {
        using var fixture = new EditorFixture();
        var editor = fixture.Editor;
        editor.Selection.InsertField("DATE", "today");
        editor.SelectedRange = new RichTextRange(0, 5);
        editor.ClearUndoHistory();
        var before = editor.Document.CurrentSnapshot;
        fixture.Handler.PlatformView.Document.Selection.SetText(TextSetOptions.None, "new");
        Assert.Equal("new", editor.Document.Text);
        editor.Undo();
        Assert.True(before.ContentEquals(editor.Document.CurrentSnapshot));
        Assert.Equal("today", fixture.NativeText);
        editor.Redo();
        Assert.Equal("new", fixture.NativeText);
    });

    [Fact]
    public Task ReadOnlyUndoAndRedoDoNotMutateContent() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new RichEditor();
        editor.Selection.ReplaceText("one");
        editor.IsReadOnly = true;
        Assert.False(editor.Commands.Undo.CanExecute(null));
        editor.Undo();
        Assert.Equal("one", editor.Document.Text);
        editor.IsReadOnly = false;
        editor.Undo();
        editor.IsReadOnly = true;
        Assert.False(editor.Commands.Redo.CanExecute(null));
        editor.Redo();
        Assert.Empty(editor.Document.Text);
    });

    [Fact]
    public Task AppearanceChangesDoNotBecomeAuthoredFormattingOrUndoUnits() => WindowsTestHost.RunAsync(() =>
    {
        using var fixture = new EditorFixture();
        var editor = fixture.Editor;
        editor.Selection.ReplaceText("text");
        editor.ClearUndoHistory();
        var snapshot = editor.Document.CurrentSnapshot;
        editor.TextColor = Colors.Red;
        editor.FontSize = 19;
        Assert.Same(snapshot, editor.Document.CurrentSnapshot);
        Assert.False(editor.CanUndo);
        Assert.False(fixture.Handler.PlatformView.Document.CanUndo());
    });

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public Task ProgrammaticEditsCanExceedMaxLength(int maxLength) => WindowsTestHost.RunAsync(() =>
    {
        using var fixture = new EditorFixture();
        fixture.Editor.MaxLength = maxLength;
        fixture.Editor.Selection.ReplaceText("longer text");
        Assert.Equal("longer text", fixture.NativeText);
        fixture.Editor.Undo();
        Assert.Empty(fixture.NativeText);
        fixture.Editor.Redo();
        Assert.Equal("longer text", fixture.NativeText);
    });

    [Fact]
    public Task ZeroMaxLengthRejectsNativeInsertion() => WindowsTestHost.RunAsync(() =>
    {
        using var fixture = new EditorFixture();
        fixture.Editor.MaxLength = 0;
        fixture.Handler.PlatformView.Document.Selection.SetText(TextSetOptions.None, "x");
        Assert.Empty(fixture.Editor.Document.Text);
        Assert.Empty(fixture.NativeText);
    });

    [Fact]
    public Task ClearingHighlightResetsTheNativeBackground() => WindowsTestHost.RunAsync(() =>
    {
        using var fixture = new EditorFixture();
        var editor = fixture.Editor;
        editor.Selection.ReplaceText("text");
        editor.SelectAll();
        var native = fixture.Handler.PlatformView.Document;
        var initial = native.GetRange(0, 4).CharacterFormat.BackgroundColor;
        editor.Selection.CharacterFormat.BackgroundColor = Colors.Yellow;
        Assert.NotEqual(initial, native.GetRange(0, 4).CharacterFormat.BackgroundColor);
        editor.Selection.CharacterFormat.BackgroundColor = null;
        Assert.Equal(initial, native.GetRange(0, 4).CharacterFormat.BackgroundColor);
    });

    [Fact]
    public Task RichPasteAndCutAreSingleUndoUnits() => WindowsTestHost.RunClipboardAsync(async () =>
    {
        using var fixture = new EditorFixture();
        var source = new RichTextDocument();
        source.Edit(edit =>
        {
            edit.InsertField(0, "DATE", "today");
            edit.UpdateCharacterFormat(new RichTextRange(0, 5), format => format with { FontWeight = 700 });
        });
        await RichTextClipboard.SetAsync(RichTextDocumentFragment.FromRange(source.CurrentSnapshot, new RichTextRange(0, 5)));
        var editor = fixture.Editor;
        editor.Selection.ReplaceText("prefix ");
        editor.ClearUndoHistory();
        await editor.PasteAsync();
        Assert.Equal("prefix today", fixture.NativeText);
        Assert.Single(editor.Document.CurrentSnapshot.Fields);
        editor.Undo();
        Assert.Equal("prefix ", fixture.NativeText);
        Assert.False(editor.CanUndo);
        editor.Redo();
        editor.SelectedRange = new RichTextRange(7, 5);
        var beforeCut = editor.Document.CurrentSnapshot;
        await editor.CutAsync();
        Assert.Equal("prefix ", fixture.NativeText);
        editor.Undo();
        Assert.True(beforeCut.ContentEquals(editor.Document.CurrentSnapshot));
        Assert.Equal(new RichTextRange(7, 0), editor.SelectedRange);
    });

    [Fact]
    public Task PasteCancellationAndPlainTextPreserveTypingFormat() => WindowsTestHost.RunClipboardAsync(async () =>
    {
        using var fixture = new EditorFixture();
        var editor = fixture.Editor;
        var package = new DataPackage();
        package.SetText("paste");
        Clipboard.SetContent(package);
        editor.Selection.ToggleBold();
        await editor.PasteAsync();
        Assert.True(editor.Document.CurrentSnapshot.Runs[0].Format.Bold);
        editor.Pasting += (_, args) => args.Cancel = true;
        var before = editor.Document.CurrentSnapshot;
        await editor.PasteAsync();
        Assert.Same(before, editor.Document.CurrentSnapshot);
    });

    [Fact]
    public Task RtfOnlyClipboardEnablesPaste() => WindowsTestHost.RunClipboardAsync(async () =>
    {
        using var fixture = new EditorFixture();
        var package = new DataPackage();
        package.SetRtf(@"{\rtf1\ansi\b rich}");
        Clipboard.SetContent(package);
        Assert.True(fixture.Editor.Commands.Paste.CanExecute(null));
        await fixture.Editor.PasteAsync();
        Assert.Equal("rich", fixture.NativeText);
        Assert.True(fixture.Editor.Document.CurrentSnapshot.Runs[0].Format.Bold);
    });

    [Fact]
    public Task NativeControlKeepsTextAndSelectionInSync() => WindowsTestHost.RunAsync(() =>
    {
        using var fixture = new EditorFixture();
        fixture.Editor.Selection.ReplaceText("hello\nworld");
        Assert.Equal("hello\nworld", fixture.Editor.Document.Text);
        Assert.Equal(new RichTextRange(11, 0), fixture.Editor.SelectedRange);
        Assert.Equal("hello\rworld\r", fixture.Handler.PlatformView.Document.GetRange(0, 12).Text);
    });

    private sealed class EditorFixture : IDisposable
    {
        public RichEditor Editor { get; } = new();
        public RichEditorHandler Handler { get; } = new();

        public string NativeText
        {
            get
            {
                var range = Handler.PlatformView.Document.GetRange(0, 0);
                range.SetRange(0, range.StoryLength);
                var text = range.Text;
                return (text.EndsWith('\r') ? text[..^1] : text).Replace('\r', '\n');
            }
        }

        public EditorFixture()
        {
            Handler.SetMauiContext(new MauiContext(WindowsTestHost.MauiApp.Services));
            Editor.Handler = Handler;
        }

        public void Dispose()
        {
            Editor.Handler = null;
            ((IElementHandler)Handler).DisconnectHandler();
        }
    }
}

internal static class WindowsTestHost
{
    private static readonly Lazy<Task<DispatcherQueue>> Dispatcher = new(Start);
    internal static MauiApp MauiApp { get; private set; } = null!;

    public static async Task RunAsync(Action action)
        => await RunAsync(() => { action(); return Task.CompletedTask; });

    public static async Task RunAsync(Func<Task> action)
    {
        var dispatcher = await Dispatcher.Value.WaitAsync(TimeSpan.FromSeconds(30));
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True(dispatcher.TryEnqueue(async () =>
        {
            try
            {
                await action();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        }));
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    public static Task RunClipboardAsync(Func<Task> action) => RunAsync(async () =>
    {
        var previous = Clipboard.GetContent();
        var backup = new DataPackage();
        foreach (var format in previous.AvailableFormats)
        {
            try
            {
                backup.SetData(format, await previous.GetDataAsync(format));
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // OLE adds private formats which DataPackageView cannot export.
            }
        }

        try
        {
            await action();
        }
        finally
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    Clipboard.SetContent(backup);
                    Clipboard.Flush();
                    break;
                }
                catch (System.Runtime.InteropServices.COMException) when (attempt < 10)
                {
                    // Other desktop applications can briefly hold the clipboard.
                    await Task.Delay(25);
                }
            }
        }
    });

    private static Task<DispatcherQueue> Start()
    {
        var completion = new TaskCompletionSource<DispatcherQueue>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                WinRT.ComWrappersSupport.InitializeComWrappers();
                XamlCheckProcessRequirements();
                Microsoft.UI.Xaml.Application.Start(args =>
                {
                    try
                    {
                        var dispatcher = DispatcherQueue.GetForCurrentThread();
                        SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(dispatcher));
                        _ = new NativeTestApplication();
                        dispatcher.TryEnqueue(() =>
                        {
                            try
                            {
                                MauiApp = MauiApp.CreateBuilder().Build();
                                completion.SetResult(dispatcher);
                            }
                            catch (Exception exception)
                            {
                                completion.SetException(exception);
                            }
                        });
                    }
                    catch (Exception exception)
                    {
                        completion.SetException(exception);
                    }
                });
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    [System.Runtime.InteropServices.DllImport("Microsoft.UI.Xaml.dll")]
    private static extern void XamlCheckProcessRequirements();

}
