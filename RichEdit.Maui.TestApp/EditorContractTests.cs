using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
#if WINDOWS
using Microsoft.UI.Xaml.Controls;
using WinRT;
#endif

namespace RichEdit.Maui.TestApp;

// Compiled into both the mobile test app and the Windows integration tests.
// Each case operates on a real handler. Native edits deliberately enter through
// the platform text API, rather than calling back into the document model.
internal static partial class EditorContractTests
{
    internal sealed record Case(string Name, Func<RichEditor, Task> Run);

    public static IReadOnlyList<Case> Cases { get; } = CreateCases().ToArray();

    public static void Reset(RichEditor editor)
    {
        editor.IsReadOnly = false;
        editor.MaxLength = -1;
        editor.Keyboard = Keyboard.Default;
        editor.AcceptsTab = false;
        editor.IsSpellCheckEnabled = true;
        editor.IsTextPredictionEnabled = true;
        editor.FontFamily = null;
        editor.FontSize = null;
        editor.TextColor = null;
        editor.Placeholder = string.Empty;
        editor.PlaceholderColor = null;
        editor.AutoSize = EditorAutoSizeOption.Disabled;
        editor.ReturnCommand = null;
        editor.ReturnCommandParameter = null;
        editor.Adornments.Clear();
        editor.Adornments.MarginWidth = 0;
        editor.Document = new RichTextDocument();
    }

#if WINDOWS
    [DynamicWindowsRuntimeCast(typeof(RichEditBox))]
#endif
    private static IEnumerable<Case> CreateCases()
    {
        foreach (var test in FoldingCases()) yield return test;
        foreach (var test in AdornmentCases()) yield return test;
#if ANDROID || IOS || MACCATALYST
        foreach (var test in CharacterFormattingTests.Cases) yield return test;
#endif
        yield return new("directional selection, document history and saved state", async editor =>
        {
            editor.Selection.ReplaceText("123456");
            editor.Document.ClearUndoHistory();
            editor.Document.MarkSaved();
            editor.SelectionState = new RichTextSelectionState(4, 1);
            await Verify(editor);
            Equal(new RichTextSelectionState(4, 1), editor.SelectionState);
            editor.Selection.ReplaceText("9");
            Equal("1956", editor.Document.Text);
            Equal(true, editor.Document.IsModified);
            editor.SelectionState = new RichTextSelectionState(0, 0);
            editor.Document.Undo();
            await Verify(editor);
            Equal("123456", editor.Document.Text);
            Equal(new RichTextSelectionState(4, 1), editor.SelectionState);
            Equal(false, editor.Document.IsModified);
            editor.Document.Redo();
            await Verify(editor);
            Equal("1956", editor.Document.Text);
            Equal(new RichTextSelectionState(2, 2), editor.SelectionState);
        });

        yield return new("asynchronous save points retain intervening changes", async editor =>
        {
            editor.Selection.ReplaceText("12");
            var snapshot = editor.Document.CurrentSnapshot;
            var point = editor.Document.CreateSavePoint();
            editor.Selection.ReplaceText("3");
            editor.Document.MarkSaved(point);
            Equal("12", snapshot.Text);
            Equal(true, editor.Document.IsModified);
            editor.Document.Undo();
            await Verify(editor);
            Equal("12", editor.Document.Text);
            Equal(false, editor.Document.IsModified);
        });

        yield return new("background mutation fails before native or document changes", async editor =>
        {
            editor.Selection.ReplaceText("123");
            var before = editor.Document.CurrentSnapshot;
            var failure = await Task.Run(() =>
            {
                try { editor.Document.Edit(edit => edit.DeleteText(new RichTextRange(0, 3))); return null; }
                catch (Exception exception) { return exception; }
            });
            Equal(true, failure is InvalidOperationException);
            Equal(true, ReferenceEquals(before, editor.Document.CurrentSnapshot));
            await Verify(editor);
            Equal("123", editor.Document.Text);
        });

        yield return new("presentation layers stay out of native input and history", async editor =>
        {
            editor.Selection.ReplaceText("123");
            editor.Document.ClearUndoHistory();
            editor.Document.MarkSaved();
            var before = editor.Document.CurrentSnapshot;
            var rtf = editor.Document.RtfText;
            using var layer = editor.Decorations.CreateLayer();
            layer.Set((RichTextDecoration[])[new(new RichTextRange(0, 3), new RichTextDecorationStyle { ForegroundColor = Colors.Green })]);
            editor.SelectionState = new RichTextSelectionState(1, 1);
            await Verify(editor);
            Equal(true, ReferenceEquals(before, editor.Document.CurrentSnapshot));
            Equal(rtf, editor.Document.RtfText);
            Equal(false, editor.Document.IsModified);
            NativeReplace(editor, "9");
            await Verify(editor);
            Equal(true, editor.Document.CurrentSnapshot.Runs.All(run => run.Format.ForegroundColor is null));
            editor.Document.Undo();
            await Verify(editor);
            Equal("123", editor.Document.Text);
            Equal(false, editor.Document.IsModified);
        });

        foreach (var content in new[] { "empty", "plain", "rich" })
        {
            yield return new($"focus changes leave {content} document history untouched", async editor =>
            {
                SetNativeFocus(editor, false);
                await Task.Delay(100);
                editor.Document = content switch
                {
                    "plain" => RichTextDocument.FromRtf(@"{\rtf1 one\par second}"),
                    "rich" => RichTextDocument.FromRtf(@"{\rtf1\ansi\deff0{\fonttbl{\f0 Arial;}}\fs33\qc\b one\b0\par second}"),
                    _ => new RichTextDocument(),
                };
                editor.ClearUndoHistory();
                var before = editor.Document.CurrentSnapshot;
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    SetNativeFocus(editor, true);
                    await Task.Delay(100);
                    Equal(before.Text, NativeText(editor), "focus changed native text");
                    SetNativeFocus(editor, false);
                    await Task.Delay(100);
                    Equal(before.Text, NativeText(editor), "blur changed native text");
                    Equal(false, editor.CanUndo, $"{content} focus cycle {attempt}; before={before.RtfText}; after={editor.Document.RtfText}");
                    Equal(true, ReferenceEquals(before, editor.Document.CurrentSnapshot), "focus changed document version");
                }
                editor.SelectedRange = new RichTextRange(editor.Document.Length, 0);
                editor.Selection.ReplaceText("!");
                editor.Undo();
                Equal(true, editor.CanRedo);
                SetNativeFocus(editor, true);
                await Task.Delay(100);
                SetNativeFocus(editor, false);
                await Task.Delay(100);
                Equal(true, editor.CanRedo, "focus invalidated redo");
                Equal(false, editor.CanUndo);
                await Verify(editor);
            });
        }

