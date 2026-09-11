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
        // Exercise native transformations explicitly, regardless of simulator defaults.
        native.SmartInsertDeleteType = UITextSmartInsertDeleteType.Yes;
        var results = new List<string>();
        var started = DateTimeOffset.UtcNow;
        var output = Path.Combine(FileSystem.CacheDirectory, "apple-editor-tests.txt");
        var pasteboard = UIPasteboard.General;
        // Use only test-owned clipboard data; reading another app's clipboard at
        // startup prompts for paste permission before the test UI can load.
        pasteboard.Items = [];
        void Save() => File.WriteAllText(output,
            $"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; {UIDevice.CurrentDevice.SystemVersion}\n" +
            $"Started {started:O}; library {typeof(RichEditor).Assembly.ManifestModule.ModuleVersionId}\n" +
            string.Join("\n", results) + "\n");
        async Task Test(string name, Func<Task> test)
        {
            if (Environment.GetEnvironmentVariable("RICHEDIT_TEST_FILTER") is { Length: > 0 } filter && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) return;
            var index = results.Count;
            results.Add($"RUN {name}");
            Save();
            var trace = new Queue<string>();
            void Record(string action)
            {
                trace.Enqueue($"{action}: version={editor.Document.Version}, selection={editor.SelectedRange}, native={native.SelectedRange}, readonly={editor.IsReadOnly}, bold={editor.Document.CurrentSnapshot.Runs.FirstOrDefault()?.Format.Bold}");
                if (trace.Count > 16) trace.Dequeue();
            }
            void SelectionChanged(object? sender, RichTextSelectionChangedEventArgs args) => Record("selection");
            void ContentChanged(object? sender, RichTextContentChangedEventArgs args) => Record("content " + args.ChangeSet.Origin);
            try
            {
                native.ResignFirstResponder();
                EditorContractTests.Reset(editor);
                native.BecomeFirstResponder();
                await Drain();
                editor.ClearUndoHistory();
                editor.SelectionChanged += SelectionChanged;
                editor.ContentChanged += ContentChanged;
                await test();
                Equal(editor.Document.Text, native.Text ?? "");
                results[index] = $"PASS {name}";
            }
            catch (Exception exception)
            {
                results[index] = $"FAIL {name}: {exception}\n" + string.Join("\n", trace);
            }
            finally
            {
                editor.SelectionChanged -= SelectionChanged;
                editor.ContentChanged -= ContentChanged;
            }
            Save();
            Console.WriteLine(results[index]);
        }

        await Test("native smart selection transformations remain tracked", async () =>
        {
            editor.Selection.ReplaceText("one\nsecond");
            editor.ClearUndoHistory();
            var before = editor.Document.CurrentSnapshot;
            editor.SelectedRange = new RichTextRange(5, 0);
            editor.SelectAll();
            await EditorContractTests.Verify(editor);
            if (native.Text != before.Text)
            {
                Equal(true, editor.CanUndo);
                var transformed = editor.Document.CurrentSnapshot;
                editor.Undo();
                await EditorContractTests.Verify(editor);
                EditorContractTests.SameContent(before, editor.Document.CurrentSnapshot);
                editor.Redo();
                await EditorContractTests.Verify(editor);
                EditorContractTests.SameContent(transformed, editor.Document.CurrentSnapshot);
            }
            else
            {
                EditorContractTests.SameContent(before, editor.Document.CurrentSnapshot);
                Equal(false, editor.CanUndo);
            }
        });

        await Test("programmatic history restores text and keeps the caret valid", async () =>
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

        await Test("native formatting without metadata distinguishes defaults from edits", async () =>
        {
            editor.Document.Edit(edit => edit.SetDefaultCharacterFormat(RichTextCharacterFormat.Default with
            {
                FontSize = 16.5,
                ForegroundColor = Microsoft.Maui.Graphics.Colors.Blue,
            }));
            editor.Selection.ReplaceText("styled");
            await Drain();
            editor.ClearUndoHistory();
            var before = editor.Document.CurrentSnapshot;
            var original = new UIStringAttributes(native.TextStorage.GetAttributes(0, out _));
            var range = new NSRange(0, editor.Document.Length);
            var attributes = new UIStringAttributes
            {
                Font = original.Font,
                ForegroundColor = original.ForegroundColor,
                ParagraphStyle = original.ParagraphStyle,
            };
            native.TextStorage.SetAttributes(attributes.Dictionary, range);
            await Drain();
            EditorContractTests.SameContent(before, editor.Document.CurrentSnapshot);
            Equal(false, editor.CanUndo);

            attributes.Font = UIFont.BoldSystemFontOfSize(25);
            attributes.ForegroundColor = UIColor.Red;
            attributes.ParagraphStyle = new NSMutableParagraphStyle
            {
                Alignment = UITextAlignment.Center,
                LineSpacing = 5,
                TabStops = [new NSTextTab(UITextAlignment.Right, 96, new NSDictionary())],
            };
            native.TextStorage.SetAttributes(attributes.Dictionary, range);
            await Drain();
            var after = editor.Document.CurrentSnapshot;
            Equal(true, after.Runs.Single().Format.Bold);
            Equal(25d, after.Runs.Single().Format.FontSize);
            Equal(Microsoft.Maui.Graphics.Colors.Red, after.Runs.Single().Format.ForegroundColor);
            Equal(RichTextAlignment.Center, after.Paragraphs.Single().Format.Alignment);
            Equal(5d, after.Paragraphs.Single().Format.LineSpacing);
            Equal(new RichTextTabStop(96, RichTextTabAlignment.Right), after.Paragraphs.Single().Format.TabStops.Single());
            Equal(true, editor.CanUndo);
            editor.Undo();
            await EditorContractTests.Verify(editor);
            EditorContractTests.SameContent(before, editor.Document.CurrentSnapshot);
            Equal(false, editor.CanUndo);
            editor.Redo();
            await EditorContractTests.Verify(editor);
            EditorContractTests.SameContent(after, editor.Document.CurrentSnapshot);
        });

        await Test("formatting preserves the scrolled viewport", async () =>
        {
            editor.Selection.ReplaceText(string.Concat(Enumerable.Repeat("Paragraph text for scrolling.\n", 150)));
            editor.SelectedRange = new RichTextRange(31, 9);
            await Drain();
            native.SetContentOffset(new CoreGraphics.CGPoint(0, 200), false);
            await Drain();
            var before = native.ContentOffset;
            editor.Selection.ToggleBold();
            await Drain();
            EditorContractTests.Equal(true, Math.Abs((double)(native.ContentOffset.Y - before.Y)) < 2,
                $"character formatting scrolled from {before.Y} to {native.ContentOffset.Y}");
            editor.Selection.ParagraphFormat.Alignment = RichTextAlignment.Center;
            await Drain();
            EditorContractTests.Equal(true, Math.Abs((double)(native.ContentOffset.Y - before.Y)) < 2,
                $"paragraph formatting scrolled from {before.Y} to {native.ContentOffset.Y}");
            editor.Selection.CharacterFormat.FontSize = 24;
            await Drain();
            EditorContractTests.Equal(true, Math.Abs((double)(native.ContentOffset.Y - before.Y)) < 2,
                $"font size formatting scrolled from {before.Y} to {native.ContentOffset.Y}");
            editor.Undo();
            await Drain();
            EditorContractTests.Equal(true, Math.Abs((double)(native.ContentOffset.Y - before.Y)) < 2,
                $"formatting undo scrolled from {before.Y} to {native.ContentOffset.Y}");
            editor.Redo();
            await Drain();
            EditorContractTests.Equal(true, Math.Abs((double)(native.ContentOffset.Y - before.Y)) < 2,
                $"formatting redo scrolled from {before.Y} to {native.ContentOffset.Y}");

            editor.SelectAll();
            await Drain();
            native.SetContentOffset(new CoreGraphics.CGPoint(0, native.ContentSize.Height - native.Bounds.Height), false);
            editor.Selection.CharacterFormat.FontSize = 2;
            await Drain();
            var maximum = Math.Max(-native.AdjustedContentInset.Top,
                native.ContentSize.Height - native.Bounds.Height + native.AdjustedContentInset.Bottom);
            EditorContractTests.Equal(true, native.ContentOffset.Y <= maximum + 2,
                $"shrinking formatting left offset {native.ContentOffset.Y} past the bottom {maximum}");
        });

        await Test("text undo and redo preserve the scrolled viewport", async () =>
        {
            var source = string.Concat(Enumerable.Repeat("Paragraph text for scrolling.\n", 150));
            editor.Document = RichTextDocument.FromPlainText(source);
            editor.SelectedRange = new RichTextRange(31, 0);
            native.InsertText("edit");
            await Task.Delay(100);
            native.SetContentOffset(new CoreGraphics.CGPoint(0, 200), false);
            await Task.Delay(100);
            var before = native.ContentOffset;
            async Task VerifyViewport(string operation)
            {
                // Sample several frames so a delayed UIKit scroll is observable.
                for (var frame = 0; frame < 15; frame++)
                {
                    await Task.Delay(20);
                    EditorContractTests.Equal(true, Math.Abs((double)(native.ContentOffset.Y - before.Y)) < 2,
                        $"text {operation} scrolled from {before.Y} to {native.ContentOffset.Y}");
                }
            }
            editor.Undo();
            await VerifyViewport("undo");
            Equal(source, editor.Document.Text);
            Equal(new RichTextRange(31, 0), editor.SelectedRange);
            editor.Redo();
            await VerifyViewport("redo");
            Equal(source.Insert(31, "edit"), editor.Document.Text);
            Equal(new RichTextRange(35, 0), editor.SelectedRange);

            editor.Document = RichTextDocument.FromPlainText("short");
            editor.Selection.ReplaceText(source);
            await Task.Delay(100);
            native.SetContentOffset(new CoreGraphics.CGPoint(0, native.ContentSize.Height - native.Bounds.Height), false);
            editor.Undo();
            await Task.Delay(250);
            var maximum = Math.Max(-native.AdjustedContentInset.Top,
                native.ContentSize.Height - native.Bounds.Height + native.AdjustedContentInset.Bottom);
            EditorContractTests.Equal(true, native.ContentOffset.Y <= maximum + 2,
                $"text undo left offset {native.ContentOffset.Y} past the bottom {maximum}");
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

        await Test("list markers render without becoming editable text", async () =>
        {
            editor.Selection.ReplaceText("one\ntwo");
            editor.SelectAll();
            editor.Selection.SetList(new RichTextListDefinition([
                new RichTextListLevelDefinition
                {
                    Marker = new RichTextListMarker.Bullet("•"),
                    Prefix = "", Suffix = "", LeadingIndent = 36, FirstLineIndent = -30, MarkerTab = 36,
                }]));
            await Drain();
            editor.SelectedRange = new RichTextRange(editor.Document.Length, 0);
            var before = editor.Document.CurrentSnapshot;
            var selection = editor.SelectedRange;
            VerifyListMarkerPixels(native, 0);
            VerifyListMarkerPixels(native, 4);
            Equal("one\ntwo", native.Text);
            Equal(selection, editor.SelectedRange);
            EditorContractTests.SameContent(before, editor.Document.CurrentSnapshot);

            editor.Selection.ClearList();
            await Drain();
            editor.Undo();
            await Drain();
            VerifyListMarkerPixels(native, 0);
            EditorContractTests.SameContent(before, editor.Document.CurrentSnapshot);
        });

        await Test("list switching and Return before existing text preserve marker indentation", async () =>
        {
            editor.Document.Edit(edit =>
            {
                edit.SetDefaultCharacterFormat(RichTextCharacterFormat.Default with { FontFamily = "Georgia", FontSize = 19 });
                edit.SetDefaultParagraphFormat(RichTextParagraphFormat.Default with { SpaceAfter = 16 });
            });
            editor.Selection.ReplaceText("Test\n\nTail");
            editor.SelectedRange = new RichTextRange(0, 0);
            editor.Selection.SetList(new RichTextListDefinition([
                new RichTextListLevelDefinition
                {
                    Marker = new RichTextListMarker.Bullet("•"),
                    Prefix = "", Suffix = "", LeadingIndent = 18, FirstLineIndent = -18, MarkerTab = 18,
                }]));
            editor.SelectedRange = new RichTextRange(5, 0);
            await Drain();
            // UIKit can inherit the native list style while discarding custom
            // attributes on a new item, as in the captured keyboard reproduction.
            using var inherited = new NSMutableDictionary(native.TextStorage.GetAttributes(0, out _)!);
            using var metadataKey = new NSString("RichEdit.Maui.ParagraphFormat");
            inherited.Remove(metadataKey);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            native.TextStorage.SetAttributes(inherited, new NSRange(5, 1));
            await Drain();
            CheckItems(18);
            native.InsertText("Disadvantage");
            await Drain();
            CheckItems(18);
            editor.Selection.SetList(new RichTextListDefinition([
                new RichTextListLevelDefinition
                {
                    Marker = new RichTextListMarker.Number(RichTextListNumberStyle.Arabic, 1),
                    Prefix = "", Suffix = ".", LeadingIndent = 36, FirstLineIndent = -36, MarkerTab = 36,
                }]));
            await Drain();
            CheckItems(36);
            foreach (var input in new[] { "\n", "\n", "D", "s", "v", "\n", "\n" })
            {
                native.InsertText(input);
                await Drain();
                CheckItems(36);
            }
            var itemStart = editor.Document.Text.IndexOf("Dsv", StringComparison.Ordinal);
            editor.SelectedRange = new RichTextRange(itemStart, 1);
            native.InsertText("T");
            await Drain();
            CheckItems(36);
            native.DeleteBackward();
            await Drain();
            CheckItems(36);
            editor.Undo();
            await Drain();
            CheckItems(36);
            var emptyItem = editor.Document.Text.IndexOf("\n\n", StringComparison.Ordinal) + 1;
            editor.SelectedRange = new RichTextRange(emptyItem, 0);
            await Drain();
            var emptyMarker = VerifyListMarkerPixels(native, emptyItem);
            native.InsertText("x");
            await Drain();
            var filledMarker = VerifyListMarkerPixels(native, emptyItem);
            Equal(true, emptyMarker.AsSpan().SequenceEqual(filledMarker));
            native.DeleteBackward();
            await Drain();
            native.SetContentOffset(CoreGraphics.CGPoint.Empty, false);
            native.LayoutIfNeeded();
            using var end = native.GetPosition(native.BeginningOfDocument, editor.Document.Length)!;
            var height = Math.Min(native.Bounds.Height, native.GetCaretRectForPosition(end).Bottom + native.TextContainerInset.Bottom);
            using var renderer = new UIGraphicsImageRenderer(new CoreGraphics.CGSize(native.Bounds.Width, height));
            using var image = renderer.CreateImage(context => native.Layer.RenderInContext(context.CGContext));
            using var png = image.AsPNG()!;
            png.Save(Path.Combine(FileSystem.CacheDirectory, "apple-list-switching.png"), false, out _);

            void CheckItems(double indent)
            {
                var snapshot = editor.Document.CurrentSnapshot;
                foreach (var paragraph in snapshot.Paragraphs.Where(paragraph => paragraph.Range.Start > 0 && paragraph.Format.List is not null))
                {
                    var item = paragraph.Format.List!;
                    Equal(indent == 18, snapshot.Lists[item.ListId].Levels[item.Level].Marker is RichTextListMarker.Bullet);
                    if (paragraph.Format.LeadingIndent != indent || paragraph.Format.FirstLineIndent != -indent)
                        throw new InvalidOperationException($"At {paragraph.Range.Start}: expected {indent}/{-indent}; got {paragraph.Format}. Text={snapshot.Text.Replace("\n", "\\n")}");
                }
            }
        });

        foreach (var firstText in new[] { "bullet", "" })
        {
            await Test($"Backspace through mixed lists preserves the empty numbered item ({firstText.Length} preceding characters)", async () =>
            {
                var bullets = new RichTextListDefinition([
                    new RichTextListLevelDefinition
                    {
                        Marker = new RichTextListMarker.Bullet("•"),
                        Prefix = "", Suffix = "", LeadingIndent = 18, FirstLineIndent = -18, MarkerTab = 18,
                    }]);
                editor.Selection.SetList(bullets);
                if (firstText.Length > 0) native.InsertText(firstText);
                await Drain();
                native.InsertText("\n");
                await Drain();
                editor.Selection.SetList(new RichTextListDefinition([
                    new RichTextListLevelDefinition
                    {
                        Marker = new RichTextListMarker.Number(RichTextListNumberStyle.Arabic, 1),
                        Prefix = "", Suffix = ".", LeadingIndent = 36, FirstLineIndent = -36, MarkerTab = 36,
                    }]));
                var numberedId = editor.Document.CurrentSnapshot.Paragraphs[^1].Format.List!.ListId;
                foreach (var text in new[] { "number", "\n", "\n" })
                {
                    native.InsertText(text);
                    await Drain();
                }
                editor.Selection.SetList(bullets);
                await Drain();
                Equal(firstText + "\nnumber\n\n", editor.Document.Text);
                editor.ClearUndoHistory();
                for (var i = 0; i < "number\n\n".Length; i++)
                {
                    native.DeleteBackward();
                    await Drain();
                    var last = editor.Document.CurrentSnapshot.Paragraphs[^1].Format;
                    Equal(numberedId, last.List!.ListId);
                    Equal(36d, last.LeadingIndent);
                    Equal(-36d, last.FirstLineIndent);
                }
                Equal(firstText + "\n", editor.Document.Text);
                VerifyListMarkerPixels(native, firstText.Length + 1);
                native.InsertText("again");
                await Drain();
                Equal(numberedId, editor.Document.CurrentSnapshot.Paragraphs[^1].Format.List!.ListId);
                editor.Undo();
                await Drain();
                Equal(firstText + "\n", editor.Document.Text);
                Equal(numberedId, editor.Document.CurrentSnapshot.Paragraphs[^1].Format.List!.ListId);
            });
        }

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

        await Test("list markers render on empty items and after nesting and scrolling", async () =>
        {
            editor.Selection.SetList(new RichTextListDefinition([
                new RichTextListLevelDefinition
                {
                    Marker = new RichTextListMarker.Bullet("•"),
                    Prefix = "", Suffix = "", LeadingIndent = 36, FirstLineIndent = -30, MarkerTab = 36,
                },
                new RichTextListLevelDefinition
                {
                    Marker = new RichTextListMarker.Bullet("▪"),
                    Prefix = "", Suffix = "", LeadingIndent = 54, FirstLineIndent = -30, MarkerTab = 54,
                }]));
            await Drain();
            var emptyMarker = VerifyListMarkerPixels(native, 0);
            Equal("", native.Text);
            native.InsertText("one");
            await Drain();
            Equal(true, emptyMarker.AsSpan().SequenceEqual(VerifyListMarkerPixels(native, 0)));
            native.InsertText("\n");
            await Drain();
            VerifyListMarkerPixels(native, 4);
            editor.Selection.ChangeListLevel(1);
            await Drain();
            VerifyListMarkerPixels(native, 4, 54);
            native.InsertText(string.Join("\n", Enumerable.Repeat("item", 60)));
            await Drain();
            native.ScrollRangeToVisible(new NSRange(editor.Document.Length, 0));
            await Drain();
            VerifyListMarkerPixels(native, editor.Document.Text.LastIndexOf('\n') + 1, 54);
        });

        await Test("list number markers repaint after restart", async () =>
        {
            editor.Selection.ReplaceText("one\ntwo");
            editor.SelectAll();
            editor.Selection.SetList(new RichTextListDefinition([
                new RichTextListLevelDefinition
                {
                    Marker = new RichTextListMarker.Number(RichTextListNumberStyle.Arabic, 1),
                    Prefix = "", Suffix = ".", LeadingIndent = 36, FirstLineIndent = -30, MarkerTab = 36,
                }]));
            editor.SelectedRange = new RichTextRange(4, 0);
            await Drain();
            var before = VerifyListMarkerPixels(native, 4);
            editor.Selection.RestartList(7);
            await Drain();
            var after = VerifyListMarkerPixels(native, 4);
            Equal(false, before.AsSpan().SequenceEqual(after));
            Equal("one\ntwo", native.Text);
        });

        await Test("native list formatting without metadata retains indentation", async () =>
        {
            if (!OperatingSystem.IsIOSVersionAtLeast(16) && !OperatingSystem.IsMacCatalystVersionAtLeast(16)) return;
            editor.Selection.ReplaceText("one");
            await Drain();
            using var list = new NSTextList("{disc}", NSTextListOptions.None, 1);
            using var style = new NSMutableParagraphStyle { HeadIndent = 36, FirstLineHeadIndent = 0, TextLists = [list] };
            using var metadataKey = new NSString("RichEdit.Maui.ParagraphFormat");
            var range = new NSRange(0, 3);
            native.TextStorage.BeginEditing();
            native.TextStorage.RemoveAttribute(metadataKey, range);
            native.TextStorage.AddAttribute(UIStringAttributeKey.ParagraphStyle, style, range);
            native.TextStorage.EndEditing();
            await Drain();
            var paragraph = editor.Document.CurrentSnapshot.Paragraphs[0].Format;
            Equal(true, paragraph.List is not null);
            Equal(36d, paragraph.LeadingIndent);
            Equal(-36d, paragraph.FirstLineIndent);
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

        await Test("native follow-up spacing is observed as part of the original undo unit", async () =>
        {
            native.SmartInsertDeleteType = UITextSmartInsertDeleteType.Yes;
            editor.Selection.ReplaceText("one");
            Equal(UITextSmartInsertDeleteType.Yes, native.SmartInsertDeleteType);
            editor.ClearUndoHistory();
            native.InsertText("two");
            // Simulate the follow-up storage edit a text service performs after
            // the initial UITextViewDelegate.Changed callback has committed.
            native.TextStorage.Replace(new NSRange(3, 0), " ");
            native.SelectedRange = new NSRange(native.TextStorage.Length, 0);
            await Drain();
            Equal("one two", native.Text);
            Equal(native.Text, editor.Document.Text);
            editor.Undo();
            await Drain();
            Equal("one", native.Text);
            Equal("one", editor.Document.Text);
            Equal(false, editor.CanUndo);
            editor.Redo();
            await Drain();
            Equal("one two", native.Text);
            Equal(native.Text, editor.Document.Text);
            Equal(UITextSmartInsertDeleteType.Yes, native.SmartInsertDeleteType);
        });

        await Test("native storage formatting changes are observed and undoable", async () =>
        {
            editor.Selection.ReplaceText("styled");
            editor.SelectAll();
            editor.ClearUndoHistory();
            native.TextStorage.AddAttribute(UIStringAttributeKey.UnderlineStyle, NSNumber.FromInt32((int)NSUnderlineStyle.Single), new NSRange(0, 6));
            await Drain();
            Equal(RichTextUnderlineStyle.Single, editor.Document.CurrentSnapshot.Runs[0].Format.Underline);
            editor.Undo();
            await Drain();
            Equal(RichTextUnderlineStyle.None, editor.Document.CurrentSnapshot.Runs[0].Format.Underline);
        });

        await Test("IME marked text conversion, commit and history", async () =>
        {
            editor.Selection.ToggleBold();
            native.SetMarkedText("n", new NSRange(1, 0));
            native.SetMarkedText("ni", new NSRange(2, 0));
            native.SetMarkedText("日本", new NSRange(2, 0));
            native.UnmarkText();
            await Drain();
            Equal("日本", editor.Document.Text);
            Equal(true, editor.Document.CurrentSnapshot.Runs[0].Format.Bold);
            native.InsertText("😀");
            await Drain();
            native.DeleteBackward();
            await Drain();
            Equal("日本", editor.Document.Text);
            var final = editor.Document.CurrentSnapshot;
            var count = 0;
            while (editor.CanUndo && count++ < 20) { editor.Undo(); await Drain(); }
            Equal("", editor.Document.Text);
            while (editor.CanRedo && count-- > -20) { editor.Redo(); await Drain(); }
            EditorContractTests.SameContent(final, editor.Document.CurrentSnapshot);
        });

        await Test("metadata edits preserve ongoing IME composition", async () =>
        {
            native.SetMarkedText("n", new NSRange(1, 0));
            editor.Document.Edit(edit => edit.SetMetadata("author", "test"));
            Equal(true, native.MarkedTextRange is not null);
            native.SetMarkedText("日本", new NSRange(2, 0));
            native.UnmarkText();
            await Drain();
            Equal("日本", editor.Document.Text);
            Equal("test", editor.Document.CurrentSnapshot.Metadata["author"]);
        });

        await Test("marked text respects replacement length budget", async () =>
        {
            editor.Selection.ReplaceText("abc");
            editor.SelectedRange = new RichTextRange(1, 1);
            editor.MaxLength = 4;
            native.SetMarkedText("日本", new NSRange(2, 0));
            native.UnmarkText();
            await Drain();
            Equal("a日本c", editor.Document.Text);
            native.InsertText("x");
            await Drain();
            Equal("a日本c", editor.Document.Text);
        });

        await Test("end editing invokes Completed and ReturnCommand once", async () =>
        {
            var completed = 0;
            var invoked = 0;
            var parameter = new object();
            void OnCompleted(object? sender, EventArgs args) => completed++;
            editor.Completed += OnCompleted;
            editor.ReturnCommandParameter = parameter;
            editor.ReturnCommand = new Command<object>(value => { Equal(parameter, value); invoked++; });
            try
            {
                native.ResignFirstResponder();
                await Drain();
                Equal(1, completed);
                Equal(1, invoked);
            }
            finally { editor.Completed -= OnCompleted; }
        });

        foreach (var test in EditorContractTests.Cases)
        {
            await Test(test.Name, () => test.Run(editor));
        }

        pasteboard.Items = [];
        results.Add($"COMPLETE {results.Count} tests, {results.Count(result => result.StartsWith("FAIL"))} failures");
        Save();
        Console.WriteLine(results[^1]);
    }

    private static byte[] VerifyListMarkerPixels(RichTextView native, int position, double textIndent = 36)
    {
        native.LayoutManager.EnsureLayoutForTextContainer(native.TextContainer);
        native.LayoutIfNeeded();
        using var start = native.GetPosition(native.BeginningOfDocument, position)!;
        var caret = native.GetCaretRectForPosition(start);
        var inset = native.TextContainerInset;
        var padding = native.TextContainer.LineFragmentPadding;
        if (caret.X < inset.Left + padding + textIndent - 1)
            throw new InvalidOperationException($"List text overlaps the marker gutter: caret={caret}.");

        using var renderer = new UIGraphicsImageRenderer(new CoreGraphics.CGSize(28, caret.Height));
        using var image = renderer.CreateImage(context =>
        {
            context.CGContext.TranslateCTM(-inset.Left - padding - (nfloat)(textIndent - 36), -caret.Y + native.ContentOffset.Y);
            native.Layer.RenderInContext(context.CGContext);
        });
        using var pixels = image.CGImage!.DataProvider.CopyData()!;
        var data = pixels.ToArray();
        var minimum = 255;
        var maximum = 0;
        for (var i = 0; i < data.Length; i += 4)
        {
            var brightness = (data[i] + data[i + 1] + data[i + 2]) / 3;
            minimum = Math.Min(minimum, brightness);
            maximum = Math.Max(maximum, brightness);
        }
        if (maximum - minimum < 40)
            throw new InvalidOperationException($"No list marker pixels at {position}: brightness={minimum}..{maximum}.");
        return data;
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
