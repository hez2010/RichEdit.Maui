#if DEBUG && (IOS || MACCATALYST)
using Foundation;
using ObjCRuntime;
using RichEdit.Maui.Platforms.Apple;
using UIKit;

namespace RichEdit.Maui.TestApp;

internal static class AppleEditorTests
{
    public static async Task RunAsync(RichEditor editor)
    {
        var native = (RichTextView)editor.Handler!.PlatformView!;
        var results = new List<string>();
        var output = Path.Combine(FileSystem.CacheDirectory, "apple-editor-tests.txt");
        var pasteboard = UIPasteboard.General;
        // Use only test-owned clipboard data; reading another app's clipboard at
        // startup prompts for paste permission before the test UI can load.
        pasteboard.Items = [];
        void Save() => File.WriteAllText(output,
            $"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; {UIDevice.CurrentDevice.SystemVersion}\n" +
            string.Join("\n", results) + "\n");
        async Task Test(string name, Func<Task> test)
        {
            var index = results.Count;
            results.Add($"RUN {name}");
            Save();
            try
            {
                native.ResignFirstResponder();
                editor.IsReadOnly = false;
                editor.MaxLength = -1;
                editor.AcceptsTab = false;
                editor.Document = new RichTextDocument();
                native.BecomeFirstResponder();
                await Drain();
                editor.ClearUndoHistory();
                await test();
                Equal(editor.Document.Text, native.Text ?? "");
                results[index] = $"PASS {name}";
            }
            catch (Exception exception)
            {
                results[index] = $"FAIL {name}: {exception}";
            }
            Save();
            Console.WriteLine(results[index]);
        }

        await Test("programmatic edits restore text and selection", async () =>
        {
            editor.Selection.ReplaceText("one\nsecond");
            Equal(new RichTextRange(10, 0), editor.SelectedRange);
            Equal("one\nsecond", native.Text);
            await Drain();
            editor.Undo();
            await Drain();
            Equal("", editor.Document.Text);
            Equal(RichTextRange.Empty, editor.SelectedRange);
            editor.Redo();
            await Drain();
            Equal("one\nsecond", editor.Document.Text);
            Equal(new RichTextRange(10, 0), editor.SelectedRange);
        });

        await Test("field-only changes are undoable", async () =>
        {
            editor.Selection.InsertField("DATE", "today");
            await Drain();
            editor.ClearUndoHistory();
            var id = editor.Document.CurrentSnapshot.Fields.Single().Id;
            editor.Document.Edit(edit => edit.UpdateField(id, "TIME", "today"));
            await Drain();
            Equal(true, editor.CanUndo);
            editor.Undo();
            await Drain();
            Equal("DATE", editor.Document.CurrentSnapshot.Fields.Single().Instruction);
            editor.Redo();
            await Drain();
            Equal("TIME", editor.Document.CurrentSnapshot.Fields.Single().Instruction);
        });

        await Test("system undo selectors preserve distinct programmatic transactions", async () =>
        {
            editor.Selection.ReplaceText("one");
            editor.Selection.ReplaceText("two");
            var undo = new Selector("undo");
            var redo = new Selector("redo");
            native.UndoManager!.PerformSelector(undo, null, 0);
            await Drain();
            Equal("one", editor.Document.Text);
            native.UndoManager.PerformSelector(undo, null, 0);
            await Drain();
            Equal("", editor.Document.Text);
            native.UndoManager.PerformSelector(redo, null, 0);
            await Drain();
            Equal("one", editor.Document.Text);
        });

        await Test("unfocused edits keep their undo history", async () =>
        {
            native.ResignFirstResponder();
            editor.Selection.ReplaceText("unfocused");
            await Drain();
            Equal(true, editor.CanUndo);
            editor.Undo();
            await Drain();
            Equal("", editor.Document.Text);
            editor.Redo();
            await Drain();
            Equal("unfocused", editor.Document.Text);
        });

        await Test("native replacement undo restores field semantics", async () =>
        {
            editor.Selection.InsertField("DATE", "today");
            await Drain();
            editor.ClearUndoHistory();
            editor.SelectAll();
            Equal(new NSRange(0, 5), native.SelectedRange);
            var before = editor.Document.RtfText;
            native.InsertText("new");
            await Drain();
            Equal("new", editor.Document.Text);
            native.UndoManager!.Undo();
            await Drain();
            Equal(before, editor.Document.RtfText);
            native.UndoManager.Redo();
            await Drain();
            Equal("new", editor.Document.Text);
        });

        await Test("native typing groups replay without model divergence", async () =>
        {
            native.InsertText("a");
            await Drain();
            native.InsertText("b");
            await Drain();
            native.InsertText("c");
            await Drain();
            Equal("abc", editor.Document.Text);
            var steps = 0;
            while (editor.CanUndo && steps++ < 10)
            {
                native.UndoManager!.Undo();
                await Drain();
                Equal(native.Text, editor.Document.Text);
            }
            Equal("", editor.Document.Text);
            steps = 0;
            while (editor.CanRedo && steps++ < 10)
            {
                native.UndoManager!.Redo();
                await Drain();
                Equal(native.Text, editor.Document.Text);
            }
            Equal("abc", editor.Document.Text);
        });

        await Test("native copy and paste preserve rich model values", async () =>
        {
            editor.Selection.InsertField("DATE", "today");
            editor.SelectAll();
            editor.Selection.UpdateCharacterFormat(format => format with
            {
                FontWeight = 700, StyleName = "Owned style", Ligatures = RichTextFeatureMode.Disabled,
            });
            native.Copy(native);
            await Drain();
            Equal("today", pasteboard.String);
            using var rtf = pasteboard.DataForPasteboardType("public.rtf");
            Equal(true, rtf is not null);
            editor.Document = new RichTextDocument();
            native.Paste(native);
            await Drain();
            Equal("today", editor.Document.Text);
            Equal("DATE", editor.Document.CurrentSnapshot.Fields.Single().Instruction);
            Equal("Owned style", editor.Document.CurrentSnapshot.Runs[0].Format.StyleName);
            Equal(true, editor.Document.CurrentSnapshot.Runs[0].Format.Bold);
        });

        await Test("native cut is one complete undo unit", async () =>
        {
            editor.Selection.InsertField("DATE", "today");
            editor.SelectAll();
            await Drain();
            editor.ClearUndoHistory();
            var before = editor.Document.RtfText;
            native.Cut(native);
            await Drain();
            Equal("", editor.Document.Text);
            editor.Undo();
            await Drain();
            Equal(before, editor.Document.RtfText);
            Equal(new RichTextRange(0, 5), editor.SelectedRange);
            Equal(false, editor.CanUndo);
            editor.Redo();
            await Drain();
            Equal("", editor.Document.Text);
        });

        await Test("plain paste uses typing format and can be cancelled", async () =>
        {
            pasteboard.String = "plain";
            editor.Selection.ToggleItalic();
            native.Paste(native);
            await Drain();
            Equal(true, editor.Document.CurrentSnapshot.Runs[0].Format.Italic);
            EventHandler<RichTextPastingEventArgs> cancel = (_, args) => args.Cancel = true;
            editor.Pasting += cancel;
            try
            {
                native.Paste(native);
                await Drain();
                Equal("plain", editor.Document.Text);
            }
            finally { editor.Pasting -= cancel; }
        });

        await Test("legacy RTF-only clipboard is accepted", async () =>
        {
            using var data = NSData.FromArray(System.Text.Encoding.Latin1.GetBytes(@"{\rtf1\ansi\b caf" + "é}"));
            using var item = NSDictionary.FromObjectAndKey(data, new NSString("public.rtf"));
            pasteboard.Items = [item];
            Equal(true, editor.Commands.Paste.CanExecute(null));
            await editor.PasteAsync();
            Equal("café", editor.Document.Text);
            Equal(true, editor.Document.CurrentSnapshot.Runs[0].Format.Bold);
        });

        await Test("native bold and underline removal override metadata", async () =>
        {
            editor.Selection.ReplaceText("styled");
            editor.SelectAll();
            editor.Selection.ToggleBold();
            editor.Selection.ToggleUnderline(RichTextUnderlineStyle.DashDot);
            await Drain();
            var bold = new Selector("toggleBoldface:");
            native.PerformSelector(bold, null, 0);
            await Drain();
            Equal(false, editor.Document.CurrentSnapshot.Runs[0].Format.Bold);
            var underline = new Selector("toggleUnderline:");
            native.PerformSelector(underline, null, 0);
            await Drain();
            Equal(RichTextUnderlineStyle.None, editor.Document.CurrentSnapshot.Runs[0].Format.Underline);
        });

        await Test("native paragraph edits preserve inheritance and precise values", async () =>
        {
            editor.Document.Edit(edit =>
            {
                edit.SetDefaultCharacterFormat(RichTextCharacterFormat.Default with { FontSize = 16.5 });
                edit.InsertText(0, "first\nsecond");
                edit.SetParagraphFormat(new RichTextRange(0, 12), RichTextParagraphFormat.Default with
                {
                    Alignment = RichTextAlignment.Distributed, LeadingIndent = 18.5,
                    LineSpacingRule = RichTextLineSpacingRule.Exactly, LineSpacing = 20,
                    TabStops = [new RichTextTabStop(24.5, RichTextTabAlignment.Right, RichTextTabLeader.Dots)],
                });
            });
            var before = editor.Document.CurrentSnapshot;
            editor.SelectedRange = new RichTextRange(editor.Document.Length, 0);
            native.InsertText("\nnew");
            await Drain();
            Equal(before.Runs[0].Format, editor.Document.CurrentSnapshot.Runs[0].Format);
            Equal(before.Paragraphs[0].Format, editor.Document.CurrentSnapshot.Paragraphs[0].Format);
        });

        await Test("native image deletion and undo restore original payload", async () =>
        {
            var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
            editor.Selection.InsertImage(RichTextImage.FromBytes(0, "image/png", bytes, 12, 12));
            await Drain();
            editor.ClearUndoHistory();
            native.DeleteBackward();
            await Drain();
            Equal("", editor.Document.Text);
            native.UndoManager!.Undo();
            await Drain();
            Equal("\uFFFC", editor.Document.Text);
            Equal(true, editor.Document.CurrentSnapshot.Images.Single().Data.AsSpan().SequenceEqual(bytes));
        });

        await Test("native list continuation and undo preserve list identity", async () =>
        {
            editor.Selection.ReplaceText("one");
            editor.Selection.SetList(new RichTextListDefinition([
                new RichTextListLevelDefinition
                {
                    Marker = new RichTextListMarker.Number(RichTextListNumberStyle.Arabic, 1),
                    Prefix = "", Suffix = ".", LeadingIndent = 24, FirstLineIndent = -12, MarkerTab = 24,
                }]));
            await Drain();
            editor.ClearUndoHistory();
            var before = editor.Document.RtfText;
            native.InsertText("\ntwo");
            await Drain();
            Equal("one\ntwo", editor.Document.Text);
            Equal(2, editor.Document.CurrentSnapshot.Paragraphs.Count(paragraph => paragraph.Format.List is not null));
            editor.Undo();
            await Drain();
            Equal(before, editor.Document.RtfText);
        });

        await Test("read-only undo and input configuration", async () =>
        {
            editor.Selection.ReplaceText("fixed");
            await Drain();
            editor.IsReadOnly = true;
            editor.Keyboard = Keyboard.Email;
            Equal(false, native.Editable);
            Equal(false, editor.Commands.Undo.CanExecute(null));
            editor.Undo();
            Equal("fixed", editor.Document.Text);
        });

        await Test("length limits reject typing but allow programmatic content and deletion", async () =>
        {
            editor.MaxLength = 0;
            native.InsertText("x");
            await Drain();
            Equal("", editor.Document.Text);
            editor.Selection.ReplaceText("long");
            Equal("long", native.Text);
            native.DeleteBackward();
            await Drain();
            Equal("lon", editor.Document.Text);
        });

        await Test("mixed native and programmatic history stays synchronized", async () =>
        {
            native.InsertText("a");
            await Drain();
            editor.Selection.InsertField("DATE", "field");
            await Drain();
            native.InsertText("z");
            await Drain();
            var after = editor.Document.RtfText;
            var steps = 0;
            while (editor.CanUndo && steps++ < 10)
            {
                editor.Undo();
                await Drain();
                Equal(native.Text, editor.Document.Text);
            }
            Equal("", editor.Document.Text);
            steps = 0;
            while (editor.CanRedo && steps++ < 10)
            {
                editor.Redo();
                await Drain();
                Equal(native.Text, editor.Document.Text);
            }
            Equal(after, editor.Document.RtfText);
        });

        pasteboard.Items = [];
        results.Add($"COMPLETE {results.Count} tests, {results.Count(result => result.StartsWith("FAIL"))} failures");
        Save();
        Console.WriteLine(results[^1]);
    }

    private static Task Drain() => Task.Delay(100);

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected {expected}; actual {actual}.");
        }
    }
}
#endif