        yield return new("Unicode, newline normalization and native UTF-16 selection", async editor =>
        {
            editor.Selection.ReplaceText("A😀e\u0301\r\n日本語\rאבג\u2028\t\u00a0👩‍💻");
            Equal("A😀e\u0301\n日本語\nאבג\u2028\t\u00a0👩‍💻", editor.Document.Text);
            await Verify(editor);
            NativeSelect(editor, new RichTextRange(1, 2));
            await Drain();
            Equal("😀", editor.Selection.Text);
            NativeReplace(editor, "🦊");
            await Verify(editor);
            Equal("A🦊e\u0301\n日本語\nאבג\u2028\t\u00a0👩‍💻", editor.Document.Text);
            editor.Undo();
            await Verify(editor);
            Equal("😀", editor.Document.GetText(new RichTextRange(1, 2)));
        });

        yield return new("native selection replacement, backward selection and history", async editor =>
        {
            editor.Selection.ReplaceText("first\nsecond\nthird");
            editor.ClearUndoHistory();
            NativeSelect(editor, new RichTextRange(3, 7), backwards: true);
            await Drain();
            Equal(new RichTextRange(3, 7), editor.SelectedRange);
            NativeReplace(editor, "X\nY");
            await Verify(editor);
            Equal("firX\nYnd\nthird", editor.Document.Text);
            editor.Undo();
            await Verify(editor);
            Equal("first\nsecond\nthird", editor.Document.Text);
            editor.Redo();
            await Verify(editor);
            Equal("firX\nYnd\nthird", editor.Document.Text);
        });

        yield return new("history restores content and the recorded selection", async editor =>
        {
            editor.Selection.ReplaceText("abcdef");
            editor.ClearUndoHistory();
            editor.SelectedRange = new RichTextRange(1, 2);
            Equal(false, editor.CanUndo);
            editor.Selection.ToggleBold();
            editor.SelectedRange = new RichTextRange(5, 0);
            editor.Undo();
            await Verify(editor);
            Equal(new RichTextRange(1, 2), editor.SelectedRange);
            Equal(false, editor.Document.GetCharacterFormat(new RichTextRange(1, 2)).RepresentativeFormat.Bold);
            editor.SelectedRange = new RichTextRange(0, 2);
            Equal(true, editor.CanRedo);
            editor.Redo();
            await Verify(editor);
            Equal(new RichTextRange(1, 2), editor.SelectedRange);
            Equal(true, editor.Document.GetCharacterFormat(new RichTextRange(1, 2)).RepresentativeFormat.Bold);

            editor.ClearUndoHistory();
            editor.SelectedRange = new RichTextRange(6, 0);
            editor.Selection.ReplaceText(" tail");
            editor.SelectedRange = new RichTextRange(0, 1);
            editor.Undo();
            await Verify(editor);
            Equal("abcdef", editor.Document.Text);
            Equal(new RichTextRange(6, 0), editor.SelectedRange);
            editor.Redo();
            await Verify(editor);
            Equal("abcdef tail", editor.Document.Text);
            Equal(new RichTextRange(11, 0), editor.SelectedRange);
            editor.SelectedRange = new RichTextRange(editor.Document.Length, 0);
            editor.Undo();
            await Verify(editor);
            Equal(new RichTextRange(6, 0), editor.SelectedRange);
        });

        yield return new("formatting mixed selections and clearing inherited values", async editor =>
        {
            editor.Selection.ReplaceText("abcdef");
            editor.SelectedRange = new RichTextRange(0, 3);
            editor.Selection.ToggleBold();
            editor.Selection.CharacterFormat.FontSize = 24;
            editor.SelectAll();
            Equal(true, editor.Selection.CharacterFormat.IsFontWeightMixed);
            Equal(true, editor.Selection.CharacterFormat.IsFontSizeMixed);
            editor.Selection.ToggleBold();
            Equal(true, editor.Document.GetCharacterFormat(editor.SelectedRange).RepresentativeFormat.Bold);
            Equal(false, editor.Selection.CharacterFormat.IsFontWeightMixed);
            editor.Selection.CharacterFormat.FontSize = null;
            Equal(true, editor.Selection.CharacterFormat.IsFontSizeInherited);
            editor.Selection.ToggleBold();
            Equal(false, editor.Document.CurrentSnapshot.Runs[0].Format.Bold);
            await Verify(editor);
        });

        yield return new("caret formatting affects only subsequent native input", async editor =>
        {
            editor.Selection.ReplaceText("ab");
            editor.ClearUndoHistory();
            editor.SelectedRange = new RichTextRange(1, 0);
            editor.Selection.ToggleItalic();
            editor.Selection.ToggleScript(RichTextScript.Superscript);
            Equal(false, editor.CanUndo);
            NativeReplace(editor, "x");
            await Verify(editor);
            Equal("axb", editor.Document.Text);
            Equal(true, editor.Document.GetCharacterFormat(new RichTextRange(1, 1)).RepresentativeFormat.Italic);
            Equal(RichTextScript.Superscript, editor.Document.GetCharacterFormat(new RichTextRange(1, 1)).RepresentativeFormat.Script);
            Equal(false, editor.Document.GetCharacterFormat(new RichTextRange(0, 1)).RepresentativeFormat.Italic);
            Equal(false, editor.Document.GetCharacterFormat(new RichTextRange(2, 1)).RepresentativeFormat.Italic);
        });

        yield return new("appearance and input settings never author content or history", async editor =>
        {
            editor.Selection.ReplaceText("one\ntwo");
            editor.SelectedRange = new RichTextRange(1, 3);
            editor.ClearUndoHistory();
            var before = editor.Document.CurrentSnapshot;
            foreach (var keyboard in new[] { Keyboard.Email, Keyboard.Numeric, Keyboard.Telephone, Keyboard.Url, Keyboard.Default })
            {
                editor.Keyboard = keyboard;
                editor.IsSpellCheckEnabled = !editor.IsSpellCheckEnabled;
                editor.IsTextPredictionEnabled = !editor.IsTextPredictionEnabled;
                editor.FontSize = 22;
                editor.TextColor = Colors.Blue;
                editor.Placeholder = "Write here";
                editor.PlaceholderColor = Colors.Gray;
                editor.AutoSize = EditorAutoSizeOption.TextChanges;
                await Verify(editor);
                Equal(new RichTextRange(1, 3), editor.SelectedRange);
                Equal(true, ReferenceEquals(before, editor.Document.CurrentSnapshot));
                Equal(false, editor.CanUndo);
            }
        });

