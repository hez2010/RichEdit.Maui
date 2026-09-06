#if DEBUG
using Android.Text;
using Android.Text.Style;
using RichEdit.Maui.Platforms.Android;

namespace RichEdit.Maui.TestApp;

// Run on an emulator with: adb shell am start -n <activity> --ez run-editor-tests true
internal static class AndroidEditorTests
{
    public static async Task RunAsync(RichEditor editor)
    {
        var native = (RichEditText)editor.Handler!.PlatformView!;
        var results = new List<string>();
        var clipboard = (Android.Content.ClipboardManager)Android.App.Application.Context
            .GetSystemService(Android.Content.Context.ClipboardService)!;
        var previousClip = clipboard.PrimaryClip;

        async Task Test(string name, Func<Task> test)
        {
            try
            {
                editor.IsReadOnly = false;
                editor.MaxLength = -1;
                editor.Keyboard = Keyboard.Default;
                editor.AcceptsTab = false;
                editor.Document = new RichTextDocument();
                await test();
                results.Add($"PASS {name}");
            }
            catch (Exception exception)
            {
                results.Add($"FAIL {name}: {exception}");
            }
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
            Equal(new RichTextRange(0, 5), editor.SelectedRange);
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

        if (previousClip is not null)
        {
            clipboard.PrimaryClip = previousClip;
        }
        else if (OperatingSystem.IsAndroidVersionAtLeast(28))
        {
            clipboard.ClearPrimaryClip();
        }

        var report = string.Join("\n", results);
        File.WriteAllText(Path.Combine(FileSystem.CacheDirectory, "editor-tests.txt"), report);
        foreach (var result in results)
        {
            Android.Util.Log.Info("RichEditTests", result);
        }
        Android.Util.Log.Info("RichEditTests", $"COMPLETE {results.Count} tests, {results.Count(result => result.StartsWith("FAIL"))} failures");
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
