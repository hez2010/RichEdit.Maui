#if (DEBUG || EDITOR_TESTS) && (IOS || MACCATALYST)
using Foundation;
using ObjCRuntime;
using RichEdit.Maui.Platforms.Apple;
using UIKit;

namespace RichEdit.Maui.Tests;

internal static partial class AppleEditorTests
{
    public static Task RunAsync(NativeEditorView editor) => AppleTestEnvironment.RunAsync(() => RunCoreAsync(editor));

    private static async Task RunCoreAsync(NativeEditorView editor)
    {
        var native = (RichTextView)editor.Handler!.PlatformView!;
        // Exercise native transformations explicitly, regardless of simulator defaults.
        native.SmartInsertDeleteType = UITextSmartInsertDeleteType.Yes;
        var results = new List<string>();
        var started = DateTimeOffset.UtcNow;
        var output = Path.Combine(EditorTestOutput.Directory, "apple-editor-tests.txt");
        var pasteboard = UIPasteboard.General;
        // Use only test-owned clipboard data; reading another app's clipboard at
        // startup prompts for paste permission before the test UI can load.
        pasteboard.Items = [];
        void Save() => File.WriteAllText(output,
            $"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; {UIDevice.CurrentDevice.SystemVersion}\n" +
            $"Started {started:O}; library {typeof(NativeEditorView).Assembly.ManifestModule.ModuleVersionId}\n" +
            string.Join("\n", results) + "\n");
        async Task Test(string name, Func<Task> test)
        {
            if (Environment.GetEnvironmentVariable("RICHEDIT_TEST_FILTER") is { Length: > 0 } filter && !name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                return;

            var index = results.Count;
            results.Add($"RUN {name}");
            Save();
            var trace = new Queue<string>();
            void Record(string action)
            {
                trace.Enqueue($"{action}: version={editor.Document.Version}, selection={editor.SelectedRange}, native={native.SelectedRange}, readonly={editor.IsReadOnly}, bold={editor.Document.CurrentSnapshot.Runs.FirstOrDefault()?.Format.Bold}");
                if (trace.Count > 16)
                    trace.Dequeue();
            }
            void SelectionChanged(object? sender, RichTextSelectionChangedEventArgs args) => Record("selection");
            void ContentChanged(object? sender, RichTextContentChangedEventArgs args) => Record("content " + args.ChangeSet.Origin);
            try
            {
                native.ResignFirstResponder();
                var previous = editor;
                var previousHandler = previous.Handler;
                editor = new NativeEditorView();
                var loaded = new TaskCompletionSource();
                void Loaded(object? sender, EventArgs args) => loaded.TrySetResult();
                editor.Loaded += Loaded;
                ((ContentPage)Application.Current!.Windows[0].Page!).Content = editor;
                await loaded.Task;
                editor.Loaded -= Loaded;
                previous.Handler = null;
                previousHandler?.DisconnectHandler();
                native = (RichTextView)editor.Handler!.PlatformView!;
                native.SmartInsertDeleteType = UITextSmartInsertDeleteType.Yes;
                EditorContractTests.Reset(editor);
                native.BecomeFirstResponder();
                // UIKit completes keyboard/focus transitions asynchronously.
                // Start each case after that transition, not during its scrolling animation.
                await Task.Delay(350);
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

        await Test("structured RTF reference uses native viewport fonts and list markers", () => VerifyReferenceLayout(editor));
        await Test("structured native list labels follow table cell geometry", () => VerifyTableListMarkers(editor));
        await Test("structured reference editing retains native input", () => VerifyReferenceEditing(editor));

        async Task VerifyNativeTable(string tableRtf)
        {
            using var data = NSData.FromArray(System.Text.Encoding.ASCII.GetBytes(tableRtf));
            var options = new NSAttributedStringDocumentAttributes { DocumentType = NSDocumentType.RTF };
            using var imported = NSAttributedString.Create(data, options, out var importError);
            if (imported is null || importError is not null) throw new InvalidOperationException(importError?.Description ?? "Native RTF import returned no content.");
            using var view = new UITextView(new CoreGraphics.CGRect(0, 0, 500, 260));
            native.Superview!.AddSubview(view);
            try
            {
                view.AttributedText = imported;
                view.LayoutManager.EnsureLayoutForTextContainer(view.TextContainer);
                view.LayoutIfNeeded();
                var text = view.Text ?? "";
                var alpha = text.IndexOf("alpha", StringComparison.Ordinal);
                var beta = text.IndexOf("beta", StringComparison.Ordinal);
                var gamma = text.IndexOf("gamma", StringComparison.Ordinal);
                if (Math.Min(alpha, Math.Min(beta, gamma)) < 0) throw new InvalidOperationException("Native RTF import lost cell text.");
                using var a = view.GetPosition(view.BeginningOfDocument, alpha)!;
                using var b = view.GetPosition(view.BeginningOfDocument, beta)!;
                using var g = view.GetPosition(view.BeginningOfDocument, gamma)!;
                var alphaRect = view.GetCaretRectForPosition(a);
                var betaRect = view.GetCaretRectForPosition(b);
                var gammaRect = view.GetCaretRectForPosition(g);
                view.BecomeFirstResponder();
                view.SelectedRange = new(beta, 4);
                view.InsertText("BETA");
                using var exported = view.AttributedText!.GetData(new(0, view.AttributedText.Length), options, out var exportError);
                if (exportError is not null) throw new InvalidOperationException(exportError.Description);
                var rtf = System.Text.Encoding.UTF8.GetString(exported!.ToArray());
                File.WriteAllText(Path.Combine(EditorTestOutput.Directory, "native-table-probe.txt"),
                    $"text={text.Replace("\n", "\\n")}\nalpha={alphaRect}; beta={betaRect}; gamma={gammaRect}\nattributes={imported.Description}\nrtf={rtf}");
                EditorContractTests.Equal(2, rtf.Split(@"\trowd", StringSplitOptions.None).Length - 1, "native RTF retains both table rows");
                EditorContractTests.Equal(true, Math.Abs(betaRect.X - alphaRect.X - 72) < 1, "preferred RTF cell widths use typographic points");
                EditorContractTests.Equal(true, Math.Abs(alphaRect.Y - betaRect.Y) < 1 && betaRect.X > alphaRect.X + 30 && gammaRect.Y > alphaRect.Y,
                    "native text engine lays out a two-column, two-row grid");
                Equal(text.Replace("beta", "BETA", StringComparison.Ordinal), view.Text);
                await Drain();
            }
            finally { view.ResignFirstResponder(); view.RemoveFromSuperview(); native.BecomeFirstResponder(); }
        }

        await Test("native RTF tables retain editable cells and row geometry", async () =>
        {
            const string tableRtf = @"{\rtf1\ansi\deff0{\fonttbl{\f0 Arial;}}\fs24
\trowd\trgaph108\trleft0\trautofit0\trftsWidth3\trwWidth2880\clftsWidth3\clwWidth1440\cellx1440\clftsWidth3\clwWidth1440\cellx2880\intbl alpha\cell beta\cell\row
\trowd\trgaph108\trleft0\trautofit0\trftsWidth3\trwWidth2880\clftsWidth3\clwWidth1440\cellx1440\clftsWidth3\clwWidth1440\cellx2880\intbl gamma\cell delta\cell\row\pard\par}";
            await VerifyNativeTable(tableRtf);
        });

        await Test("structured RTF tables retain native cells and widths", async () =>
        {
            var snapshot = RichEdit.Core.RichDocumentSnapshot.Build(edit =>
            {
                var table = edit.InsertTable(edit.Body, 1, 2, (RichEdit.Core.RichLength[])[new(1440), new(1440)]);
                var initial = edit.Snapshot;
                var grid = initial.GetTableGrid(table);
                var labels = new[] { "alpha", "beta", "gamma", "delta" };
                for (var row = 0; row < 2; row++)
                    for (var column = 0; column < 2; column++)
                    {
                        var paragraph = initial.Get<RichEdit.Core.RichTableCell>(grid[row, column]).Blocks[0];
                        edit.ReplaceText(new(edit.Body, paragraph, 0), 0, labels[row * 2 + column]);
                    }
                edit.DeleteBlock(new(2));
            });
            await VerifyNativeTable(await StructuredRichProjection.CreateRtfAsync(snapshot));
            using var saved = new MemoryStream();
            await RichEdit.Core.Rtf.RtfCodec.WriteAsync(snapshot, saved);
            await VerifyNativeTable(System.Text.Encoding.ASCII.GetString(saved.ToArray()));
        });

        await Test("structured table maps retain native empty cells and field addresses", async () =>
        {
            var snapshot = RichEdit.Core.RichDocumentSnapshot.Build(edit =>
            {
                var table = edit.InsertTable(edit.Body, 1, 2, (RichEdit.Core.RichLength[])[new(2400), new(2400)]);
                var initial = edit.Snapshot;
                var first = initial.Get<RichEdit.Core.RichTableCell>(initial.GetTableGrid(table)[0, 0]).Blocks[0];
                edit.ReplaceText(new(edit.Body, first, 0), 0, "literal\uFFF9\uFFFB\uFFFC");
                edit.InsertField(new(edit.Body, first, 10), "DATE", "2026");
                edit.SetHyperlink(new(edit.Body, first, 0), 7, new("https://example.test"));
                edit.InsertTable(initial.GetTableGrid(table)[1, 1], 1, 1, (RichEdit.Core.RichLength[])[new(900)]);
                edit.DeleteBlock(new(2));
            });
            var prepared = await StructuredRichNativeMap.PrepareAsync(snapshot);
            using var data = NSData.FromArray(System.Text.Encoding.ASCII.GetBytes(prepared.Rtf));
            using var imported = NSAttributedString.Create(data, new NSAttributedStringDocumentAttributes { DocumentType = NSDocumentType.RTF }, out var error);
            if (imported is null || error is not null) throw new InvalidOperationException(error?.Description ?? "Missing imported RTF.");
            using var content = new NSMutableAttributedString(imported);
            var resolved = prepared.Resolve(content.Value ?? "");
            foreach (var removal in resolved.Removals.Reverse()) content.Replace(new NSRange(removal.Start, removal.Length), "");
            EditorContractTests.Equal(resolved.Map.NativeText, content.Value, "native-only markers removed before display");
            foreach (var paragraph in snapshot.Nodes.Values.OfType<RichEdit.Core.RichParagraph>())
                for (var offset = 0; offset <= snapshot.GetParagraphLength(paragraph.Id); offset++)
                {
                    var point = new RichEdit.Core.RichTextPoint(snapshot.Body, paragraph.Id, offset);
                    EditorContractTests.Equal(point, resolved.Map.ToRich(resolved.Map.ToNative(point))!.Value, "structural/native caret round trip");
                }
        });

        await Test("structured authored images retain native table attachments", async () =>
        {
            RichEdit.Core.RichNodeId paragraph = default;
            var model = new RichEdit.Core.RichDocument(RichEdit.Core.RichDocumentSnapshot.Build(edit =>
            {
                var table = edit.InsertTable(edit.Body, 1, 1, new RichEdit.Core.RichLength[] { new(2400) });
                paragraph = edit.Snapshot.Get<RichEdit.Core.RichTableCell>(edit.Snapshot.GetTableGrid(table)[0, 0]).Blocks[0];
                var asset = edit.AddImageAsset(RichEdit.Core.RichImageKind.Png, RichTextDisplayProjection.TransparentPixel.AsMemory());
                edit.InsertImage(new(edit.Body, paragraph, 0), asset, new(640), new(480));
            }));
            var previous = editor.Document;
            try
            {
                editor.BindStructuredDocument(model);
                await Drain();
                var point = new RichEdit.Core.RichTextPoint(model.CurrentSnapshot.Body, paragraph, 0);
                var sourceOffset = model.CurrentSnapshot.ProjectText().GetOffset(point);
                var position = ((RichEditorHandler)editor.Handler!).NativeProjection.ToDisplayCaret(sourceOffset);
                Equal('\uFFFC', native.Text![position]);
                using var attributes = native.TextStorage.GetAttributes(position, out _);
                var attachment = new UIStringAttributes(attributes).TextAttachment ?? throw new InvalidOperationException("Missing native table image attachment.");
                var glyphs = native.LayoutManager.GetGlyphRange(new NSRange(position, 1));
                var bounds = native.LayoutManager.GetBoundingRect(glyphs, native.TextContainer);
                EditorContractTests.Equal(true, Math.Abs(bounds.Width - 32) < 1 && bounds.Height >= 24,
                    $"table image glyph={bounds}, attachment={attachment.Bounds}, image={attachment.Image?.Size}, attributes={attributes?.Description}");
                Equal(false, model.CanUndo);
                editor.SelectedRange = new(sourceOffset + 1, 0);
                native.InsertText("x");
                await Drain();
                Equal("\uFFFCx", model.CurrentSnapshot.ProjectText(paragraph).Text.ToString());
                editor.Undo();
                await Drain();
                Equal("\uFFFC", model.CurrentSnapshot.ProjectText(paragraph).Text.ToString());
                Equal(1, model.CurrentSnapshot.Assets.Count);
            }
            finally { editor.Document = previous; }
        });

        await Test("native smart selection transformations remain tracked", async () =>
        {
            editor.Selection.ReplaceText("one\nsecond");
            editor.ClearUndoHistory();
            var before = editor.Document.CurrentSnapshot;
            editor.SelectedRange = new TextSpan(5, 0);
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
            Equal(new TextSpan(10, 0), editor.SelectedRange);
            Equal("one\nsecond", native.Text);
            await Drain();
            editor.Undo();
            await Drain();
            Equal("", editor.Document.Text);
            Equal(TextSpan.Empty, editor.SelectedRange);
            editor.Redo();
            await Drain();
            Equal("one\nsecond", editor.Document.Text);
            Equal(new TextSpan(10, 0), editor.SelectedRange);
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

        await Test("native scrolling remains free after formatting", async () =>
        {
            editor.Selection.ReplaceText(string.Concat(Enumerable.Repeat("Paragraph text for scrolling.\n", 150)));
            editor.SelectedRange = new TextSpan(31, 9);
            await Drain();
            async Task VerifyFreeScrolling(string operation)
            {
                await Drain();
                native.SetContentOffset(new CoreGraphics.CGPoint(0, 200), false);
                await Drain();
                EditorContractTests.Equal(true, native.ContentOffset.Y > 150,
                    $"native scrolling after {operation}: {native.ContentOffset.Y}");
                Equal(new TextSpan(31, 9), editor.SelectedRange);
            }
            editor.Selection.ToggleBold();
            await VerifyFreeScrolling("character formatting");
            editor.Selection.ParagraphFormat.Alignment = RichTextAlignment.Center;
            await VerifyFreeScrolling("paragraph formatting");
            editor.Selection.CharacterFormat.FontSize = 24;
            await VerifyFreeScrolling("font size");
            editor.Undo();
            await VerifyFreeScrolling("undo");
            editor.Redo();
            await VerifyFreeScrolling("redo");

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

        await Test("native scrolling remains free after text undo and redo", async () =>
        {
            var source = string.Concat(Enumerable.Repeat("Paragraph text for scrolling.\n", 150));
            editor.Document = RichTextDocument.FromPlainText(source);
            editor.SelectedRange = new TextSpan(31, 0);
            native.InsertText("edit");
            await Task.Delay(100);
            native.SetContentOffset(new CoreGraphics.CGPoint(0, 200), false);
            await Task.Delay(100);
            async Task VerifyViewport(string operation)
            {
                await Task.Delay(500);
                native.SetContentOffset(new CoreGraphics.CGPoint(0, 200), false);
                // The native viewport must remain free to leave the selection.
                for (var frame = 0; frame < 15; frame++)
                {
                    await Task.Delay(20);
                    EditorContractTests.Equal(true, native.ContentOffset.Y > 150,
                        $"native scrolling after {operation}: {native.ContentOffset.Y}");
                }
            }
            editor.Undo();
            await VerifyViewport("undo");
            Equal(source, editor.Document.Text);
            Equal(new TextSpan(31, 0), editor.SelectedRange);
            editor.Redo();
            await VerifyViewport("redo");
            Equal(source.Insert(31, "edit"), editor.Document.Text);
            Equal(new TextSpan(35, 0), editor.SelectedRange);

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

        await Test("explicit reveal changes the viewport without moving selection", async () =>
        {
            editor.Document = RichTextDocument.FromPlainText(string.Concat(Enumerable.Repeat("line\n", 200)));
            editor.SelectedRange = new(0, 4);
            editor.Selection.ToggleBold();
            await Drain();
            var selection = editor.SelectionState;
            editor.ScrollIntoView(new(editor.Document.Length, 0));
            await Task.Delay(200);
            Equal(selection, editor.SelectionState);
            EditorContractTests.Equal(true, native.ContentOffset.Y > 200, "explicit bottom reveal");
            editor.ScrollIntoView(new(0, 0));
            await Task.Delay(200);
            Equal(selection, editor.SelectionState);
            EditorContractTests.Equal(true, native.ContentOffset.Y < 20, "explicit top reveal");
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
                FontWeight = 700,
                StyleName = "Owned style",
                Ligatures = RichTextFeatureMode.Disabled,
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
            Equal(new TextSpan(0, 5), editor.SelectedRange);
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
            finally
            {
                editor.Pasting -= cancel;
            }
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
                edit.SetParagraphFormat(new TextSpan(0, 12), RichTextParagraphFormat.Default with
                {
                    Alignment = RichTextAlignment.Distributed,
                    LeadingIndent = 18.5,
                    LineSpacingRule = RichTextLineSpacingRule.Exactly,
                    LineSpacing = 20,
                    TabStops = [new RichTextTabStop(24.5, RichTextTabAlignment.Right, RichTextTabLeader.Dots)],
                });
            });
            var before = editor.Document.CurrentSnapshot;
            editor.SelectedRange = new TextSpan(editor.Document.Length, 0);
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
            editor.SelectedRange = new TextSpan(editor.Document.Length, 0);
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
            editor.SelectedRange = new TextSpan(0, 0);
            editor.Selection.SetList(new RichTextListDefinition([
                new RichTextListLevelDefinition
                {
                    Marker = new RichTextListMarker.Bullet("•"),
                    Prefix = "", Suffix = "", LeadingIndent = 18, FirstLineIndent = -18, MarkerTab = 18,
                }]));
            editor.SelectedRange = new TextSpan(5, 0);
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
            editor.SelectedRange = new TextSpan(itemStart, 1);
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
            editor.SelectedRange = new TextSpan(emptyItem, 0);
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
            png.Save(Path.Combine(EditorTestOutput.Directory, "apple-list-switching.png"), false, out _);

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
                if (firstText.Length > 0)
                    native.InsertText(firstText);

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

        await Test("Backspace through a middle numbered item preserves adjacent bullet lists", async () =>
        {
            editor.Selection.ReplaceText("bullet\nnumber\nfollowing");
            editor.SelectAll();
            editor.Selection.SetList(new RichTextListDefinition([
                new() { Marker = new RichTextListMarker.Bullet("•"), Prefix = "", Suffix = "", LeadingIndent = 18, FirstLineIndent = -18, MarkerTab = 18 },
            ]));
            var bulletId = editor.Document.CurrentSnapshot.Paragraphs[0].Format.List!.ListId;
            editor.SelectedRange = new(7, 6);
            editor.Selection.SetList(new RichTextListDefinition([
                new() { Marker = new RichTextListMarker.Number(RichTextListNumberStyle.Arabic, 1), Prefix = "", Suffix = ".", LeadingIndent = 36, FirstLineIndent = -36, MarkerTab = 36 },
            ]));
            var numberedId = editor.Document.CurrentSnapshot.Paragraphs[1].Format.List!.ListId;
            editor.SelectedRange = new(13, 0);
            await Drain();
            for (var remaining = 5; remaining >= 0; remaining--)
            {
                native.DeleteBackward();
                await Drain();
                Equal("bullet\n" + "number"[..remaining] + "\nfollowing", editor.Document.Text);
                CheckItems();
            }

            native.InsertText("again");
            await Drain();
            CheckItems();
            editor.Undo();
            await Drain();
            Equal("bullet\n\nfollowing", editor.Document.Text);
            CheckItems();
            native.DeleteBackward();
            await Drain();
            Equal("bullet\nfollowing", editor.Document.Text);
            Equal(true, editor.Document.CurrentSnapshot.Paragraphs.All(paragraph => paragraph.Format.List?.ListId == bulletId));

            void CheckItems()
            {
                var paragraphs = editor.Document.CurrentSnapshot.Paragraphs;
                Equal(3, paragraphs.Length);
                Equal(bulletId, paragraphs[0].Format.List!.ListId);
                Equal(numberedId, paragraphs[1].Format.List!.ListId);
                Equal(bulletId, paragraphs[2].Format.List!.ListId);
                Equal(36d, paragraphs[1].Format.LeadingIndent);
                Equal(-36d, paragraphs[1].Format.FirstLineIndent);
            }
        });

        foreach (var retainParagraphMetadata in new[] { false, true })
        {
            await Test($"native list ownership overrides inherited paragraph metadata ({retainParagraphMetadata})", async () =>
            {
                editor.Selection.ReplaceText("bullet\nnumber");
                editor.SelectedRange = new(0, 6);
                editor.Selection.SetList(new RichTextListDefinition([
                    new() { Marker = new RichTextListMarker.Bullet("•"), Prefix = "", Suffix = "", LeadingIndent = 18, FirstLineIndent = -18, MarkerTab = 18 },
                ]));
                editor.SelectedRange = new(7, 6);
                editor.Selection.SetList(new RichTextListDefinition([
                    new() { Marker = new RichTextListMarker.Number(RichTextListNumberStyle.Arabic, 1), Prefix = "", Suffix = ".", LeadingIndent = 36, FirstLineIndent = -36, MarkerTab = 36 },
                ]));
                await Drain();
                var before = editor.Document.CurrentSnapshot;
                var inherited = native.TextStorage.GetAttributes(0, out _) ?? throw new InvalidOperationException("Missing bullet attributes.");
                using var attributes = new NSMutableDictionary(inherited);
                var numbered = new UIStringAttributes(native.TextStorage.GetAttributes(7, out _));
                attributes[UIStringAttributeKey.ParagraphStyle] = numbered.ParagraphStyle!;
                if (!retainParagraphMetadata)
                {
                    using var key = new NSString("RichEdit.Maui.ParagraphFormat");
                    attributes.Remove(key);
                }

                // Leave the model's first paragraph bulleted while its native list
                // changes to the existing numbered list, with stale or absent metadata.
                native.TextStorage.SetAttributes(attributes, new NSRange(0, 7));
                await Drain();
                var after = editor.Document.CurrentSnapshot;
                Equal(before.Paragraphs[1].Format.List!.ListId, after.Paragraphs[0].Format.List!.ListId);
                Equal(36d, after.Paragraphs[0].Format.LeadingIndent);
                Equal(-36d, after.Paragraphs[0].Format.FirstLineIndent);
                Equal(before.Lists[before.Paragraphs[1].Format.List!.ListId], after.Lists[after.Paragraphs[0].Format.List!.ListId]);
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
            editor.SelectedRange = new TextSpan(4, 0);
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
            if (!OperatingSystem.IsIOSVersionAtLeast(16) && !OperatingSystem.IsMacCatalystVersionAtLeast(16))
                return;

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
            while (editor.CanUndo && count++ < 20)
            {
                editor.Undo();
                await Drain();
            }

            Equal("", editor.Document.Text);
            while (editor.CanRedo && count-- > -20)
            {
                editor.Redo();
                await Drain();
            }

            EditorContractTests.SameContent(final, editor.Document.CurrentSnapshot);
        });

        await Test("metadata edits wait for native IME composition to complete", async () =>
        {
            native.SetMarkedText("n", new NSRange(1, 0));
            var rejected = false;
            try { editor.Document.Edit(edit => edit.SetMetadata("author", "test")); }
            catch (InvalidOperationException) { rejected = true; }
            Equal(true, rejected);
            Equal(true, native.MarkedTextRange is not null);
            native.SetMarkedText("日本", new NSRange(2, 0));
            native.UnmarkText();
            await Drain();
            editor.Document.Edit(edit => edit.SetMetadata("author", "test"));
            Equal("日本", editor.Document.Text);
            Equal("test", editor.Document.CurrentSnapshot.Metadata["author"]);
        });

        await Test("marked text respects replacement length budget", async () =>
        {
            editor.Selection.ReplaceText("abc");
            editor.SelectedRange = new TextSpan(1, 1);
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
            editor.ReturnCommand = new Command<object>(value =>
            {
                Equal(parameter, value);
                invoked++;
            });
            try
            {
                native.ResignFirstResponder();
                await Drain();
                Equal(1, completed);
                Equal(1, invoked);
            }
            finally
            {
                editor.Completed -= OnCompleted;
            }
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
            native.DrawViewHierarchy(new CoreGraphics.CGRect(0, 0, native.Bounds.Width, native.Bounds.Height), true);
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
        {
            using var fullRenderer = new UIGraphicsImageRenderer(native.Bounds.Size);
            using var fullImage = fullRenderer.CreateImage(_ => native.DrawViewHierarchy(new CoreGraphics.CGRect(0, 0, native.Bounds.Width, native.Bounds.Height), true));
            using var png = fullImage.AsPNG();
            File.WriteAllBytes(Path.Combine(EditorTestOutput.Directory, "list-marker-failure.png"), png!.ToArray());
            throw new InvalidOperationException($"No list marker pixels at {position}: brightness={minimum}..{maximum}, caret={caret}, offset={native.ContentOffset}, bounds={native.Bounds}.");
        }

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