        yield return new("native rendering attributes match authored character and paragraph formats", async editor =>
        {
            editor.Selection.ReplaceText("Rendered text");
            editor.SelectAll();
            editor.Selection.UpdateCharacterFormat(format => format with
            {
                FontWeight = 700, Italic = true, FontSize = 18, Underline = RichTextUnderlineStyle.Single,
                ForegroundColor = Colors.Red, BackgroundColor = Colors.Yellow,
            });
            editor.Selection.ParagraphFormat.Alignment = RichTextAlignment.Center;
            await Verify(editor);
#if WINDOWS
            var native = (Microsoft.UI.Xaml.Controls.RichEditBox)editor.Handler!.PlatformView!;
            var range = native.Document.GetRange(0, 1);
            Equal(Microsoft.UI.Text.FormatEffect.On, range.CharacterFormat.Bold);
            Equal(Microsoft.UI.Text.FormatEffect.On, range.CharacterFormat.Italic);
            Equal(18f, range.CharacterFormat.Size);
            Equal((byte)255, range.CharacterFormat.ForegroundColor.R);
            Equal((byte)255, range.CharacterFormat.BackgroundColor.G);
            Equal(Microsoft.UI.Text.ParagraphAlignment.Center, range.ParagraphFormat.Alignment);
#elif ANDROID
            var native = (Android.Widget.EditText)editor.Handler!.PlatformView!;
            using var paint = new Android.Text.TextPaint(native.Paint!);
            foreach (var span in native.EditableText!.GetSpans(0, 1, Java.Lang.Class.FromType(typeof(Android.Text.Style.CharacterStyle)))!.OfType<Android.Text.Style.CharacterStyle>())
                span.UpdateDrawState(paint);
            Equal(true, paint.Typeface!.IsBold);
            Equal(true, paint.Typeface.IsItalic);
            Equal(true, paint.UnderlineText);
            Equal(Android.Graphics.Color.Red, paint.Color);
            var expectedSize = Android.Util.TypedValue.ApplyDimension(Android.Util.ComplexUnitType.Sp, 18, native.Resources!.DisplayMetrics);
            Equal(true, Math.Abs(paint.TextSize - expectedSize) < 0.01);
            var alignment = native.EditableText.GetSpans(0, 1, Java.Lang.Class.FromType(typeof(Android.Text.Style.AlignmentSpanStandard)))!
                .OfType<Android.Text.Style.AlignmentSpanStandard>().Single();
            Equal(Android.Text.Layout.Alignment.AlignCenter, alignment.Alignment);
#else
            var native = (UIKit.UITextView)editor.Handler!.PlatformView!;
            var attributes = new UIKit.UIStringAttributes(native.AttributedText!.GetAttributes(0, out _));
            Equal(true, attributes.Font!.FontDescriptor.SymbolicTraits.HasFlag(UIKit.UIFontDescriptorSymbolicTraits.Bold), $"font={attributes.Font}; model={editor.Document.CurrentSnapshot.Runs[0].Format}");
            Equal(true, attributes.Font.FontDescriptor.SymbolicTraits.HasFlag(UIKit.UIFontDescriptorSymbolicTraits.Italic));
            Equal(18d, (double)attributes.Font.PointSize);
            Equal(Foundation.NSUnderlineStyle.Single, attributes.UnderlineStyle);
            Equal(UIKit.UIColor.Red, attributes.ForegroundColor);
            Equal(UIKit.UIColor.Yellow, attributes.BackgroundColor);
            Equal(UIKit.UITextAlignment.Center, attributes.ParagraphStyle!.Alignment);
#endif
        });

        yield return new("document replacement detaches old notifications and history", async editor =>
        {
            var selection = editor.Selection;
            var old = editor.Document;
            editor.Selection.ReplaceText("old document");
            var replacement = RichTextDocument.FromRtf(@"{\rtf1\ansi\b new}");
            editor.Document = replacement;
            Equal(false, editor.CanUndo);
            old.Edit(edit => edit.InsertText(0, "orphan "));
            await Verify(editor);
            Equal("new", editor.Document.Text);
            Equal(true, ReferenceEquals(selection, editor.Selection));
            editor.SelectedRange = new RichTextRange(3, 0);
            NativeReplace(editor, "!");
            await Verify(editor);
            Equal("new!", replacement.Text);
            editor.Undo();
            await Verify(editor);
            Equal("new", replacement.Text);
        });

        yield return new("native correction replacements preserve rich ranges and undo history", async editor =>
        {
            editor.Selection.ReplaceText("teh ");
            editor.Selection.InsertField("DATE", "today");
            editor.Selection.InsertImage(Image());
            editor.Document.Edit(edit => edit.SetMetadata("author", "test"));
            editor.SelectedRange = new RichTextRange(0, 3);
            editor.Selection.ToggleBold();
            editor.ClearUndoHistory();
            var before = editor.Document.CurrentSnapshot;
            NativeReplace(editor, "the");
            await Verify(editor);
            Equal("the today\uFFFC", editor.Document.Text);
            Equal(new RichTextRange(4, 5), editor.Document.CurrentSnapshot.Fields.Single().Range);
            Equal(9, editor.Document.CurrentSnapshot.Images.Single().Position);
            editor.SelectedRange = new RichTextRange(0, 3);
            NativeReplace(editor, "their");
            await Verify(editor);
            Equal("their today\uFFFC", editor.Document.Text);
            Equal(new RichTextRange(6, 5), editor.Document.CurrentSnapshot.Fields.Single().Range);
            Equal(11, editor.Document.CurrentSnapshot.Images.Single().Position);
            var after = editor.Document.CurrentSnapshot;
            var count = 0;
            while (editor.CanUndo && count++ < 4) { editor.Undo(); await Verify(editor); }
            SameContent(before, editor.Document.CurrentSnapshot);
            count = 0;
            while (editor.CanRedo && count++ < 4) { editor.Redo(); await Verify(editor); }
            SameContent(after, editor.Document.CurrentSnapshot);
        });

