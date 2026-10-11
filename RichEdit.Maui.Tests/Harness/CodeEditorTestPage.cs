#if DEBUG && (ANDROID || IOS || MACCATALYST)
using Microsoft.Maui.Platform;
using CodeEdit.Maui;
using RichEdit.Maui.TestApp;

namespace RichEdit.Maui.Tests;

// These tests use the full control tree and inspect the native text surface.
internal sealed partial class CodeEditorTestPage : ContentPage
{
    private readonly CodeEditor _editor = new()
    {
        Document = CodeDocument.FromPlainText(new string('x', 300) + "\nlast"),
        TextColor = Colors.Black,
        BackgroundColor = Colors.White
    };
    private readonly Entry _focusTarget = new() { Placeholder = "Focus target" };
    private readonly NativeEditorView _textView;
    private readonly CodeEditorActions _actions;

    internal CodeEditorTestPage(string? filter)
    {
        _actions = new CodeEditorActions(_editor);
        _textView = ((Grid)_editor.Content).Children.OfType<NativeEditorView>().Single();
        var layout = new Grid { RowDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
        layout.Add(_editor);
        layout.Add(_focusTarget, 0, 1);
        Content = layout;
        SafeAreaEdges = new(SafeAreaRegions.Container);
        var started = false;
        _textView.Loaded += async (_, _) =>
        {
            if (started)
                return;

            started = true;
#if IOS || MACCATALYST
            await AppleTestEnvironment.RunAsync(() => RunAsync(filter));
#else
            await RunAsync(filter);
#endif
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
            $"Started {started:O}; library {typeof(NativeEditorView).Assembly.ManifestModule.ModuleVersionId}; code {typeof(CodeEditor).Assembly.ManifestModule.ModuleVersionId}\n" +
            string.Join("\n", results) + "\n");
        async Task Test(string name, string source, Func<Task> action, bool replaceDocument = true)
        {
            if (!string.IsNullOrEmpty(filter) && !name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                return;

            var index = results.Count;
            results.Add($"RUN {name}");
            Save();
            try
            {
                _editor.IsReadOnly = false;
                _editor.IsEnabled = true;
                _editor.MaxLength = -1;
                _editor.UseTabs = false;
                _editor.TabSize = 4;
                _editor.IndentSize = 4;
                _editor.FontSize = 14;
                _editor.AutoIndent = true;
                _editor.NewLine = "\n";
                _editor.WordWrap = false;
                _editor.AllowVirtualSpace = false;
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
                if (replaceDocument)
                    _editor.Document = CodeDocument.FromPlainText(source);

                _editor.SelectedRange = TextSpan.Empty;
                _editor.Focus();
                await Task.Delay(60);
                await _editor.Coloring().RefreshAsync();
                await action();
                await EditorContractTests.Verify(_textView);
                results[index] = $"PASS {name}";
            }
            catch (Exception exception)
            {
                results[index] = $"FAIL {name}: {exception}";
            }

            Save();
            Console.WriteLine(results[index]);
        }

        await Test("initial layout respects horizontal scrolling", "", () =>
        {
            Equal(2, NativeLineCount(), "initial visual lines");
            return Task.CompletedTask;
        }, replaceDocument: false);

        await Test("layout tab display width is native and independent of indentation", "\tX\n    X", async () =>
        {
            var before = _editor.Document.CurrentSnapshot;
            double X(int offset)
            {
                var layout = _editor.TextLayout.Capture() ?? throw new InvalidOperationException("Missing native tab layout.");
                return _editor.TextLayout.GetCaretBounds(layout, offset)?.Left ?? throw new InvalidOperationException("Missing native tab caret.");
            }
            var fourSpaces = X(7) - X(3);
            Equal(true, Math.Abs((X(1) - X(0)) - fourSpaces) <= 1, "four-column native tab");
            _editor.SelectedRange = new(1, 0);
            _editor.TabSize = 8;
            await Task.Delay(40);
            Equal(true, Math.Abs((X(1) - X(0)) - fourSpaces * 2) <= 1, "eight-column native tab");
            _editor.IndentSize = 2;
            Equal(8, _editor.TabSize);
            Equal(true, ReferenceEquals(before, _editor.Document.CurrentSnapshot));
            Equal(false, _editor.CanUndo);
            Equal(false, _editor.Document.IsModified);
            Equal(new TextSpan(1, 0), _editor.SelectedRange);
            _editor.WordWrap = true;
            _editor.FontSize = 20;
            await Task.Delay(60);
            Equal(true, Math.Abs((X(1) - X(0)) - (X(7) - X(3)) * 2) <= 1, "tab follows native font scaling with wrapping");
            _editor.WordWrap = false;
            using (var widget = _editor.Adornments.Add(0, new Label { Text = "hint", WidthRequest = 110, HeightRequest = 20 },
                new() { Placement = RichTextAdornmentPlacement.Inline }))
            {
                await Task.Delay(200);
                var interval = (X(7) - X(3)) * 2;
                var leading = X(0) - X(3);
                var expected = (Math.Floor((leading + 0.01) / interval) + 1) * interval;
                Equal(true, Math.Abs((X(1) - X(3)) - expected) <= 2, "native tabs include inline reservation widths");
            }
            await Task.Delay(60);
            _editor.SelectedRange = new(0, 0);
#if ANDROID
            var native = ((RichEditorHandler)_textView.Handler!).PlatformView;
            using var info = new Android.Views.InputMethods.EditorInfo();
            using var connection = native.OnCreateInputConnection(info)!;
            using var prefix = new Java.Lang.String("WWWWWWWWW");
            connection.CommitText(prefix, 1);
#else
            ((RichEditorHandler)_textView.Handler!).PlatformView.InsertText("WWWWWWWWW");
#endif
            await Task.Delay(60);
            Equal("WWWWWWWWW\tX\n    X", _editor.Document.Text);
            Equal(true, Math.Abs((X(10) - X(0)) - (X(16) - X(12)) * 4) <= 2, "native typing advances the following tab to its next stop");
        });

        await Test("rectangle virtual space typing composition and undo", "abcd\r\nx\r\nabcdef", async () =>
        {
            var layout = _editor.TextLayout.Capture()!;
            var first = _editor.TextLayout.GetCaretBounds(layout, 3)!.Value;
            var last = _editor.TextLayout.GetCaretBounds(layout, 12)!.Value;
            Equal(true, _editor.SelectRectangle(new(first.X, first.Center.Y), new(first.X, last.Center.Y), virtualSpace: true));
            Equal(3, _editor.Selections.Count);
            Equal(true, _editor.Selections.Select(item => item.Active).SequenceEqual(new[] { 3, 7, 12 }));
            Equal(2, _editor.Selections[1].ActiveVirtualSpaces);
            _editor.Selections = new(_editor.Selections, 1);
            var before = _editor.Selections;
            EditorContractTests.NativeReplace(_textView, "!");
            Equal("abc!d\r\nx  !\r\nabc!def", _editor.Document.Text);
            _editor.Undo();
            Equal("abcd\r\nx\r\nabcdef", _editor.Document.Text);
            Equal(2, _editor.Selections[1].ActiveVirtualSpaces);
            Equal(false, _editor.CanUndo);
#if ANDROID
            var native = ((RichEditorHandler)_textView.Handler!).PlatformView;
            using var info = new Android.Views.InputMethods.EditorInfo();
            using var connection = native.OnCreateInputConnection(info)!;
            using var composing = new Java.Lang.String("日");
            using var committed = new Java.Lang.String("日本");
            connection.SetComposingText(composing, 1);
            Equal("abcd\r\nx\r\nabcdef", _editor.Document.Text);
            connection.CommitText(committed, 1);
            connection.FinishComposingText();
#else
            var native = ((RichEditorHandler)_textView.Handler!).PlatformView;
            native.SetMarkedText("日", new Foundation.NSRange(1, 0));
            Equal("abcd\r\nx\r\nabcdef", _editor.Document.Text);
            native.SetMarkedText("日本", new Foundation.NSRange(2, 0));
            native.UnmarkText();
#endif
            await Task.Delay(40);
            Equal("abc日本d\r\nx  日本\r\nabc日本def", _editor.Document.Text);
            _editor.Undo();
            Equal(true, before.SequenceEqual(_editor.Selections));
            Equal(false, _editor.CanUndo);
            _editor.AllowVirtualSpace = true;
            _editor.Selections = new((TextSelection[])[before[1]]);
            Equal(true, _editor.MoveSelections(EditorKey.Right));
            Equal(3, _editor.Selections.Primary.ActiveVirtualSpaces);
            Equal(true, _editor.MoveSelections(EditorKey.Down));
            Equal(13, _editor.Selections.Primary.Active);
            Equal(0, _editor.Selections.Primary.ActiveVirtualSpaces);
            Equal(false, _editor.CanUndo);
            layout = _editor.TextLayout.Capture()!;
            first = _editor.TextLayout.GetCaretBounds(layout, 3)!.Value;
            last = _editor.TextLayout.GetCaretBounds(layout, 12)!.Value;
            RichTextPointerEventArgs Pointer(Point point, RichTextPointerButtons buttons = RichTextPointerButtons.Primary,
                EditorKeyModifiers modifiers = EditorKeyModifiers.Alt) => new(new(point.X - layout.Origin.X, point.Y - layout.Origin.Y),
                    RichTextPointerDeviceKind.Mouse, buttons, modifiers);
            Equal(true, _textView.ObservePointer(Pointer(first.Center), true));
            Equal(2, _editor.Selections.Count);
            Equal(true, _textView.ObservePointer(Pointer(new(first.X, last.Center.Y)), false));
            Equal(3, _editor.Selections.Count);
            Equal(true, _textView.ObservePointer(Pointer(last.Center, RichTextPointerButtons.None), false));
            Equal(false, _textView.ObservePointer(Pointer(last.Center, modifiers: EditorKeyModifiers.None), true));
            Equal(1, _editor.Selections.Count);
        });

        await Test("native word movement extends every caret", "alpha beta\r\ngamma delta", () =>
        {
            _editor.Selections = new((TextSelection[])[new(0, 0), new(12, 12)]);
            var identity = _editor.Selections.Primary.Id;
            Equal(true, _editor.MoveSelections(EditorKey.Right, extend: true, byWord: true));
            Equal(2, _editor.Selections.Count);
            Equal(identity, _editor.Selections.Primary.Id);
            Equal(0, _editor.Selections[0].Anchor);
            Equal(12, _editor.Selections[1].Anchor);
            foreach (var selection in _editor.Selections) Equal(true, selection.Range.Length is >= 5 and <= 6);
            Equal(false, _editor.CanUndo);
            Equal(true, _editor.ReplaceSelections("X"));
            _editor.Undo();
            Equal("alpha beta\r\ngamma delta", _editor.Document.Text);
            Equal(true, _editor.MoveSelections(EditorKey.Left, byWord: true));
            Equal(true, _editor.Selections.All(item => item.Range.IsEmpty));
            return Task.CompletedTask;
        });

        await Test("presentation revisions do not change source or saved state", "class C { }", async () =>
        {
            var before = _editor.Document.CurrentSnapshot;
            var rtf = _textView.Document.RtfText;
            _editor.Document.MarkSaved();
            _editor.SelectionState = new TextSelectionState(8, 1);
            _editor.Coloring().Palette = static _ => Colors.Purple;
            await _editor.Coloring().RefreshAsync();
            Equal(before.Version, _editor.Document.Version);
            Equal(rtf, _textView.Document.RtfText);
            Equal(false, _editor.Document.IsModified);
            Equal(new TextSelectionState(8, 1), _editor.SelectionState);
            Equal(new TextPosition(0, 1), _editor.CaretPosition);
            ColorAt(0, TokenColor("keyword"));
            using var layer = _editor.Decorations.CreateLayer();
            layer.TrySet(_editor.Document.Revision, [new(new TextSpan(0, 5), new RichTextDecorationStyle { ForegroundColor = Colors.Green })]);
            ColorAt(0, Colors.Green);
            Equal(before.Version, _editor.Document.Version);
            layer.Clear();
            ColorAt(0, TokenColor("keyword"));
        });

        await Test("source document undo restores direction and save points", "123456", async () =>
        {
            _editor.Document.MarkSaved();
            _editor.SelectionState = new TextSelectionState(4, 1);
            _editor.Selection.ReplaceText("9");
            _editor.SelectionState = new TextSelectionState(0, 0);
            _editor.Undo();
            await _editor.Coloring().RefreshAsync();
            Equal("123456", _editor.Document.Text);
            Equal(new TextSelectionState(4, 1), _editor.SelectionState);
            Equal(false, _editor.Document.IsModified);
            Equal(1, _editor.GetOffset(_editor.CaretPosition));
            _editor.Redo();
            Equal("1956", _editor.Document.Text);
            Equal(new TextSelectionState(2, 2), _editor.SelectionState);
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
            _editor.SelectedRange = new TextSpan(6, 1);
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
            _editor.SelectedRange = new TextSpan(_editor.Document.Length, 0);
            var observed = new List<string>();
            void Changed(object? sender, TextDocumentChangedEventArgs args) => observed.Add(_editor.Document.Text);
            _editor.Document.Changed += Changed;
            try
            {
                EditorContractTests.NativeMultiInsert(_textView, "\n");
                await Task.Delay(200);
            }
            finally { _editor.Document.Changed -= Changed; }
            Equal("    value\n    ", _editor.Document.Text);
            Equal(1, observed.Count, "native newline and indentation publish once");
            Equal(_editor.Document.Text, observed[0]);
            _editor.Undo();
            Equal("    value", _editor.Document.Text);
            Equal(false, _editor.CanUndo, "newline undo units");
            _editor.Redo();
            Equal("    value\n    ", _editor.Document.Text);
        });

        await Test("block indent and outdent are atomic", "one\ntwo\nthree", () =>
        {
            _editor.SelectedRange = new TextSpan(0, 8);
            _actions.Indent();
            Equal("    one\n    two\nthree", _editor.Document.Text);
            _editor.Undo();
            Equal("one\ntwo\nthree", _editor.Document.Text);
            Equal(false, _editor.CanUndo);
            _editor.Redo();
            _editor.SelectedRange = new TextSpan(0, 16);
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
            Equal(new TextSpan(0, 3), _actions.FindNext("foo", options));
            Equal(new TextSpan(9, 3), _actions.FindNext("foo", options));
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
#if IOS || MACCATALYST
            var native = (UIKit.UITextView)_textView.Handler!.PlatformView!;
            native.LayoutManager.InvalidateLayout(new Foundation.NSRange(0, native.TextStorage.Length));
#endif
            var captured = _editor.TextLayout.Capture() ?? throw new InvalidOperationException("Missing code layout.");
            Equal(true, captured.Lines.SelectMany(line => line.SourceRanges).Select(range => range.Start).SequenceEqual(new[] { 0, 3, 7 }),
                "visible line-number source positions after layout invalidation");
            _actions.GoToLine(2, 3);
            Equal(new TextSpan(5, 0), _editor.SelectedRange);
            var gutter = ((Grid)_editor.Content).Children.OfType<GraphicsView>().Single();
            Equal(true, gutter.Width > 0 && gutter.Height > 0, "gutter layout");
#if IOS || MACCATALYST
            _editor.TextColor = Colors.Black;
            _editor.BackgroundColor = Colors.White;
            gutter.Invalidate();
            await Task.Delay(30);
            VerifyLineNumberPixels(gutter);
#endif
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
#if IOS || MACCATALYST
            gutter.Invalidate();
            await Task.Delay(30);
            VerifyLineNumberPixels(gutter);
#endif
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

        await Test("mixed source endings survive native edits and clipboard", "a\r\nb\rc\n", async () =>
        {
            Equal("a\r\nb\rc\n", _editor.Document.Text);
            _editor.SelectedRange = new(4, 0);
            EditorContractTests.NativeReplace(_textView, "XY");
            await EditorContractTests.Drain();
            Equal("a\r\nbXY\rc\n", _editor.Document.Text);
            Equal(new TextSpan(6, 0), _editor.SelectedRange);
            _editor.Undo();
            Equal("a\r\nb\rc\n", _editor.Document.Text);
            _editor.SelectAll();
            await _editor.CopyAsync();
            _editor.Document = new();
            await _editor.PasteAsync();
            Equal("a\r\nb\rc\n", _editor.Document.Text);
            _editor.Undo();
            Equal("", _editor.Document.Text);
        });

        await Test("CRLF native newline and indentation commit at every caret", "  a\r\n    b", async () =>
        {
            _editor.NewLine = "\r\n";
            _editor.Selections = new((TextSelection[])[new(3, 3), new(10, 10)]);
            var changes = 0;
            void Changed(object? sender, TextDocumentChangedEventArgs args) => changes++;
            _editor.ContentChanged += Changed;
            try
            {
                EditorContractTests.NativeMultiInsert(_textView, "\n");
                await EditorContractTests.Drain();
                Equal("  a\r\n  \r\n    b\r\n    ", _editor.Document.Text);
                Equal(1, changes);
                Equal("7,20", string.Join(",", _editor.Selections.Select(selection => selection.Active)));
                _editor.Undo();
                Equal("  a\r\n    b", _editor.Document.Text);
                Equal(false, _editor.CanUndo);
            }
            finally { _editor.ContentChanged -= Changed; }
        });

        await Test("nested document undo groups project atomically", "start", async () =>
        {
            using (_editor.Document.BeginUndoGroup("Compound edit"))
            {
                _editor.Selection.ReplaceText("one ");
                using (_editor.Document.BeginUndoGroup())
                    _editor.Selection.ReplaceText("two ");
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
            layer.TrySet(_editor.Document.Revision, [new(new TextSpan(0, 2), new RichTextDecorationStyle { ForegroundColor = Colors.Green })]);
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
            _editor.TabSize = 8;
            Equal(4, _textView.SourceTabSize, "tab display changes wait for composition");
            await _editor.Coloring().RefreshAsync();
            Equal(0, Android.Views.InputMethods.BaseInputConnection.GetComposingSpanStart(native.EditableText!), "composition start");
            Equal(3, Android.Views.InputMethods.BaseInputConnection.GetComposingSpanEnd(native.EditableText!), "composition end");
            connection.CommitText(committed, 1);
            connection.FinishComposingText();
#else
            var native = (UIKit.UITextView)_textView.Handler!.PlatformView!;
            native.SetMarkedText("int", new Foundation.NSRange(3, 0));
            _editor.TabSize = 8;
            Equal(4, _textView.SourceTabSize, "tab display changes wait for composition");
            await _editor.Coloring().RefreshAsync();
            Equal(true, native.MarkedTextRange is not null, "marked range");
            native.UnmarkText();
            native.InsertText(" ");
#endif
            await Task.Delay(300);
            Equal("int ", _editor.Document.Text);
            Equal(8, _textView.SourceTabSize, "tab display changes apply after composition");
            ColorAt(0, TokenColor("keyword"));
            var count = 0;
            while (_editor.CanUndo && count++ < 8)
                _editor.Undo();

            Equal("", _editor.Document.Text, "composition undo");
            Equal(true, _editor.CanRedo);
        });

#if ANDROID
        await Test("hardware arrow keys retain native navigation", "abcd\nnext", async () =>
        {
            _editor.SelectedRange = new TextSpan(2, 0);
            Key(Android.Views.Keycode.DpadLeft);
            await Task.Delay(30);
            Equal(new TextSpan(1, 0), _editor.SelectedRange, "left arrow");
            Key(Android.Views.Keycode.DpadRight);
            Equal(new TextSpan(2, 0), _editor.SelectedRange, "right arrow");
            Key(Android.Views.Keycode.MoveEnd);
            Equal(new TextSpan(4, 0), _editor.SelectedRange, "End");
            Key(Android.Views.Keycode.MoveHome);
            Equal(TextSpan.Empty, _editor.SelectedRange, "Home");
        });

        await Test("hardware Delete and Backspace retain native deletion", "abcd", () =>
        {
            _editor.SelectedRange = new TextSpan(1, 0);
            Key(Android.Views.Keycode.ForwardDel);
            Equal("acd", _editor.Document.Text, "Delete");
            Key(Android.Views.Keycode.Del);
            Equal("cd", _editor.Document.Text, "Backspace");
            return Task.CompletedTask;
        });

        await Test("input services bound large source payloads with canonical offsets", "", () =>
        {
            _editor.Document = new(new string('x', 100_000));
            _editor.SelectedRange = new(50_000, 0);
            var native = (RichEdit.Maui.Platforms.Android.RichEditText)_textView.Handler!.PlatformView!;
            Equal(true, native.AutofillValue is null);
            using var info = new Android.Views.InputMethods.EditorInfo();
            using var connection = native.OnCreateInputConnection(info)!;
            using var request = new Android.Views.InputMethods.ExtractedTextRequest();
            using var extracted = connection.GetExtractedText(request, Android.Views.InputMethods.GetTextFlags.None)!;
            Equal(true, extracted.Text!.Length() <= 8192);
            Equal(50_000, extracted.StartOffset + extracted.SelectionStart);
            Equal(50_000, extracted.StartOffset + extracted.SelectionEnd);
            using var before = connection.GetTextBeforeCursorFormatted(int.MaxValue, Android.Views.InputMethods.GetTextFlags.None)!;
            using var after = connection.GetTextAfterCursorFormatted(int.MaxValue, Android.Views.InputMethods.GetTextFlags.None)!;
            Equal(8192, before.Length()); Equal(8192, after.Length());
            if (OperatingSystem.IsAndroidVersionAtLeast(31))
            {
                using var surrounding = connection.GetSurroundingText(int.MaxValue, int.MaxValue, 0)!;
                Equal(true, surrounding.Text!.Length <= 8192);
                Equal(50_000, surrounding.Offset + surrounding.SelectionStart);
            }
            _editor.SelectedRange = new(0, 100_000);
            Equal(true, connection.GetSelectedTextFormatted(Android.Views.InputMethods.GetTextFlags.None) is null);
            Equal(true, connection.GetExtractedText(request, Android.Views.InputMethods.GetTextFlags.None) is null);
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
            _editor.SelectedRange = new TextSpan(_editor.Document.Length, 0);
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
        await Test("accessibility reads indexed source lines without flattening the document", "", () =>
        {
            _editor.Coloring().Enabled = false;
            var native = (UIKit.IUIAccessibilityReadingContent)_textView.Handler!.PlatformView!;
            _ = native.GetAccessibilityContent(0);
            _editor.Document = new(string.Concat(Enumerable.Repeat("alpha\r\nbeta\rchar\n", 10_000)));
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            Equal("alpha\r\n", native.GetAccessibilityContent(29_997));
            Equal("beta\r", native.GetAccessibilityContent(29_998));
            Equal("char\n", native.GetAccessibilityContent(29_999));
            Equal("", native.GetAccessibilityContent(30_000));
            Equal("", native.GetAccessibilityContent(30_001));
            Equal("", native.GetAccessibilityContent(-1));
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Equal(true, allocated < 256_000, $"bounded line reads allocated {allocated} bytes");
            return Task.CompletedTask;
        });
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

        if (string.Equals(filter, "performance", StringComparison.OrdinalIgnoreCase))
            await Test("performance modern source dense", "", () => SourcePerformanceWorkload.RunDense(_editor));
        if (string.Equals(filter, "large", StringComparison.OrdinalIgnoreCase))
            await Test("large native source and long logical line", "", () => SourcePerformanceWorkload.RunLarge(_editor));
        await RunInputTests((name, source, action) => Test("input " + name, source, action));
        await RunFeatureTests((name, source, action) => Test(name, source, action));
        await RunViewportTests((name, source, action) => Test(name, source, action));

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

#if IOS || MACCATALYST
    private void VerifyLineNumberPixels(GraphicsView gutter)
    {
        var native = gutter.ToPlatform(_textView.Handler!.MauiContext!);
        native.LayoutIfNeeded();
        native.SetNeedsDisplay();
        native.Layer.DisplayIfNeeded();
        using var format = new UIKit.UIGraphicsImageRendererFormat { Opaque = true, PreferredRange = UIKit.UIGraphicsImageRendererFormatRange.Standard };
        using var renderer = new UIKit.UIGraphicsImageRenderer(native.Bounds.Size, format);
        using var image = renderer.CreateImage(context =>
        {
            context.CGContext.SetFillColor(UIKit.UIColor.White.CGColor);
            context.CGContext.FillRect(native.Bounds);
            native.Layer.RenderInContext(context.CGContext);
        });
        var bitmap = image.CGImage!;
        using var pixels = bitmap.DataProvider.CopyData()!;
        var bytes = pixels.ToArray();
        Equal(32, (int)bitmap.BitsPerPixel, "line-number bitmap format");
        // Fractional view dimensions leave a partially covered pixel at the edge.
        // Compare only pixels wholly inside the view, excluding that raster padding.
        var width = Math.Min((int)bitmap.Width, (int)Math.Floor(native.Bounds.Width * format.Scale));
        var height = Math.Min((int)bitmap.Height, (int)Math.Floor(native.Bounds.Height * format.Scale));
        var background = BitConverter.ToUInt32(bytes, 0);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var offset = y * (int)bitmap.BytesPerRow + x * 4;
                if (BitConverter.ToUInt32(bytes, offset) != background)
                    return;
            }

        using var png = image.AsPNG()!;
        png.Save(Path.Combine(FileSystem.CacheDirectory, "code-gutter-failure.png"), false, out _);
        throw new InvalidOperationException("The laid-out line-number gutter contains no text pixels.");
    }
#endif

    private static void Equal<T>(T expected, T actual, string? description = null) => EditorContractTests.Equal(expected, actual, description);
}
#endif
