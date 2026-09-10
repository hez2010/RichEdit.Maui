#if DEBUG && (ANDROID || IOS || MACCATALYST)
using Microsoft.Maui.Platform;
using CodeEdit.Maui;
using RichEdit.Maui.TestApp;

namespace RichEdit.Maui.Tests;

// These tests use the full control tree and inspect the native text surface.
internal sealed partial class CodeEditorTestPage : ContentPage
{
    private readonly CodeEditor _editor = new() { Document = CodeDocument.FromPlainText(new string('x', 300) + "\nlast"), TextColor = Colors.Black, BackgroundColor = Colors.White };
    private readonly Entry _focusTarget = new() { Placeholder = "Focus target" };
    private readonly RichEditor _textView;
    private readonly CodeEditorActions _actions;

    internal CodeEditorTestPage(string? filter)
    {
        _actions = new CodeEditorActions(_editor);
        _textView = ((Grid)_editor.Content).Children.OfType<RichEditor>().Single();
        var layout = new Grid { RowDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
        layout.Add(_editor);
        layout.Add(_focusTarget, 0, 1);
        Content = layout;
        SafeAreaEdges = new(SafeAreaRegions.Container);
        var started = false;
        _textView.Loaded += async (_, _) =>
        {
            if (started) return;
            started = true;
            await RunAsync(filter);
        };
    }

    private Color TokenColor(string type) => _editor.Coloring().Palette!(new(new(0, 1), Enum.Parse<CodeTokenKind>(type == "macro" ? "Preprocessor" : type, true)))!;

    private async Task RunAsync(string? filter)
    {
        var results = new List<string>();
        var started = DateTimeOffset.UtcNow;
        var output = Path.Combine(FileSystem.CacheDirectory, "code-editor-tests.txt");
        void Save() => File.WriteAllText(output,
            $"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; {DeviceInfo.Platform} {DeviceInfo.VersionString}\n" +
            $"Started {started:O}; library {typeof(RichEditor).Assembly.ManifestModule.ModuleVersionId}; code {typeof(CodeEditor).Assembly.ManifestModule.ModuleVersionId}\n" +
            string.Join("\n", results) + "\n");
        async Task Test(string name, string source, Func<Task> action, bool replaceDocument = true)
        {
            if (!string.IsNullOrEmpty(filter) && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) return;
            var index = results.Count;
            results.Add($"RUN {name}");
            Save();
            try
            {
                _editor.IsReadOnly = false;
                _editor.IsEnabled = true;
                _editor.MaxLength = -1;
                _editor.UseTabs = false;
                _editor.AutoIndent = true;
                _editor.WordWrap = false;
                _editor.ShowLineNumbers = true;
                _editor.Coloring().Palette = static token => token.Kind switch
                {
                    CodeTokenKind.Keyword => Colors.Blue,
                    CodeTokenKind.String => Colors.Maroon,
                    CodeTokenKind.Comment => Colors.Green,
                    CodeTokenKind.Number => Colors.DarkCyan,
                    CodeTokenKind.Preprocessor => Colors.Purple,
                    _ => null,
                };
                _editor.Coloring().Enabled = true;
                if (replaceDocument) _editor.Document = CodeDocument.FromPlainText(source);
                _editor.SelectedRange = RichTextRange.Empty;
                _editor.Focus();
                await Task.Delay(60);
                await _editor.Coloring().RefreshAsync();
                await action();
                await EditorContractTests.Verify(_textView);
                results[index] = $"PASS {name}";
            }
            catch (Exception exception) { results[index] = $"FAIL {name}: {exception}"; }
            Save();
            Console.WriteLine(results[index]);
        }

        await Test("initial layout respects horizontal scrolling", "", () =>
        {
            Equal(2, NativeLineCount(), "initial visual lines");
            return Task.CompletedTask;
        }, replaceDocument: false);

        await Test("presentation revisions do not change source or saved state", "class C { }", async () =>
        {
            var before = _editor.Document.CurrentSnapshot;
            var rtf = _textView.Document.RtfText;
            _editor.Document.MarkSaved();
            _editor.SelectionState = new RichTextSelectionState(8, 1);
            _editor.Coloring().Palette = static _ => Colors.Purple;
            await _editor.Coloring().RefreshAsync();
            Equal(before.Version, _editor.Document.Version);
            Equal(rtf, _textView.Document.RtfText);
            Equal(false, _editor.Document.IsModified);
            Equal(new RichTextSelectionState(8, 1), _editor.SelectionState);
            Equal(new CodePosition(1, 2), _editor.CaretPosition);
            ColorAt(0, TokenColor("keyword"));
            using var layer = _editor.Decorations.CreateLayer();
            layer.TrySet(_editor.Document.Revision, [new(new RichTextRange(0, 5), new RichTextDecorationStyle { ForegroundColor = Colors.Green })]);
            ColorAt(0, Colors.Green);
            Equal(before.Version, _editor.Document.Version);
            layer.Clear();
            ColorAt(0, TokenColor("keyword"));
        });

        await Test("source document undo restores direction and save points", "123456", async () =>
        {
            _editor.Document.MarkSaved();
            _editor.SelectionState = new RichTextSelectionState(4, 1);
            _editor.Selection.ReplaceText("9");
            _editor.SelectionState = new RichTextSelectionState(0, 0);
            _editor.Document.Undo();
            await _editor.Coloring().RefreshAsync();
            Equal("123456", _editor.Document.Text);
            Equal(new RichTextSelectionState(4, 1), _editor.SelectionState);
            Equal(false, _editor.Document.IsModified);
            Equal(1, _editor.GetOffset(_editor.CaretPosition));
            _editor.Document.Redo();
            Equal("1956", _editor.Document.Text);
            Equal(new RichTextSelectionState(2, 2), _editor.SelectionState);
        });

        await Test("syntax colors reach native text", "class C { string s = \"hello\"; int n = 42; // comment\n}", () =>
        {
            ColorAt(0, TokenColor("keyword"));
            ColorAt(_editor.Document.Text.IndexOf('"'), TokenColor("string"));
            ColorAt(_editor.Document.Text.IndexOf("42", StringComparison.Ordinal), TokenColor("number"));
            ColorAt(_editor.Document.Text.IndexOf("//", StringComparison.Ordinal), TokenColor("comment"));
            ColorAt(6, _editor.TextColor!);
            Equal(false, _editor.CanUndo, "initial highlighting history");
            return Task.CompletedTask;
        });

        await Test("focus and unfocus retain colors and history", "class C { string s = \"hello\"; }", async () =>
        {
            var version = _editor.Document.Version;
            for (var i = 0; i < 3; i++)
            {
                Equal(true, _focusTarget.Focus(), "move focus away");
                await Task.Delay(60);
                ColorAt(0, TokenColor("keyword"));
                Equal(true, _editor.Focus(), "focus editor");
                await Task.Delay(60);
                ColorAt(0, TokenColor("keyword"));
            }
            Equal(version, _editor.Document.Version, "focus document version");
            Equal(false, _editor.CanUndo, "focus history");
        });

        await Test("theme changes preserve selection and redo", "class C { }", async () =>
        {
            _editor.SelectedRange = new RichTextRange(6, 1);
            _editor.Selection.ReplaceText("D");
            _editor.Undo();
            var selection = _editor.SelectedRange;
            var document = _editor.Document;
            _editor.Coloring().Palette = static _ => Colors.Purple;
            await _editor.Coloring().RefreshAsync();
            Equal(true, ReferenceEquals(document, _editor.Document), "document identity");
            Equal(selection, _editor.SelectedRange, "selection after theme");
            Equal(false, _editor.CanUndo, "theme undo");
            Equal(true, _editor.CanRedo, "theme redo");
            ColorAt(0, TokenColor("keyword"));
            ColorAt(6, _editor.TextColor!);
            _editor.Redo();
            Equal("class D { }", _editor.Document.Text);
        });

        await Test("native typing is highlighted and remains undoable", "class C { }", async () =>
        {
            EditorContractTests.NativeReplace(_textView, "public ");
            await _editor.Coloring().RefreshAsync();
            Equal("public class C { }", _editor.Document.Text);
            ColorAt(0, TokenColor("keyword"));
            ColorAt(6, _editor.TextColor!);
            _editor.Undo();
            Equal("class C { }", _editor.Document.Text);
            Equal(false, _editor.CanUndo, "typing undo units");
            await _editor.Coloring().RefreshAsync();
            Equal(true, _editor.CanRedo, "highlighting keeps redo");
            _editor.Redo();
            Equal("public class C { }", _editor.Document.Text);
        });

        await Test("native newline and indentation share one undo unit", "    value", async () =>
        {
            _editor.SelectedRange = new RichTextRange(_editor.Document.Length, 0);
            EditorContractTests.NativeReplace(_textView, "\n");
            await Task.Delay(200);
            Equal("    value\n    ", _editor.Document.Text);
            _editor.Undo();
            Equal("    value", _editor.Document.Text);
            Equal(false, _editor.CanUndo, "newline undo units");
            _editor.Redo();
            Equal("    value\n    ", _editor.Document.Text);
        });

        await Test("block indent and outdent are atomic", "one\ntwo\nthree", () =>
        {
            _editor.SelectedRange = new RichTextRange(0, 8);
            _actions.Indent();
            Equal("    one\n    two\nthree", _editor.Document.Text);
            _editor.Undo();
            Equal("one\ntwo\nthree", _editor.Document.Text);
            Equal(false, _editor.CanUndo);
            _editor.Redo();
            _editor.SelectedRange = new RichTextRange(0, 16);
            _actions.Outdent();
            Equal("one\ntwo\nthree", _editor.Document.Text);
            return Task.CompletedTask;
        });

        await Test("comments toggle around blank lines", "  first();\n\n\tsecond();\n", () =>
        {
            var source = _editor.Document.Text;
            _editor.SelectAll();
            _actions.ToggleLineComment();
            Equal("  // first();\n\n\t// second();\n", _editor.Document.Text);
            _editor.SelectAll();
            _actions.ToggleLineComment();
            Equal(source, _editor.Document.Text);
            return Task.CompletedTask;
        });

        await Test("find and replace use UTF16 whole words and one undo unit", "foo food FOO foo\u0301 foo", () =>
        {
            var options = new CodeSearchOptions(WholeWord: true);
            Equal(3, _actions.FindAll("foo", options).Count);
            Equal(new RichTextRange(0, 3), _actions.FindNext("foo", options));
            Equal(new RichTextRange(9, 3), _actions.FindNext("foo", options));
            Equal(3, _actions.ReplaceAll("foo", "bar", options));
            Equal("bar food bar foo\u0301 bar", _editor.Document.Text);
            _editor.Undo();
            Equal("foo food FOO foo\u0301 foo", _editor.Document.Text);
            Equal(false, _editor.CanUndo);
            return Task.CompletedTask;
        });

        await Test("read only and length limits reject partial commands", "a\na", () =>
        {
            _editor.MaxLength = 4;
            _editor.SelectAll();
            _actions.Indent();
            Equal(0, _actions.ReplaceAll("a", "long"));
            _editor.IsReadOnly = true;
            _actions.InsertNewLine();
            _actions.ToggleLineComment();
            Equal("a\na", _editor.Document.Text);
            Equal(false, _editor.CanUndo);
            return Task.CompletedTask;
        });

        await Test("disabling highlighting restores native default color", "return 42;", async () =>
        {
            _editor.Coloring().Enabled = false;
            await _editor.Coloring().RefreshAsync();
            Equal(0, _editor.Coloring().Tokens.Count);
            ColorAt(0, _editor.TextColor!);
            ColorAt(7, _editor.TextColor!);
            Equal(false, _editor.CanUndo);
        });

        await Test("line numbers and navigation track document replacement", "😀\n日本語\n", async () =>
        {
            Equal(3, _editor.LineCount);
            _actions.GoToLine(2, 3);
            Equal(new RichTextRange(5, 0), _editor.SelectedRange);
            var gutter = ((Grid)_editor.Content).Children.OfType<GraphicsView>().Single();
            Equal(true, gutter.Width > 0 && gutter.Height > 0, "gutter layout");
            _editor.ShowLineNumbers = false;
            await Task.Delay(30);
            Equal(false, gutter.IsVisible);
            _editor.ShowLineNumbers = true;
            var old = _editor.Document;
            _editor.Document = CodeDocument.FromPlainText("class New { }");
            old.Edit(edit => edit.InsertText(0, "detached\n"));
            await _editor.Coloring().RefreshAsync();
            Equal(1, _editor.LineCount);
            ColorAt(0, TokenColor("keyword"));
        });

        await Test("word wrap updates native layout", new string('x', 300) + "\nlast", async () =>
        {
            var unwrapped = NativeLineCount();
            _editor.WordWrap = true;
            await Task.Delay(100);
            var wrapped = NativeLineCount();
            Equal(true, wrapped > unwrapped, $"wrapped visual lines: {unwrapped} -> {wrapped}");
            _editor.WordWrap = false;
            await Task.Delay(100);
            Equal(unwrapped, NativeLineCount(), "unwrapped visual lines");
            Equal(2, _editor.LineCount, "logical lines");
            Equal(false, _editor.CanUndo);
            _editor.IsReadOnly = true;
            _editor.IsReadOnly = false;
            _editor.MaxLength = 1000;
            _editor.MaxLength = -1;
            _editor.Coloring().Palette = static _ => Colors.Purple;
            await _editor.Coloring().RefreshAsync();
            await Task.Delay(30);
            Equal(unwrapped, NativeLineCount(), "wrapping after input and appearance updates");
            EditorContractTests.NativeReplace(_textView, "// ");
            await _editor.Coloring().RefreshAsync();
            await Task.Delay(30);
            Equal(unwrapped, NativeLineCount(), "wrapping after typing and highlighting");
        });

        await Test("nested document undo groups project atomically", "start", async () =>
        {
            using (_editor.Document.BeginUndoGroup("Compound edit"))
            {
                _editor.Selection.ReplaceText("one ");
                using (_editor.Document.BeginUndoGroup()) _editor.Selection.ReplaceText("two ");
            }
            await _editor.Coloring().RefreshAsync();
            Equal("one two start", _editor.Document.Text);
            _editor.Undo();
            Equal("start", _editor.Document.Text);
            Equal(false, _editor.CanUndo);
            _editor.Redo();
            Equal("one two start", _editor.Document.Text);
        });

        await Test("derived formatting preserves both history directions", "a", async () =>
        {
            _editor.Coloring().Enabled = false;
            _editor.Document.Edit(edit => edit.InsertText(1, "b"));
            _editor.Document.Edit(edit => edit.InsertText(2, "c"));
            _editor.Undo();
            await _editor.Coloring().RefreshAsync();
            using var layer = _editor.Decorations.CreateLayer();
            layer.TrySet(_editor.Document.Revision, [new(new RichTextRange(0, 2), new RichTextDecorationStyle { ForegroundColor = Colors.Green })]);
            ColorAt(0, Colors.Green);
            Equal(true, _editor.CanUndo);
            Equal(true, _editor.CanRedo);
            _editor.Redo();
            Equal("abc", _editor.Document.Text);
            _editor.Undo();
            _editor.Undo();
            Equal("a", _editor.Document.Text);
            Equal(false, _editor.CanUndo);
        });

        await Test("IME composition survives pending highlighting", "", async () =>
        {
#if ANDROID
            var native = (Android.Widget.EditText)_textView.Handler!.PlatformView!;
            using var info = new Android.Views.InputMethods.EditorInfo();
            using var connection = native.OnCreateInputConnection(info)!;
            using var composing = new Java.Lang.String("int");
            using var committed = new Java.Lang.String("int ");
            connection.SetComposingText(composing, 1);
            await _editor.Coloring().RefreshAsync();
            Equal(0, Android.Views.InputMethods.BaseInputConnection.GetComposingSpanStart(native.EditableText!), "composition start");
            Equal(3, Android.Views.InputMethods.BaseInputConnection.GetComposingSpanEnd(native.EditableText!), "composition end");
            connection.CommitText(committed, 1);
            connection.FinishComposingText();
#else
            var native = (UIKit.UITextView)_textView.Handler!.PlatformView!;
            native.SetMarkedText("int", new Foundation.NSRange(3, 0));
            await _editor.Coloring().RefreshAsync();
            Equal(true, native.MarkedTextRange is not null, "marked range");
            native.UnmarkText();
            native.InsertText(" ");
#endif
            await Task.Delay(300);
            Equal("int ", _editor.Document.Text);
            ColorAt(0, TokenColor("keyword"));
            var count = 0;
            while (_editor.CanUndo && count++ < 8) _editor.Undo();
            Equal("", _editor.Document.Text, "composition undo");
            Equal(true, _editor.CanRedo);
        });

#if ANDROID
        await Test("hardware arrow keys retain native navigation", "abcd\nnext", async () =>
        {
            _editor.SelectedRange = new RichTextRange(2, 0);
            Key(Android.Views.Keycode.DpadLeft);
            await Task.Delay(30);
            Equal(new RichTextRange(1, 0), _editor.SelectedRange, "left arrow");
            Key(Android.Views.Keycode.DpadRight);
            Equal(new RichTextRange(2, 0), _editor.SelectedRange, "right arrow");
            Key(Android.Views.Keycode.MoveEnd);
            Equal(new RichTextRange(4, 0), _editor.SelectedRange, "End");
            Key(Android.Views.Keycode.MoveHome);
            Equal(RichTextRange.Empty, _editor.SelectedRange, "Home");
        });

        await Test("hardware Delete and Backspace retain native deletion", "abcd", () =>
        {
            _editor.SelectedRange = new RichTextRange(1, 0);
            Key(Android.Views.Keycode.ForwardDel);
            Equal("acd", _editor.Document.Text, "Delete");
            Key(Android.Views.Keycode.Del);
            Equal("cd", _editor.Document.Text, "Backspace");
            return Task.CompletedTask;
        });

        await Test("hardware backtick and punctuation reach native input", "", () =>
        {
            Key(Android.Views.Keycode.Grave);
            Key(Android.Views.Keycode.LeftBracket);
            Key(Android.Views.Keycode.RightBracket);
            Key(Android.Views.Keycode.Backslash);
            Equal("`[]\\", _editor.Document.Text);
            return Task.CompletedTask;
        });

        await Test("hardware Tab and Enter invoke code commands", "    value", async () =>
        {
            var native = (Android.Widget.EditText)_textView.Handler!.PlatformView!;
            _editor.SelectedRange = new RichTextRange(_editor.Document.Length, 0);
            using var enter = new Android.Views.KeyEvent(Android.Views.KeyEventActions.Down, Android.Views.Keycode.Enter);
            native.DispatchKeyEvent(enter);
            await Task.Delay(100);
            Equal("    value\n    ", _editor.Document.Text);
            using var tab = new Android.Views.KeyEvent(Android.Views.KeyEventActions.Down, Android.Views.Keycode.Tab);
            native.DispatchKeyEvent(tab);
            Equal("    value\n        ", _editor.Document.Text);
            _editor.Undo();
            _editor.Undo();
            Equal("    value", _editor.Document.Text);
            Equal(false, _editor.CanUndo);
        });
#else
        await Test("source input disables smart substitutions", "", () =>
        {
            var native = (UIKit.UITextView)_textView.Handler!.PlatformView!;
            Equal(UIKit.UITextAutocapitalizationType.None, native.AutocapitalizationType);
            Equal(UIKit.UITextSmartQuotesType.No, native.SmartQuotesType);
            Equal(UIKit.UITextSmartDashesType.No, native.SmartDashesType);
            Equal(UIKit.UITextSmartInsertDeleteType.No, native.SmartInsertDeleteType);
            return Task.CompletedTask;
        });
#endif

        await RunInputTests((name, source, action) => Test("input " + name, source, action));
        await RunFeatureTests((name, source, action) => Test(name, source, action));

        results.Add($"COMPLETE {results.Count} tests, {results.Count(result => result.StartsWith("FAIL", StringComparison.Ordinal))} failures");
        Save();
        Console.WriteLine(results[^1]);
    }

#if ANDROID
    private void Key(Android.Views.Keycode key)
    {
        var native = (Android.Widget.EditText)_textView.Handler!.PlatformView!;
        using var down = new Android.Views.KeyEvent(Android.Views.KeyEventActions.Down, key);
        using var up = new Android.Views.KeyEvent(Android.Views.KeyEventActions.Up, key);
        native.DispatchKeyEvent(down);
        native.DispatchKeyEvent(up);
    }
#endif

    private void ColorAt(int position, Color expected)
    {
#if ANDROID
        var native = (Android.Widget.EditText)_textView.Handler!.PlatformView!;
        using var paint = new Android.Text.TextPaint(native.Paint!);
        foreach (var span in native.EditableText!.GetSpans(position, position + 1,
            Java.Lang.Class.FromType(typeof(Android.Text.Style.CharacterStyle)))!.OfType<Android.Text.Style.CharacterStyle>())
            span.UpdateDrawState(paint);
        var actual = paint.Color.ToColor();
#else
        var native = (UIKit.UITextView)_textView.Handler!.PlatformView!;
        var attributes = new UIKit.UIStringAttributes(native.TextStorage.GetAttributes(position, out _));
        var actual = (attributes.ForegroundColor ?? native.TextColor!).ToColor();
#endif
        Equal(expected.ToArgbHex(), actual?.ToArgbHex(), $"native color at {position}");
    }

    private int NativeLineCount()
    {
#if ANDROID
        return ((Android.Widget.EditText)_textView.Handler!.PlatformView!).Layout!.LineCount;
#else
        var native = (UIKit.UITextView)_textView.Handler!.PlatformView!;
        var manager = native.LayoutManager;
        manager.EnsureLayoutForTextContainer(native.TextContainer);
        var count = 0;
        for (nuint glyph = 0; glyph < manager.NumberOfGlyphs; count++)
        {
            manager.GetLineFragmentUsedRect(glyph, out var range, true);
            glyph = (nuint)(range.Location + range.Length);
        }
        return count + (native.Text is not { Length: > 0 } text || text.EndsWith('\n') ? 1 : 0);
#endif
    }

    private static void Equal<T>(T expected, T actual, string? description = null) => EditorContractTests.Equal(expected, actual, description);
}
#endif