        yield return new("native deletion in repeated text remaps the actual edited range", async editor =>
        {
            editor.Selection.ReplaceText("aaa");
            editor.SelectedRange = new RichTextRange(1, 0);
            editor.Selection.InsertField("DATE", "a");
            var before = editor.Document.CurrentSnapshot;
            editor.ClearUndoHistory();
            editor.SelectedRange = new RichTextRange(0, 1);
            NativeReplace(editor, "");
            await Verify(editor);
            Equal("aaa", editor.Document.Text);
            Equal(new RichTextRange(0, 1), editor.Document.CurrentSnapshot.Fields.Single().Range);
            editor.Undo();
            await Verify(editor);
            SameContent(before, editor.Document.CurrentSnapshot);
        });

        yield return new("native formatting changes are reflected in content and undo history", async editor =>
        {
            editor.Selection.ReplaceText("styled");
            editor.SelectAll();
            editor.ClearUndoHistory();
#if WINDOWS
            var native = (Microsoft.UI.Xaml.Controls.RichEditBox)editor.Handler!.PlatformView!;
            native.Document.Selection.CharacterFormat.Bold = Microsoft.UI.Text.FormatEffect.On;
#elif ANDROID
            var native = (Android.Widget.EditText)editor.Handler!.PlatformView!;
            using var bold = new Android.Text.Style.StyleSpan(Android.Graphics.TypefaceStyle.Bold);
            native.EditableText!.SetSpan(bold, 0, 6, Android.Text.SpanTypes.ExclusiveExclusive);
#else
            var native = (UIKit.UITextView)editor.Handler!.PlatformView!;
            native.TextStorage.AddAttribute(UIKit.UIStringAttributeKey.Font, UIKit.UIFont.BoldSystemFontOfSize(18)!, new Foundation.NSRange(0, 6));
#endif
            await Verify(editor);
            Equal(true, editor.Document.CurrentSnapshot.Runs[0].Format.Bold);
            Equal(true, editor.CanUndo);
            editor.Undo();
            await Verify(editor);
            Equal(false, editor.Document.CurrentSnapshot.Runs[0].Format.Bold);
            editor.Redo();
            await Verify(editor);
            Equal(true, editor.Document.CurrentSnapshot.Runs[0].Format.Bold);
#if WINDOWS
            native.Document.Selection.CharacterFormat.Bold = Microsoft.UI.Text.FormatEffect.Off;
#elif ANDROID
            foreach (var span in native.EditableText!.GetSpans(0, 6, Java.Lang.Class.FromType(typeof(Android.Text.Style.StyleSpan)))!)
                native.EditableText.RemoveSpan(span);
#else
            native.TextStorage.AddAttribute(UIKit.UIStringAttributeKey.Font, UIKit.UIFont.SystemFontOfSize(18)!, new Foundation.NSRange(0, 6));
#endif
            await Verify(editor);
            Equal(false, editor.Document.CurrentSnapshot.Runs[0].Format.Bold);
            editor.Undo();
            await Verify(editor);
            Equal(true, editor.Document.CurrentSnapshot.Runs[0].Format.Bold);
        });

        yield return new("atomic changes, rollback and event origins", async editor =>
        {
            var content = new List<RichTextChangeSet>();
            var texts = 0;
            void Changed(object? sender, RichTextContentChangedEventArgs args) => content.Add(args.ChangeSet);
            void TextChanged(object? sender, RichTextTextChangedEventArgs args) => texts++;
            editor.ContentChanged += Changed;
            editor.TextChanged += TextChanged;
            try
            {
                editor.Document.Edit(edit =>
                {
                    edit.InsertText(0, "hello");
                    edit.UpdateCharacterFormat(new RichTextRange(0, 5), format => format with { Italic = true });
                    edit.SetMetadata("author", "test");
                });
                Equal(1, content.Count);
                Equal(1, texts);
                Equal(RichTextChangeOrigin.Programmatic, content[^1].Origin);
                editor.SelectAll();
                editor.Selection.ToggleBold();
                Equal(2, content.Count);
                Equal(1, texts);
                var before = editor.Document.CurrentSnapshot;
                try
                {
                    editor.Document.Edit(edit => { edit.InsertText(0, "rollback"); throw new ApplicationException("rollback"); });
                }
                catch (ApplicationException) { }
                Equal(true, ReferenceEquals(before, editor.Document.CurrentSnapshot));
                Equal(2, content.Count);
                editor.SelectedRange = new RichTextRange(5, 0);
                NativeReplace(editor, "!");
                await Verify(editor);
                Equal(RichTextChangeOrigin.User, content[^1].Origin);
                Equal(2, texts);
                editor.Undo();
                Equal(RichTextChangeOrigin.Undo, content[^1].Origin);
                editor.Redo();
                Equal(RichTextChangeOrigin.Redo, content[^1].Origin);
            }
            finally
            {
                editor.ContentChanged -= Changed;
                editor.TextChanged -= TextChanged;
            }
        });

        yield return new("read-only blocks selection operations and commands but permits document edits", async editor =>
        {
            using var formatting = new RichEditorFormattingCommands(editor);
            editor.Selection.ReplaceText("fixed");
            editor.SelectAll();
            var before = editor.Document.CurrentSnapshot;
            editor.IsReadOnly = true;
            Equal(false, editor.Commands.Cut.CanExecute(null));
            Equal(false, formatting.ToggleBold.CanExecute(null));
            Equal(false, formatting.InsertField.CanExecute(new FieldRequest("DATE", "today")));
            Equal(true, editor.Commands.Copy.CanExecute(null));
            editor.Selection.ReplaceText("no");
            editor.Selection.ToggleBold();
            editor.Selection.InsertField("DATE", "no");
            editor.Selection.InsertImage(Image());
            editor.Selection.SetLink("https://example.com");
            editor.Selection.SetList(List(RichTextListNumberStyle.Arabic));
            await editor.CutAsync();
            await editor.PasteAsync();
            editor.Undo();
            Equal(true, ReferenceEquals(before, editor.Document.CurrentSnapshot));
            editor.Document.Edit(edit => edit.InsertText(5, "!"));
            Equal("fixed!", editor.Document.Text);
            await Verify(editor);
        });

