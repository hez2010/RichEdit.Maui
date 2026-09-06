#if DEBUG
using Android.Text;
using Android.Text.Style;
using RichEdit.Maui.Platforms.Android;

namespace RichEdit.Maui.TestApp;

// Run on an emulator with: adb shell am start -n <activity> --ez run-editor-tests true
internal static class AndroidEditorTests
{
    public static async Task RunAsync(RichEditor editor, string? filter = null)
    {
        var native = (RichEditText)editor.Handler!.PlatformView!;
        var results = new List<string>();
        var started = DateTimeOffset.UtcNow;
        var clipboard = (Android.Content.ClipboardManager)Android.App.Application.Context
            .GetSystemService(Android.Content.Context.ClipboardService)!;
        var previousClip = clipboard.PrimaryClip;
        var output = Path.Combine(FileSystem.CacheDirectory, "editor-tests.txt");
        void Save() => File.WriteAllText(output,
            $"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; Android {Android.OS.Build.VERSION.Release} API {Android.OS.Build.VERSION.SdkInt}\n" +
            $"Started {started:O}; library {typeof(RichEditor).Assembly.ManifestModule.ModuleVersionId}\n" +
            string.Join("\n", results) + "\n");

        async Task Test(string name, Func<Task> test)
        {
            if (!string.IsNullOrEmpty(filter) && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) return;
            var index = results.Count;
            results.Add($"RUN {name}");
            Save();
            try
            {
                EditorContractTests.Reset(editor);
                await test();
                await EditorContractTests.Verify(editor);
                results[index] = $"PASS {name}";
            }
            catch (Exception exception)
            {
                results[index] = $"FAIL {name}: {exception}";
            }
            Save();
            Android.Util.Log.Info("RichEditTests", results[index]);
        }

        Task Sync(Action action) { action(); return Task.CompletedTask; }

        await Test("text, selection, undo and redo", () => Sync(() =>
        {
            editor.Selection.ReplaceText("one\nsecond");
            Equal(editor.Document.Text, native.Text);
            Equal(new RichTextRange(10, 0), editor.SelectedRange);
            editor.Undo();
            Equal("", native.Text);
            editor.Redo();
            Equal("one\nsecond", native.Text);
        }));

        await Test("native typing uses caret format", () => Sync(() =>
        {
            editor.Selection.ToggleBold();
            native.EditableText!.Insert(0, "a");
            Equal("a", editor.Document.Text);
            Equal(true, editor.Document.GetCharacterFormat(new RichTextRange(0, 1)).RepresentativeFormat.Bold);
        }));

        await Test("hardware Tab respects AcceptsTab and MaxLength", () => Sync(() =>
        {
            using var key = new Android.Views.KeyEvent(Android.Views.KeyEventActions.Down, Android.Views.Keycode.Tab);
            editor.AcceptsTab = true;
            native.OnKeyDown(Android.Views.Keycode.Tab, key);
            Equal("\t", editor.Document.Text);
            editor.MaxLength = 1;
            native.OnKeyDown(Android.Views.Keycode.Tab, key);
            Equal("\t", editor.Document.Text);
        }));

        await Test("email keyboard keeps multiline layout", () => Sync(() =>
        {
            editor.Keyboard = Keyboard.Email;
            editor.Selection.ReplaceText("one\ntwo");
            Equal(true, native.MaxLines > 1);
            Equal("one\ntwo", native.Text);
        }));

        await Test("soft breaks create native lines without creating document paragraphs", async () =>
        {
            editor.Selection.ReplaceText("one\u2028two");
            await EditorContractTests.Verify(editor);
            Equal(1, editor.Document.CurrentSnapshot.Paragraphs.Length);
            Equal(0, native.Layout!.GetLineForOffset(0));
            Equal(1, native.Layout.GetLineForOffset(4));
            editor.Selection.UpdateParagraphFormat(format => format with { LeadingIndent = 24, FirstLineIndent = 12 });
            await EditorContractTests.Verify(editor);
            var indent = Android.Util.TypedValue.ApplyDimension(Android.Util.ComplexUnitType.Dip, 12, native.Resources!.DisplayMetrics);
            EditorContractTests.Equal(true, Math.Abs(native.Layout.GetPrimaryHorizontal(0) - native.Layout.GetPrimaryHorizontal(4) - indent) < 1,
                $"soft-line indents: first={native.Layout.GetPrimaryHorizontal(0)}, next={native.Layout.GetPrimaryHorizontal(4)}, expected difference={indent}; spans=" +
                string.Join(";", native.EditableText!.GetSpans(0, 7, Java.Lang.Class.FromType(typeof(LeadingMarginSpanStandard)))!.OfType<LeadingMarginSpanStandard>()
                    .Select(span => $"{native.EditableText.GetSpanStart(span)}..{native.EditableText.GetSpanEnd(span)}={span.GetLeadingMargin(true)}/{span.GetLeadingMargin(false)}")));
            editor.SelectedRange = new RichTextRange(4, 3);
            EditorContractTests.NativeReplace(editor, "next");
            await EditorContractTests.Verify(editor);
            Equal("one\u2028next", editor.Document.Text);
            editor.Undo();
            Equal("one\u2028two", editor.Document.Text);
        });

        await Test("fractional font sizes reach native text paint", () => Sync(() =>
        {
            editor.Selection.CharacterFormat.FontSize = 16.5;
            editor.Selection.ReplaceText("x");
            using var paint = new TextPaint(native.Paint!);
            foreach (var span in native.EditableText!.GetSpans(0, 1, Java.Lang.Class.FromType(typeof(MetricAffectingSpan)))!.OfType<MetricAffectingSpan>())
            {
                span.UpdateMeasureState(paint);
            }
            var expected = Android.Util.TypedValue.ApplyDimension(Android.Util.ComplexUnitType.Sp, 16.5f, native.Resources!.DisplayMetrics);
            Equal(true, Math.Abs(paint.TextSize - expected) < 0.001);
        }));

        await Test("hidden text remains transparent with an authored foreground", () => Sync(() =>
        {
            editor.Selection.UpdateCharacterFormat(format => format with { Hidden = true, ForegroundColor = Colors.Red });
            editor.Selection.ReplaceText("x");
            using var paint = new TextPaint(native.Paint!);
            foreach (var span in native.EditableText!.GetSpans(0, 1, Java.Lang.Class.FromType(typeof(CharacterStyle)))!.OfType<CharacterStyle>())
            {
                span.UpdateDrawState(paint);
            }
            Equal((byte)0, paint.Color.A);
        }));

        await Test("paragraph edits retain inherited font and precise paragraph values", () => Sync(() =>
        {
            editor.Document.Edit(edit =>
            {
                edit.SetDefaultCharacterFormat(RichTextCharacterFormat.Default with { FontSize = 16.5, ForegroundColor = Colors.Red });
                edit.InsertText(0, "first\nsecond");
                edit.UpdateParagraphFormat(new RichTextRange(0, 12), format => format with
                {
                    Alignment = RichTextAlignment.Distributed,
                    LeadingIndent = 18.5,
                    TabStops = [new RichTextTabStop(24.5, RichTextTabAlignment.Right, RichTextTabLeader.Dots)],
                });
            });
            var before = editor.Document.CurrentSnapshot;
            native.EditableText!.Insert(native.Length(), "\nnew");
            Equal(before.Runs[0].Format, editor.Document.CurrentSnapshot.Runs[0].Format);
            Equal(before.Paragraphs[0].Format, editor.Document.CurrentSnapshot.Paragraphs[0].Format);
        }));

        await Test("native copy retains rich fields", async () =>
        {
            editor.Selection.InsertField("DATE", "today");
            editor.SelectAll();
            editor.Selection.ToggleBold();
            native.OnTextContextMenuItem(Android.Resource.Id.Copy);
            await Task.Yield();
            editor.Document = new RichTextDocument();
            await editor.PasteAsync();
            Equal("today", editor.Document.Text);
            Equal(1, editor.Document.CurrentSnapshot.Fields.Length);
            Equal(true, editor.Document.CurrentSnapshot.Runs[0].Format.Bold);
        });

        await Test("native cut and context undo restore fields", async () =>
        {
            editor.Selection.InsertField("DATE", "today");
            editor.SelectAll();
            editor.ClearUndoHistory();
            var before = editor.Document.RtfText;
            native.OnTextContextMenuItem(Android.Resource.Id.Cut);
            await Task.Yield();
            Equal("", editor.Document.Text);
            native.OnTextContextMenuItem(Android.Resource.Id.Undo);
            Equal(before, editor.Document.RtfText);
            Equal(RichTextRange.Empty, editor.SelectedRange);
            native.OnTextContextMenuItem(Android.Resource.Id.Redo);
            Equal("", editor.Document.Text);
        });

        await Test("paste as plain text uses destination formatting", async () =>
        {
            editor.Selection.ReplaceText("bold");
            editor.SelectAll();
            editor.Selection.ToggleBold();
            await editor.CopyAsync();
            editor.Document = new RichTextDocument();
            editor.Selection.ToggleItalic();
            native.OnTextContextMenuItem(Android.Resource.Id.PasteAsPlainText);
            await Task.Yield();
            Equal("bold", editor.Document.Text);
            Equal(false, editor.Document.CurrentSnapshot.Runs[0].Format.Bold);
            Equal(true, editor.Document.CurrentSnapshot.Runs[0].Format.Italic);
        });

        await Test("read-only survives input configuration changes", () => Sync(() =>
        {
            editor.Selection.ReplaceText("fixed");
            editor.IsReadOnly = true;
            editor.Keyboard = Keyboard.Email;
            editor.IsSpellCheckEnabled = false;
            Equal(null, native.KeyListener);
            editor.Undo();
            Equal("fixed", editor.Document.Text);
            editor.IsReadOnly = false;
            Equal(true, native.KeyListener is not null);
        }));

        await Test("length limit constrains typing but allows document edits", () => Sync(() =>
        {
            editor.MaxLength = 2;
            native.EditableText!.Insert(0, "abc");
            Equal("ab", editor.Document.Text);
            editor.Selection.ReplaceText("programmatic");
            Equal(editor.Document.Text, native.Text);
            Equal(true, native.Length() > 2);
            editor.Undo();
            Equal("ab", native.Text);
        }));

        await Test("list paragraph split creates separate native marker spans", () => Sync(() =>
        {
            editor.Selection.ReplaceText("one");
            editor.Selection.SetList(new RichTextListDefinition([
                new RichTextListLevelDefinition
                {
                    Marker = new RichTextListMarker.Bullet("*"), Prefix = "", Suffix = "",
                    LeadingIndent = 24, FirstLineIndent = -12, MarkerTab = 24,
                }]));
            native.EditableText!.Insert(native.Length(), "\ntwo");
            Equal(2, editor.Document.CurrentSnapshot.Paragraphs.Count(paragraph => paragraph.Format.List is not null));
            var spans = native.EditableText!.GetSpans(0, native.Length(), Java.Lang.Class.FromType(typeof(Java.Lang.Object)))!;
            Equal(2, spans.Count(span => span.GetType().Name == "RichListMarkerSpan"));
            var expectedIndent = Android.Util.TypedValue.ApplyDimension(Android.Util.ComplexUnitType.Dip, 24, native.Resources!.DisplayMetrics);
            var actualIndent = native.EditableText.GetSpans(0, 3, Java.Lang.Class.FromType(typeof(Java.Lang.Object)))!
                .OfType<ILeadingMarginSpan>().Sum(span => span.GetLeadingMargin(false));
            Equal((int)Math.Round(expectedIndent), actualIndent);
        }));

        await Test("paragraph spacing follows native span positions after typing", () => Sync(() =>
        {
            editor.Selection.ReplaceText("one\nsecond");
            editor.Document.Edit(edit => edit.UpdateParagraphFormat(new RichTextRange(4, 6), format => format with { SpaceBefore = 10 }));
            native.EditableText!.Insert(0, "x");
            var span = native.EditableText!.GetSpans(5, 11, Java.Lang.Class.FromType(typeof(Java.Lang.Object)))!
                .OfType<ILineHeightSpan>().Single();
            using var metrics = new Android.Graphics.Paint.FontMetricsInt { Ascent = -10, Descent = 2, Top = -10, Bottom = 2 };
            span.ChooseHeight(native.EditableText, 5, 11, 0, 0, metrics);
            Equal(true, metrics.Ascent < -10);
        }));

        await Test("IME composition, commit, surrogate deletion and undo", async () =>
        {
            native.RequestFocus();
            using var info = new Android.Views.InputMethods.EditorInfo();
            using var connection = native.OnCreateInputConnection(info)!;
            using var composingN = new Java.Lang.String("n");
            using var composingNi = new Java.Lang.String("ni");
            using var japanese = new Java.Lang.String("日本");
            using var emoji = new Java.Lang.String("😀");
            editor.Selection.ToggleBold();
            connection.SetComposingText(composingN, 1);
            EditorContractTests.Equal(true, editor.Document.CurrentSnapshot.Runs[0].Format.Bold, "first composing character");
            connection.SetComposingText(composingNi, 1);
            EditorContractTests.Equal(true, editor.Document.CurrentSnapshot.Runs[0].Format.Bold, "replaced composing characters");
            connection.CommitText(japanese, 1);
            connection.FinishComposingText();
            await EditorContractTests.Verify(editor);
            Equal("日本", editor.Document.Text);
            Equal(true, editor.Document.CurrentSnapshot.Runs[0].Format.Bold);
            connection.CommitText(emoji, 1);
            Equal("日本😀", editor.Document.Text);
            connection.DeleteSurroundingTextInCodePoints(1, 0);
            await EditorContractTests.Verify(editor);
            Equal("日本", editor.Document.Text);
            var final = editor.Document.CurrentSnapshot;
            var count = 0;
            while (editor.CanUndo && count++ < 20) { editor.Undo(); await EditorContractTests.Verify(editor); }
            Equal("", editor.Document.Text);
            while (editor.CanRedo && count-- > -20) { editor.Redo(); await EditorContractTests.Verify(editor); }
            EditorContractTests.SameContent(final, editor.Document.CurrentSnapshot);
        });

        await Test("metadata edits preserve ongoing IME composition", async () =>
        {
            native.RequestFocus();
            using var info = new Android.Views.InputMethods.EditorInfo();
            using var connection = native.OnCreateInputConnection(info)!;
            using var composing = new Java.Lang.String("n");
            using var committed = new Java.Lang.String("日本");
            connection.SetComposingText(composing, 1);
            editor.Document.Edit(edit => edit.SetMetadata("author", "test"));
            Equal(0, Android.Views.InputMethods.BaseInputConnection.GetComposingSpanStart(native.EditableText!));
            connection.CommitText(committed, 1);
            connection.FinishComposingText();
            await EditorContractTests.Verify(editor);
            Equal("日本", editor.Document.Text);
            Equal("test", editor.Document.CurrentSnapshot.Metadata["author"]);
        });

        await Test("IME batch replacement and length limit", async () =>
        {
            native.RequestFocus();
            editor.Selection.ReplaceText("abc");
            editor.SelectedRange = new RichTextRange(1, 1);
            editor.MaxLength = 4;
            using var info = new Android.Views.InputMethods.EditorInfo();
            using var connection = native.OnCreateInputConnection(info)!;
            using var input = new Java.Lang.String("😀x");
            connection.BeginBatchEdit();
            connection.CommitText(input, 1);
            connection.EndBatchEdit();
            await EditorContractTests.Verify(editor);
            Equal("a😀c", editor.Document.Text);
        });

        await Test("IME action invokes Completed and ReturnCommand once", async () =>
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
                native.OnEditorAction(Android.Views.InputMethods.ImeAction.Done);
                await Task.Yield();
                Equal(1, completed);
                Equal(1, invoked);
            }
            finally { editor.Completed -= OnCompleted; }
        });

        await Test("hardware undo and redo shortcuts", () => Sync(() =>
        {
            editor.Selection.ReplaceText("text");
            using var undo = new Android.Views.KeyEvent(0, 0, Android.Views.KeyEventActions.Down, Android.Views.Keycode.Z, 0, Android.Views.MetaKeyStates.CtrlOn);
            native.OnKeyDown(Android.Views.Keycode.Z, undo);
            Equal("", editor.Document.Text);
            using var redo = new Android.Views.KeyEvent(0, 0, Android.Views.KeyEventActions.Down, Android.Views.Keycode.Z, 0, Android.Views.MetaKeyStates.CtrlOn | Android.Views.MetaKeyStates.ShiftOn);
            native.OnKeyDown(Android.Views.Keycode.Z, redo);
            Equal("text", editor.Document.Text);
        }));

        foreach (var test in EditorContractTests.Cases)
        {
            await Test(test.Name, () => test.Run(editor));
        }

        if (previousClip is not null)
        {
            clipboard.PrimaryClip = previousClip;
        }
        else if (OperatingSystem.IsAndroidVersionAtLeast(28))
        {
            clipboard.ClearPrimaryClip();
        }

        results.Add($"COMPLETE {results.Count} tests, {results.Count(result => result.StartsWith("FAIL"))} failures");
        Save();
        Android.Util.Log.Info("RichEditTests", results[^1]);
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected {expected}; actual {actual}.");
        }
    }
}
#endif
