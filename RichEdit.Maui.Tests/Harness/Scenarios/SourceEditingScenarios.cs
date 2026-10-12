#if WINDOWS || ((DEBUG || EDITOR_TESTS) && (ANDROID || IOS || MACCATALYST))
using CodeEdit.Maui;
using RichEdit.Maui.TestApp;
using static RichEdit.Maui.Tests.EditingScenarioContext;

namespace RichEdit.Maui.Tests;

internal static class SourceEditingScenarios
{
    internal static readonly string Sample = string.Join("\n", Enumerable.Range(0, 100).Select(i => $"    var value{i} = {i}; // editable source line {i}"));
    internal static IReadOnlyList<EditingScenario> Cases { get; } = Create().ToArray();
    private static IEnumerable<EditingScenario> Create()
    {
#if IOS || MACCATALYST
        yield return new("code/native/viewport-baseline", ["native control baseline"], context => context.CheckNativeViewportBaseline());
#endif
        yield return new("code/views/shared-editing", ["shared source", "independent selections", "native peer text", "caret", "scroll", "undo"], async context =>
        {
            await context.OpenCode(Sample); var peer = await context.AddCodePeer(0); var start = context.Code.GetLineRange(40).Start + 8;
            await context.SelectCode(start);
            await context.ObserveEdit("shared source input", () => context.Type("peer"));
            Equal(new TextSelectionState(0, 0), peer.SelectionState, "peer source caret remains independent");
            var peerSurface = ((Grid)peer.Content).Children.OfType<NativeEditorView>().Single();
            Check(NativeText(peerSurface).Contains("peer", StringComparison.Ordinal), "native source peer updates");
            context.Code.Undo(); await Settle(100); Equal(Sample, context.Text, "shared source undo");
            Check(!NativeText(peerSurface).Contains("peer", StringComparison.Ordinal), "native peer follows undo");
        });
        foreach (var wrap in new[] { false, true })
            yield return new("code/typing/wrap-" + wrap, ["typing", "caret", "selection", "scroll", "undo", "redo", "wrapping"], async context =>
            {
                await context.OpenCode(Sample); context.Code.WordWrap = wrap;
                var position = context.Code.GetLineRange(40).Start + 8;
                await context.SelectCode(position);
                await context.ObserveEdit("native typing", () => context.Type("abc"));
                Equal(Sample.Insert(position, "abc"), context.Text, "native source text");
                Equal(new TextSpan(position + 3, 0), context.Code.SelectedRange, "source caret");
                context.Code.Undo(); await Settle(100);
                Equal(Sample, context.Text, "undo source"); context.AssertNativeSelection();
                context.Code.Redo(); await Settle(100);
                Equal(Sample.Insert(position, "abc"), context.Text, "redo source"); context.AssertNativeSelection();
            });
        foreach (var ending in new[] { "\n", "\r\n", "\r" })
            yield return new("code/input/newline-" + (ending == "\n" ? "lf" : ending == "\r\n" ? "crlf" : "cr"), ["autoindent", "line endings", "caret", "undo"], async context =>
            {
                var original = "    call();" + ending + "next();";
                await context.OpenCode(original); context.Code.NewLine = ending;
                await context.SelectCode(11);
                await context.ObserveEdit("Enter", () => context.Type("\n"));
                Equal("    call();" + ending + "    " + ending + "next();", context.Text, "newline and indentation policy");
                Equal(new TextSpan(11 + ending.Length + 4, 0), context.Code.SelectedRange, "caret after indentation");
                context.Code.Undo(); await Settle(100); Equal(original, context.Text, "one undo for Enter and indentation");
                context.AssertNativeSelection();
            });

        yield return new("code/input/reversed-unicode-selection", ["selection", "Unicode", "caret", "undo", "scroll"], async context =>
        {
            await context.OpenCode(Sample); var start = context.Code.GetLineRange(40).Start + 8;
            await context.SelectCode(start, 5, true);
            await context.ObserveEdit("replace with Unicode", () => context.Type("日本語e\u0301👩‍💻"));
            Equal(Sample.Remove(start, 5).Insert(start, "日本語e\u0301👩‍💻"), context.Text, "Unicode input remains exact");
            context.Code.Undo(); await Settle(100); Equal(Sample, context.Text, "Unicode undo");
            Equal(new TextSelectionState(start + 5, start), context.Code.SelectionState, "undo restores directional selection");
        });
        yield return new("code/input/backspace-crlf", ["deletion", "line endings", "caret", "undo"], async context =>
        {
            await context.OpenCode("a\r\nb"); await context.SelectCode(3);
            await context.ObserveEdit("join source lines", context.Backspace);
            Equal("ab", context.Text, "CRLF is one line break"); Equal(new TextSpan(1, 0), context.Code.SelectedRange, "join caret");
            context.Code.Undo(); await Settle(100); Equal("a\r\nb", context.Text, "original CRLF restored");
        });
        foreach (var tabs in new[] { false, true })
            yield return new("code/commands/indent-" + tabs, ["indent", "outdent", "tabs", "selection", "undo"], async context =>
            {
                await context.OpenCode("one\ntwo\nthree"); context.Code.UseTabs = tabs; context.Code.IndentSize = 4;
                await context.SelectCode(0, 8);
                await context.ObserveEdit("indent", () => context.Code.Indent());
                var prefix = tabs ? "\t" : "    "; Equal(prefix + "one\n" + prefix + "two\nthree", context.Text, "selected lines indented");
                await context.ObserveEdit("outdent", () => context.Code.Outdent());
                Equal("one\ntwo\nthree", context.Text, "outdent reverses indentation");
                context.Code.Undo(); await Settle(100); Equal(prefix + "one\n" + prefix + "two\nthree", context.Text, "one undo for outdent");
            });
        yield return new("code/commands/comment", ["comments", "selection", "undo"], async context =>
        {
            await context.OpenCode("  one();\n\n  two();\n"); await context.SelectCode(0, context.Text.Length);
            await context.ObserveEdit("comment", () => context.Code.ToggleLineComment());
            Equal("  // one();\n\n  // two();\n", context.Text, "comments follow indentation and skip blank lines");
            context.Code.Undo(); await Settle(100); Equal("  one();\n\n  two();\n", context.Text, "comment undo");
        });
        yield return new("code/selections/multiple-carets", ["multiple carets", "typing", "caret", "undo", "redo", "scroll"], async context =>
        {
            await context.OpenCode(Sample); var first = context.Code.GetLineRange(40).Start + 8; var second = context.Code.GetLineRange(41).Start + 8;
            await context.SelectCode(first);
            context.Code.Selections = new((TextSelection[])[new(first, first), new(second, second)], 0);
            await context.ObserveEdit("multi-caret input", () => context.Type("XY"));
            Equal(Sample.Insert(second, "XY").Insert(first, "XY"), context.Text, "all carets receive input");
            Equal(2, context.Code.Selections.Count, "secondary caret retained");
            context.Code.Undo(); await Settle(100); Equal(Sample, context.Text, "one multi-caret undo");
            Equal(2, context.Code.Selections.Count, "undo restores carets");
        });
        yield return new("code/selections/virtual-space", ["virtual space", "caret", "undo"], async context =>
        {
            await context.OpenCode("ab\ncd"); context.Code.AllowVirtualSpace = true;
            await context.SelectCode(2);
            context.Code.Selections = new((TextSelection[])[new(2, 2) { AnchorVirtualSpaces = 4, ActiveVirtualSpaces = 4 }]);
            await context.ObserveEdit("virtual input", () => context.Type("X"));
            Equal("ab    X\ncd", context.Text, "virtual space materialized on input");
            context.Code.Undo(); await Settle(100); Equal("ab\ncd", context.Text, "undo removes materialized spaces");
        });
        yield return new("code/selections/rectangle", ["rectangle", "native geometry", "selection", "typing", "undo"], async context =>
        {
            await context.OpenCode("0123456789\n0123456789\n0123456789"); await context.SelectCode(0);
            var capture = context.Code.TextLayout.Capture()!;
            var first = context.Code.TextLayout.GetCaretBounds(capture, 2)!.Value;
            var last = context.Code.TextLayout.GetCaretBounds(capture, 27)!.Value;
            Check(context.Code.SelectRectangle(new(first.Left, first.Center.Y), new(last.Left, last.Center.Y)), "rectangle formed from native rows");
            Equal(3, context.Code.Selections.Count, "three rectangle rows");
            await context.ObserveEdit("rectangle replacement", () => context.Type("X"));
            Equal("01X56789\n01X56789\n01X56789", context.Text, "rectangle edits each row");
            context.Code.Undo(); await Settle(100); Equal("0123456789\n0123456789\n0123456789", context.Text, "rectangle undo");
        });
        yield return new("code/input/paired-rule", ["input rules", "caret", "undo", "redo"], async context =>
        {
            await context.OpenCode("abc"); await context.SelectCode(3);
            using var rule = context.Code.InputRules.Register(input =>
            {
                if (input.InputChanges.LastOrDefault()?.Text != "(") return;
                var caret = input.Selections.Primary.Active;
                input.Replace(new(caret, 0), ")"); input.SetSelections(TextSelectionSet.Caret(caret));
            });
            await context.ObserveEdit("pair delimiter", () => context.Type("("));
            Equal("abc()", context.Text, "paired input"); Equal(new TextSpan(4, 0), context.Code.SelectedRange, "caret between pair");
            context.Code.Undo(); await Settle(100); Equal("abc", context.Text, "pair is one undo");
        });
        yield return new("code/viewport/reveal-and-scroll-away", ["scroll", "reveal", "selection", "anchors"], async context =>
        {
            await context.OpenCode(Sample); await context.SelectCode(0);
            var selection = context.Code.SelectionState;
            Check(await context.Code.RevealAsync(context.Code.GetLineRange(70).Start, CodeRevealAlignment.Center), "distant reveal");
            await Settle(150); Equal(selection, context.Code.SelectionState, "reveal preserves selection");
            var offset = context.ScrollOffset(); Check(offset.Y > 100, "caret can remain offscreen while scrolling");
            using var anchor = context.Code.CaptureScrollAnchor()!;
            Check(await context.Code.RevealAsync(0), "reveal start");
            Check(await context.Code.RestoreScrollAnchorAsync(anchor), "restore source scroll anchor");
            await Settle(100); Check(Math.Abs(context.ScrollOffset().Y - offset.Y) < 2, "anchor restores exact viewport");
        });
        yield return new("code/document/limits-and-readonly", ["read-only", "length limit", "history"], async context =>
        {
            await context.OpenCode("abc"); await context.SelectCode(3); context.Code.MaxLength = 4;
            context.Type("X"); await Settle(80); Equal("abcX", context.Text, "input at limit");
            context.Type("Y"); await Settle(80); Equal("abcX", context.Text, "input beyond limit rejected");
            context.Code.IsReadOnly = true;
            try { context.Type("Z"); } catch (UnauthorizedAccessException) { }
            await Settle(80); Equal("abcX", context.Text, "read-only source unchanged");
        });
        yield return new("code/language/completion-and-snippet", ["completion", "snippets", "tracked placeholders", "selection", "caret", "undo"], async context =>
        {
            await context.OpenCode("Con"); await context.SelectCode(3);
            using var features = new CodeEditorFeatures(context.Code, context.Host);
            await features.ShowCompletionsAsync();
            await context.ObserveEdit("accept completion", () => Check(features.AcceptCompletion(0), "completion accepted"));
            Equal("Console", context.Text, "completion text");
            context.Code.Undo(); await Settle(100); Equal("Con", context.Text, "completion undo");
            context.Code.Document = new CodeDocument(); await context.SelectCode(0);
            await context.ObserveEdit("insert snippet", features.InsertSnippet);
            Equal("name", context.Code.Selection.Text, "first placeholder selected");
            context.Type("answer"); await Settle(100);
            Check(features.NextPlaceholder(), "next placeholder"); Equal("value", context.Code.Selection.Text, "second placeholder selected");
            Check(features.NextPlaceholder(previous: true), "previous placeholder"); Equal("answer", context.Code.Selection.Text, "edited placeholder tracked");
            context.AssertNativeSelection();
        });
        yield return new("code/language/search-replace", ["search", "replace", "selection", "undo"], async context =>
        {
            await context.OpenCode("alpha beta alpha\nalpha"); await context.SelectCode(0);
            var actions = new CodeEditorActions(context.Code);
            Equal(3, actions.FindAll("alpha").Count, "find all");
            Check(actions.FindNext("alpha") is not null, "find next"); context.AssertNativeSelection();
            await context.ObserveEdit("replace all", () => Equal(3, actions.ReplaceAll("alpha", "A"), "replacement count"));
            Equal("A beta A\nA", context.Text, "all replacements");
            context.Code.Undo(); await Settle(100); Equal("alpha beta alpha\nalpha", context.Text, "replace all is one undo");
        });
        yield return new("code/language/stale-checked-edit", ["checked edits", "stale analysis", "caret", "history"], async context =>
        {
            await context.OpenCode("word"); await context.SelectCode(4);
            var revision = context.Code.Document.Revision;
            context.Type("X"); await Settle(80); var selection = context.Code.SelectionState;
            Check(!context.Code.TryApplyEdits(revision, new TextReplacement[] { new(new(0, 4), "stale") }), "stale replacement rejected");
            Equal("wordX", context.Text, "stale analysis preserves typing"); Equal(selection, context.Code.SelectionState, "stale analysis preserves caret");
        });
        yield return new("code/presentation/decorations", ["syntax colors", "diagnostics", "stale publication", "selection", "scroll", "history"], async context =>
        {
            await context.OpenCode(Sample); var start = context.Code.GetLineRange(40).Start + 8;
            await context.SelectCode(start, 5);
            using var layer = context.Code.Decorations.CreateLayer();
            var stale = layer.BeginUpdate(); var current = layer.BeginUpdate();
            var items = new RichTextDecoration[] { new(new(start, 5), new() { ForegroundColor = Colors.Blue, Underline = RichTextUnderlineStyle.Wave }) };
            Check(!layer.TryPublish(stale, items), "stale decorations rejected");
            await context.ObserveEdit("publish diagnostics", () => Check(layer.TryPublish(current, items), "decoration publication"), sameSelection: true);
            Equal(Sample, context.Text, "diagnostics do not change source"); Check(!context.Code.CanUndo, "diagnostics do not enter history");
        });
        yield return new("code/clipboard/copy-cut-paste", ["clipboard", "selection", "caret", "undo", "redo"], async context =>
        {
            await context.OpenCode("alpha beta"); await context.SelectCode(0, 5);
            await context.Code.CopyAsync(); await context.SelectCode(10);
            await context.Code.PasteAsync(); await Settle(100);
            Equal("alpha betaalpha", context.Text, "plain clipboard paste"); context.AssertNativeSelection();
            context.Code.Undo(); await Settle(100); Equal("alpha beta", context.Text, "paste undo");
            await context.SelectCode(0, 5); await context.Code.CutAsync(); await Settle(100);
            Equal(" beta", context.Text, "cut selected source"); context.AssertNativeSelection();
            context.Code.Undo(); await Settle(100); Equal("alpha beta", context.Text, "cut undo");
        });
        yield return new("code/folding/edit-and-expand", ["folding", "native geometry", "caret", "selection", "undo"], async context =>
        {
            await context.OpenCode("head\ninside one\ninside two\ntail"); await context.SelectCode(2);
            context.Code.Folding.Collapse(new(5, 22)); await Settle(120);
            await context.ObserveEdit("type before fold", () => context.Type("X"));
            Equal("heXad\ninside one\ninside two\ntail", context.Text, "folded content retained after input");
            Equal(new TextSpan(6, 22), context.Code.Folding.CollapsedRanges.Single(), "fold tracks edit");
            context.Code.Undo(); await Settle(100); Equal(new TextSpan(5, 22), context.Code.Folding.CollapsedRanges.Single(), "fold tracks undo");
            context.Code.Folding.ExpandAll(); await Settle(100);
            Check(context.Code.TextLayout.Capture()!.Lines.Count >= 4, "expanded native lines present"); context.AssertNativeSelection();
        });
        yield return new("code/presentation/widget-input", ["inline widgets", "native child views", "caret", "selection", "undo"], async context =>
        {
            await context.OpenCode(Sample); var start = context.Code.GetLineRange(40).Start + 8; await context.SelectCode(start);
            View? realized = null;
            using var source = context.Code.Adornments.CreateSource(_ => realized = new Label { Text = "Hint", WidthRequest = 40, HeightRequest = 20 });
            Check(source.TryPublish(source.BeginUpdate(), new[] { new RichTextWidget("hint", start + 4, new(40, 20), Options: new() { Placement = RichTextAdornmentPlacement.Inline }) }), "source widget accepted");
            for (var i = 0; i < 100 && realized?.Handler is null; i++) await Task.Delay(20);
            Check(realized?.Handler is not null, "inline widget realized");
            await context.ObserveEdit("type near inline widget", () => context.Type("XY"));
            Equal(Sample.Insert(start, "XY"), context.Text, "widget reservation excluded from source");
            context.Code.Undo(); await Settle(100); Equal(Sample, context.Text, "input near widget undo"); context.AssertNativeSelection();
        });
#if ANDROID || IOS || MACCATALYST
        yield return new("code/input/native-composition", ["native IME protocol", "caret", "selection", "scroll", "undo"], async context =>
        {
            await context.OpenCode(Sample); var start = context.Code.GetLineRange(40).Start + 8; await context.SelectCode(start);
            await context.ObserveEdit("source IME commit", () => context.Compose("かな", "仮名"));
            Equal(Sample.Insert(start, "仮名"), context.Text, "source marked text commits exactly once");
            context.Code.Undo(); await Settle(100); Equal(Sample, context.Text, "one IME undo"); context.AssertNativeSelection();
        });
#endif
    }
}
#endif