        yield return new("command state, parameters, notifications and redo invalidation", async editor =>
        {
            using var formatting = new RichEditorFormattingCommands(editor);
            var notifications = 0;
            void StateChanged(object? sender, EventArgs args) => notifications++;
            editor.Commands.Undo.CanExecuteChanged += StateChanged;
            try
            {
                Equal(false, editor.Commands.Copy.CanExecute(null));
                Equal(false, formatting.InsertField.CanExecute("invalid"));
                Equal(false, formatting.ToggleUnderline.CanExecute(RichTextUnderlineStyle.None));
                formatting.InsertField.Execute(new FieldRequest("DATE", "today"));
                Equal(true, editor.CanUndo);
                Equal(true, notifications > 0);
                editor.Commands.SelectAll.Execute(null);
                formatting.SetLink.Execute(new LinkRequest("https://example.com", "tip"));
                Equal("tip", editor.Document.CurrentSnapshot.Links.Single().ToolTip);
                editor.Commands.Undo.Execute(null);
                Equal(0, editor.Document.CurrentSnapshot.Links.Length);
                Equal(true, editor.Commands.Redo.CanExecute(null));
                editor.Selection.ReplaceText("branch");
                Equal(false, editor.CanRedo);
                editor.ClearUndoHistory();
                Equal(false, editor.Commands.Undo.CanExecute(null));
                await Verify(editor);
            }
            finally { editor.Commands.Undo.CanExecuteChanged -= StateChanged; }
        });

        yield return new("rich clipboard round trip includes lists, links, fields and image bytes", async editor =>
        {
            editor.Selection.ReplaceText("link\n");
            editor.SelectedRange = new RichTextRange(0, 4);
            editor.Selection.SetLink("https://example.com/a?q=1", "tooltip");
            editor.SelectedRange = new RichTextRange(5, 0);
            editor.Selection.InsertField("DATE", "today");
            editor.Selection.InsertImage(Image() with { AlternativeText = "pixel", Source = "owned", Rotation = 30 });
            editor.SelectAll();
            editor.Selection.SetList(List(RichTextListNumberStyle.UpperRoman));
            editor.Selection.UpdateCharacterFormat(format => format with { FontSize = 17.25, StyleName = "custom", Ligatures = RichTextFeatureMode.Disabled });
            var before = editor.Document.CurrentSnapshot;
            await editor.CopyAsync();
            editor.Document = new RichTextDocument();
            await editor.PasteAsync();
            await Verify(editor);
            Equal(before.Text, editor.Document.Text);
            Equal(before.Runs[0].Format, editor.Document.CurrentSnapshot.Runs[0].Format);
            Equal(before.Links.Single().Target, editor.Document.CurrentSnapshot.Links.Single().Target);
            Equal(before.Fields.Single().Instruction, editor.Document.CurrentSnapshot.Fields.Single().Instruction);
            Equal(before.Images.Single(), editor.Document.CurrentSnapshot.Images.Single());
            Equal(2, editor.Document.CurrentSnapshot.Paragraphs.Count(paragraph => paragraph.Format.List is not null));
            var pasted = editor.Document.CurrentSnapshot;
            editor.Undo();
            Equal("", editor.Document.Text);
            Equal(false, editor.CanUndo);
            editor.Redo();
            SameContent(pasted, editor.Document.CurrentSnapshot);
            await Verify(editor);
        });

        yield return new("external clipboard replacement invalidates rich cache even with identical text", async editor =>
        {
            editor.Selection.InsertField("DATE", "same");
            editor.SelectAll();
            editor.Selection.ToggleBold();
            await editor.CopyAsync();
            await SetPlainClipboard("same");
            editor.Document = new RichTextDocument();
            await editor.PasteAsync();
            Equal("same", editor.Document.Text);
            Equal(0, editor.Document.CurrentSnapshot.Fields.Length);
            Equal(false, editor.Document.CurrentSnapshot.Runs[0].Format.Bold);
            await Verify(editor);
        });

        yield return new("paste handlers replace fragments and respect cancellation", async editor =>
        {
            await SetPlainClipboard("original");
            void Replace(object? sender, RichTextPastingEventArgs args) => args.Fragment = RichTextDocumentFragment.FromRtf(@"{\rtf1\ansi\i replaced}");
            editor.Pasting += Replace;
            try { await editor.PasteAsync(); }
            finally { editor.Pasting -= Replace; }
            Equal("replaced", editor.Document.Text);
            Equal(true, editor.Document.CurrentSnapshot.Runs[0].Format.Italic);
            var snapshot = editor.Document.CurrentSnapshot;
            void Cancel(object? sender, RichTextPastingEventArgs args) => args.Cancel = true;
            editor.Pasting += Cancel;
            try { await editor.PasteAsync(); }
            finally { editor.Pasting -= Cancel; }
            Equal(true, ReferenceEquals(snapshot, editor.Document.CurrentSnapshot));
            await Verify(editor);
        });

        foreach (var mutation in new[] { "selection", "document", "content", "read-only" })
        {
            yield return new($"paste abandons stale destination after {mutation} changes", async editor =>
            {
                editor.Selection.ReplaceText("safe");
                await SetPlainClipboard("paste");
                void Change(object? sender, RichTextPastingEventArgs args)
                {
                    switch (mutation)
                    {
                        case "selection": editor.SelectedRange = new RichTextRange(0, 0); break;
                        case "document": editor.Document = RichTextDocument.FromRtf(@"{\rtf1 replacement}"); break;
                        case "content": editor.Document.Edit(edit => edit.InsertText(0, "changed ")); break;
                        case "read-only": editor.IsReadOnly = true; break;
                    }
                }
                editor.Pasting += Change;
                try { await editor.PasteAsync(); }
                finally { editor.Pasting -= Change; }
                Equal(mutation == "document" ? "replacement" : mutation == "content" ? "changed safe" : "safe", editor.Document.Text);
                await Verify(editor);
            });
        }

        foreach (var (maximum, input, expected) in new[]
        {
            (0, "x", ""), (1, "😀", ""), (2, "😀x", "😀"), (3, "a😀z", "a😀"), (3, "abcd", "abc"), (-1, "abc😀", "abc😀"),
        })
        {
            yield return new($"paste length {maximum} with {input}", async editor =>
            {
                editor.MaxLength = maximum;
                editor.Selection.ToggleBold();
                await SetPlainClipboard(input);
                await editor.PasteAsync();
                Equal(expected, editor.Document.Text);
                Equal(new RichTextRange(expected.Length, 0), editor.SelectedRange);
                if (expected.Length > 0) Equal(true, editor.Document.CurrentSnapshot.Runs[0].Format.Bold);
                await Verify(editor);
            });
        }

        yield return new("paste limit credits replaced selection and clips rich semantics", async editor =>
        {
            editor.Selection.InsertField("DATE", "abc😀z");
            editor.SelectAll();
            editor.Selection.ToggleBold();
            await editor.CopyAsync();
            editor.Document = new RichTextDocument();
            editor.Selection.ReplaceText("12345");
            editor.SelectedRange = new RichTextRange(1, 3);
            editor.MaxLength = 5;
            await editor.PasteAsync();
            Equal("1abc5", editor.Document.Text);
            Equal(new RichTextRange(4, 0), editor.SelectedRange);
            Equal(true, editor.Document.GetCharacterFormat(new RichTextRange(1, 3)).RepresentativeFormat.Bold);
            var field = editor.Document.CurrentSnapshot.Fields.Single();
            Equal(new RichTextRange(1, 3), field.Range);
            await Verify(editor);
        });

        yield return new("links and fields remap through native edits and semantic-only changes", async editor =>
        {
            editor.Selection.ReplaceText("lead link ");
            editor.SelectedRange = new RichTextRange(5, 4);
            editor.Selection.SetLink("https://example.com", "tip");
            editor.SelectedRange = new RichTextRange(10, 0);
            editor.Selection.InsertField("DATE", "one\ntwo");
            var original = editor.Document.CurrentSnapshot;
            editor.SelectedRange = RichTextRange.Empty;
            NativeReplace(editor, "prefix ");
            await Verify(editor);
            Equal("prefix lead link one\ntwo", editor.Document.Text);
            Equal(new RichTextRange(12, 4), editor.Document.CurrentSnapshot.Links.Single().Range);
            var field = editor.Document.CurrentSnapshot.Fields.Single();
            Equal(new RichTextRange(17, 7), field.Range);
            editor.Document.Edit(edit => edit.UpdateField(field.Id, "TIME", "one\ntwo"));
            Equal("TIME", editor.Document.CurrentSnapshot.Fields.Single().Instruction);
            editor.Undo();
            Equal("DATE", editor.Document.CurrentSnapshot.Fields.Single().Instruction);
            editor.Undo();
            SameContent(original, editor.Document.CurrentSnapshot);
            await Verify(editor);
        });

        foreach (var valid in new[] { true, false })
        {
            yield return new($"image update, native deletion and history ({(valid ? "PNG" : "invalid bytes")})", async editor =>
            {
                editor.Selection.ReplaceText("ab");
                editor.SelectedRange = new RichTextRange(1, 0);
                editor.Selection.InsertImage(valid ? Image() : RichTextImage.FromBytes(0, "image/png", [1, 2, 3], 12, 12));
                var image = editor.Document.CurrentSnapshot.Images.Single();
                editor.Document.Edit(edit => edit.UpdateImage(1, image with { Width = 24, AlternativeText = "updated" }));
                var before = editor.Document.CurrentSnapshot;
                editor.ClearUndoHistory();
                editor.SelectedRange = new RichTextRange(1, 1);
                NativeReplace(editor, "");
                await Verify(editor);
                Equal("ab", editor.Document.Text);
                Equal(0, editor.Document.CurrentSnapshot.Images.Length);
                editor.Undo();
                SameContent(before, editor.Document.CurrentSnapshot);
                await Verify(editor);
                editor.Redo();
                Equal("ab", editor.Document.Text);
                await Verify(editor);
            });
        }

        foreach (var style in Enum.GetValues<RichTextListNumberStyle>())
        {
            yield return new($"list {style}, nesting, restart, native split and removal", async editor =>
            {
                editor.Selection.ReplaceText("one\ntwo\nthree");
                editor.SelectAll();
                editor.Selection.SetList(List(style));
                editor.SelectedRange = new RichTextRange(4, 3);
                editor.Selection.ChangeListLevel(1);
                editor.Selection.RestartList(7);
                var middle = editor.Document.CurrentSnapshot.Paragraphs[1].Format.List!;
                Equal(1, middle.Level);
                Equal(7, middle.RestartAt);
                editor.SelectedRange = new RichTextRange(7, 0);
                NativeReplace(editor, "\nnew");
                await Verify(editor);
                Equal(4, editor.Document.CurrentSnapshot.Paragraphs.Length);
                Equal(null, editor.Document.CurrentSnapshot.Paragraphs[2].Format.List!.RestartAt);
                Equal(1, editor.Document.CurrentSnapshot.Paragraphs[2].Format.List!.Level);
                editor.SelectAll();
                var before = editor.Document.CurrentSnapshot;
                editor.Selection.ClearList();
                Equal(true, editor.Document.CurrentSnapshot.Paragraphs.All(paragraph => paragraph.Format.List is null));
                editor.Undo();
                SameContent(before, editor.Document.CurrentSnapshot);
                await Verify(editor);
            });
        }

        foreach (var (name, format) in CharacterFormats())
        {
            yield return new($"character {name} survives unrelated native input and history", async editor =>
            {
                editor.Selection.ReplaceText("styled\nplain");
                editor.Document.Edit(edit => edit.SetCharacterFormat(new RichTextRange(0, 6), format));
                var expected = editor.Document.GetCharacterFormat(new RichTextRange(0, 6)).RepresentativeFormat;
                editor.SelectedRange = new RichTextRange(editor.Document.Length, 0);
                editor.ClearUndoHistory();
                NativeReplace(editor, "!");
                await Verify(editor);
                Equal(expected, editor.Document.GetCharacterFormat(new RichTextRange(0, 6)).RepresentativeFormat);
                var after = editor.Document.CurrentSnapshot;
                editor.Undo();
                await Verify(editor);
                Equal(expected, editor.Document.GetCharacterFormat(new RichTextRange(0, 6)).RepresentativeFormat);
                editor.Redo();
                SameContent(after, editor.Document.CurrentSnapshot);
                await Verify(editor);
            });
        }

        foreach (var (name, format) in ParagraphFormats())
        {
            yield return new($"paragraph {name} survives unrelated native input and history", async editor =>
            {
                editor.Selection.ReplaceText("one\ntwo");
                editor.Document.Edit(edit => edit.SetParagraphFormat(new RichTextRange(0, 3), format));
                var expected = editor.Document.CurrentSnapshot.Paragraphs[0].Format;
                editor.SelectedRange = new RichTextRange(editor.Document.Length, 0);
                editor.ClearUndoHistory();
                NativeReplace(editor, "\nnew");
                await Verify(editor);
                Equal(expected, editor.Document.CurrentSnapshot.Paragraphs[0].Format);
                editor.Undo();
                await Verify(editor);
                Equal(expected, editor.Document.CurrentSnapshot.Paragraphs[0].Format);
            });
        }

        foreach (var seed in new[] { 8149, 20260906, 42 })
        {
            yield return new($"mixed native and programmatic transaction replay seed {seed}", async editor =>
            {
                var random = new Random(seed);
                string[] insertions = ["a", "bc", "日", "\n", "\n\n", "\t", "", "xyz"];
                var expected = "";
                for (var step = 0; step < 100; step++)
                {
                    var start = random.Next(expected.Length + 1);
                    var length = random.Next(expected.Length - start + 1);
                    var text = insertions[random.Next(insertions.Length)];
                    editor.SelectedRange = new RichTextRange(start, length);
                    if (step % 3 == 0)
                    {
                        NativeReplace(editor, text);
                        await Verify(editor);
                        // Native services may apply smart spacing or corrections.
                        // Their actual result must be reflected in the document.
                        expected = NativeText(editor);
                    }
                    else
                    {
                        editor.Selection.ReplaceText(text);
                        expected = expected[..start] + text + expected[(start + length)..];
                    }
                    Equal(expected, editor.Document.Text, $"seed {seed}, step {step}");
                    if (step % 7 == 0 && expected.Length > 0)
                    {
                        editor.Document.Edit(edit => edit.UpdateCharacterFormat(new RichTextRange(0, expected.Length), format => format with { Italic = !format.Italic }));
                    }
                    await Verify(editor);
                    expected = NativeText(editor);
                }
                var final = editor.Document.CurrentSnapshot;
                var count = 0;
                while (editor.CanUndo && count++ < 250) { editor.Undo(); await Verify(editor); }
                Equal("", editor.Document.Text);
                Equal(false, editor.CanUndo);
                count = 0;
                while (editor.CanRedo && count++ < 250) { editor.Redo(); await Verify(editor); }
                SameContent(final, editor.Document.CurrentSnapshot);
                Equal(false, editor.CanRedo);
            });
        }

        yield return new("large document, distant edits and trailing empty paragraphs", async editor =>
        {
            var text = string.Concat(Enumerable.Repeat("Paragraph 日本語 😀\ttext\u2028soft\n", 400)) + "\n";
            editor.Selection.ReplaceText(text);
            await Verify(editor);
            Equal(text, editor.Document.Text);
            editor.SelectedRange = new RichTextRange(0, 9);
            NativeReplace(editor, "Start");
            editor.SelectedRange = new RichTextRange(editor.Document.Length, 0);
            editor.Selection.ToggleBold();
            NativeReplace(editor, "end");
            await Verify(editor);
            Equal("Start" + text[9..] + "end", editor.Document.Text);
            Equal(true, editor.Document.GetCharacterFormat(new RichTextRange(editor.Document.Length - 3, 3)).RepresentativeFormat.Bold);
        });
    }

    internal static IEnumerable<(string, RichTextCharacterFormat)> CharacterFormats()
    {
        var f = RichTextCharacterFormat.Default;
        yield return ("font family", f with { FontFamily = "Arial" });
        yield return ("font size", f with { FontSize = 18 });
        yield return ("bold", f with { FontWeight = 700 });
        yield return ("italic", f with { Italic = true });
        foreach (var style in Enum.GetValues<RichTextUnderlineStyle>()) yield return ($"underline {style}", f with { Underline = style });
        yield return ("underline color", f with { Underline = RichTextUnderlineStyle.Single, UnderlineColor = Colors.Red });
        yield return ("single strike", f with { Strikethrough = RichTextStrikethroughStyle.Single });
        yield return ("double colored strike", f with { Strikethrough = RichTextStrikethroughStyle.Double, StrikethroughColor = Colors.Blue });
        yield return ("foreground", f with { ForegroundColor = Colors.Red });
        yield return ("highlight", f with { BackgroundColor = Colors.Yellow });
        yield return ("superscript", f with { Script = RichTextScript.Superscript });
        yield return ("subscript", f with { Script = RichTextScript.Subscript });
        yield return ("baseline", f with { BaselineOffset = 2 });
        yield return ("tracking", f with { CharacterSpacing = 2 });
        yield return ("scale", f with { HorizontalScale = 1.25 });
        yield return ("small caps", f with { SmallCaps = true });
        yield return ("all caps", f with { AllCaps = true });
        yield return ("outline", f with { Outline = true });
        yield return ("shadow", f with { Shadow = true });
        yield return ("hidden", f with { Hidden = true });
        yield return ("language", f with { LanguageTag = "ja-JP" });
        yield return ("direction", f with { Direction = RichTextDirection.RightToLeft });
        yield return ("kerning", f with { Kerning = RichTextFeatureMode.Enabled });
        yield return ("ligatures", f with { Ligatures = RichTextFeatureMode.Disabled });
        yield return ("shading", f with { Shading = 2500, ShadingForegroundColor = Colors.Red, ShadingBackgroundColor = Colors.Yellow });
        yield return ("style name", f with { StyleName = "Custom" });
    }

    private static IEnumerable<(string, RichTextParagraphFormat)> ParagraphFormats()
    {
        var f = RichTextParagraphFormat.Default;
        foreach (var alignment in Enum.GetValues<RichTextAlignment>()) yield return ($"alignment {alignment}", f with { Alignment = alignment });
        foreach (var rule in Enum.GetValues<RichTextLineSpacingRule>())
            yield return ($"line spacing {rule}", f with { LineSpacingRule = rule, LineSpacing = rule == RichTextLineSpacingRule.Multiple ? 1.5 : rule is RichTextLineSpacingRule.AtLeast or RichTextLineSpacingRule.Exactly ? 24 : 0 });
        yield return ("RTL", f with { Direction = RichTextDirection.RightToLeft });
        yield return ("indents", f with { LeadingIndent = 24, FirstLineIndent = -12, TrailingIndent = 12 });
        yield return ("spacing", f with { SpaceBefore = 6, SpaceAfter = 8 });
        yield return ("tabs", f with { TabStops = [new(24), new(48, RichTextTabAlignment.Right, RichTextTabLeader.Dots)] });
        yield return ("hyphenation", f with { Hyphenation = true });
        yield return ("background", f with { BackgroundColor = Colors.Yellow });
        yield return ("minimum and maximum height", f with { MinimumLineHeight = 18, MaximumLineHeight = 30 });
        yield return ("shading", f with { Shading = 2500, ShadingForegroundColor = Colors.Red, ShadingBackgroundColor = Colors.Yellow });
        yield return ("borders", f with { Border = new RichTextBorder(RichTextBorderSides.Top | RichTextBorderSides.Bottom, RichTextBorderStyle.Dashed, 1.5, Colors.Blue) });
        yield return ("style name", f with { StyleName = "Custom" });
    }

    private static RichTextImage Image() => RichTextImage.FromBytes(0, "image/png",
        Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="), 12, 12);

    private static RichTextListDefinition List(RichTextListNumberStyle style) => new((RichTextListLevelDefinition[])[
        new RichTextListLevelDefinition { Marker = new RichTextListMarker.Number(style, 3), Prefix = "(", Suffix = ")", LeadingIndent = 24, FirstLineIndent = -12, MarkerTab = 24 },
        new RichTextListLevelDefinition { Marker = new RichTextListMarker.Number(style, 1), Prefix = "", Suffix = ".", LeadingIndent = 48, FirstLineIndent = -12, MarkerTab = 48 },
    ]);

    public static async Task Verify(RichEditor editor)
    {
        // Native text services may finish their edit after the initial callback.
        var native = "";
        for (var attempt = 0; attempt < 50; attempt++)
        {
            await Drain();
            native = NativeText(editor);
            if (native == editor.Document.Text) break;
        }
        Equal(native, editor.Document.Text, "native projection");
        Equal(true, editor.SelectedRange.Start >= 0 && editor.SelectedRange.End <= editor.Document.Length, "selection bounds");
    }

    public static Task Drain() => Task.Delay(20);

#if WINDOWS
    [DynamicWindowsRuntimeCast(typeof(RichEditBox))]
#endif
    public static string NativeText(RichEditor editor)
    {
#if WINDOWS
        var native = (Microsoft.UI.Xaml.Controls.RichEditBox)editor.Handler!.PlatformView!;
        if (editor.Document.CurrentSnapshot.Links.Length == 0)
        {
            native.Document.GetText(Microsoft.UI.Text.TextGetOptions.None, out var plain);
            return (plain.EndsWith('\r') ? plain[..^1] : plain).Replace('\r', '\n').Replace('\v', '\u2028');
        }
        // The story includes hidden HYPERLINK instructions. Read the native RTF
        // result text so those codes are excluded without hiding authored text.
        native.Document.GetText(Microsoft.UI.Text.TextGetOptions.FormatRtf, out var rtf);
        var text = RichTextDocument.FromRtf(rtf.TrimEnd('\0')).Text;
        return text.EndsWith('\n') ? text[..^1] : text;
#elif ANDROID
        return ((Android.Widget.EditText)editor.Handler!.PlatformView!).Text ?? "";
#else
        return ((UIKit.UITextView)editor.Handler!.PlatformView!).Text ?? "";
#endif
    }

#if WINDOWS
    [DynamicWindowsRuntimeCast(typeof(RichEditBox))]
#endif
    public static void NativeSelect(RichEditor editor, RichTextRange range, bool backwards = false)
    {
#if WINDOWS
        var native = (Microsoft.UI.Xaml.Controls.RichEditBox)editor.Handler!.PlatformView!;
        native.Document.Selection.SetRange(backwards ? range.End : range.Start, backwards ? range.Start : range.End);
#elif ANDROID
        var native = (Android.Widget.EditText)editor.Handler!.PlatformView!;
        native.SetSelection(backwards ? range.End : range.Start, backwards ? range.Start : range.End);
#else
        ((UIKit.UITextView)editor.Handler!.PlatformView!).SelectedRange = new Foundation.NSRange(range.Start, range.Length);
#endif
    }

#if WINDOWS
    [DynamicWindowsRuntimeCast(typeof(RichEditBox))]
#endif
    public static void NativeReplace(RichEditor editor, string text)
    {
        if (text.Length == 0 && editor.SelectedRange.IsEmpty) return;
#if WINDOWS
        ((Microsoft.UI.Xaml.Controls.RichEditBox)editor.Handler!.PlatformView!).Document.Selection.SetText(Microsoft.UI.Text.TextSetOptions.None, text);
#elif ANDROID
        var native = (Android.Widget.EditText)editor.Handler!.PlatformView!;
        var start = Math.Min(native.SelectionStart, native.SelectionEnd);
        var end = Math.Max(native.SelectionStart, native.SelectionEnd);
        using var replacement = new Java.Lang.String(text);
        native.EditableText!.Replace(start, end, replacement);
        native.SetSelection(Math.Min(start + text.Length, native.Length()));
#else
        var native = (UIKit.UITextView)editor.Handler!.PlatformView!;
        if (text.Length == 0) native.DeleteBackward();
        else native.InsertText(text);
#endif
    }

#if WINDOWS
    [DynamicWindowsRuntimeCast(typeof(RichEditBox))]
    [DynamicWindowsRuntimeCast(typeof(Panel))]
#endif
    private static void SetNativeFocus(RichEditor editor, bool focused)
    {
#if WINDOWS
        var native = (Microsoft.UI.Xaml.Controls.RichEditBox)editor.Handler!.PlatformView!;
        if (focused) native.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
        else ((Microsoft.UI.Xaml.Controls.Panel)native.Parent).Children.OfType<Microsoft.UI.Xaml.Controls.Button>().Single().Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
#elif ANDROID
        var native = (Android.Widget.EditText)editor.Handler!.PlatformView!;
        if (focused) native.RequestFocus();
        else
        {
            var parent = (Android.Views.View)native.Parent!;
            parent.FocusableInTouchMode = true;
            parent.RequestFocus();
        }
#else
        var native = (UIKit.UITextView)editor.Handler!.PlatformView!;
        if (focused) native.BecomeFirstResponder();
        else native.ResignFirstResponder();
#endif
    }

    private static Task SetPlainClipboard(string text) => Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.Default.SetTextAsync(text);

    public static void SameContent(RichTextDocumentSnapshot expected, RichTextDocumentSnapshot actual)
    {
        Equal(expected.Text, actual.Text);
        Equal(expected.RtfText, actual.RtfText);
        Equal(true, expected.Runs.SequenceEqual(actual.Runs), "runs");
        Equal(true, expected.Paragraphs.SequenceEqual(actual.Paragraphs), "paragraphs");
        Equal(true, expected.Fields.SequenceEqual(actual.Fields), "fields");
        Equal(true, expected.Links.SequenceEqual(actual.Links), "links");
        Equal(true, expected.Images.SequenceEqual(actual.Images), "images");
        Equal(true, expected.Metadata.OrderBy(pair => pair.Key).SequenceEqual(actual.Metadata.OrderBy(pair => pair.Key)), "metadata");
    }

    public static void Equal<T>(T expected, T actual, string? context = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{context}: Expected {expected}; actual {actual}.");
    }
}
