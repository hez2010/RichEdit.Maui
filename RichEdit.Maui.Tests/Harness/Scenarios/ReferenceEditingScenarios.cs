#if WINDOWS || ((DEBUG || EDITOR_TESTS) && (ANDROID || IOS || MACCATALYST))
using RichEdit.Core;
using RichEdit.Core.Rtf;
using CoreSelection = RichEdit.Core.RichTextSelection;
using static RichEdit.Maui.Tests.EditingScenarioContext;

namespace RichEdit.Maui.Tests;

internal static partial class ReferenceEditingScenarios
{
    internal static IReadOnlyList<EditingScenario> Cases { get; } = Create().ToArray();
    private static IEnumerable<EditingScenario> Create()
    {
        foreach (var (id, style) in CharacterStyles())
            yield return new("rich/character/" + id, ["formatting", "selection", "scroll", "undo", "redo", "RTF"], async context =>
            {
                await context.OpenReference();
                await context.Select(context.Find("The text you are reading is aligned left", 4), 4);
                var before = await context.SaveRich();
                await context.ObserveEdit(id, () => Check(context.Rich.ApplyCharacterFormat(style).Accepted, "format accepted"), sameSelection: true);
                void Verify() => Check(context.Rich.Document.CurrentSnapshot.GetSelectedCharacterFormats(context.Rich.Selection).All(format => Matches(style, format)), "requested character values retained");
                Verify();
                context.AssertNativeCharacterStyle(style);
                await context.CheckUndo(before, Verify);
                using var input = new MemoryStream(await context.SaveRich());
                var imported = await RichEdit.Core.Rtf.RtfCodec.ReadAsync(input);
                var projection = imported.Snapshot.ProjectText();
                var start = projection.Text.ToString().IndexOf("The text you are reading is aligned left", StringComparison.Ordinal) + 4;
                Check(imported.Snapshot.GetSelectedCharacterFormats(new CoreSelection(projection.GetPoint(start)!.Value, projection.GetPoint(start + 4)!.Value)).All(format => Matches(style, format)), "RTF retains character values");
            });
        foreach (var (id, style) in ParagraphStyles())
            yield return new("rich/paragraph/" + id, ["formatting", "selection", "scroll", "undo", "redo"], async context =>
            {
                await context.OpenReference();
                var point = context.Find("The text you are reading is aligned left", 4);
                await context.Select(point, 4);
                var before = await context.SaveRich();
                await context.ObserveEdit(id, () => Check(context.Rich.Edit(edit => edit.ApplyParagraphFormat(context.Rich.Selection, style)).Accepted, "paragraph format accepted"), sameSelection: true);
                void Verify() => Check(Matches(style, context.Rich.Document.CurrentSnapshot.ResolveParagraph(point.Paragraph)), "paragraph properties retained");
                Verify(); await context.CheckUndo(before, Verify);
            });

        foreach (var location in new[] { "start", "middle", "end" })
            yield return new("rich/typing/" + location, ["typing", "caret", "scroll", "undo", "redo", "format inheritance"], async context =>
            {
                await context.OpenReference();
                var point = context.Find("The text you are reading is aligned left");
                var length = context.Rich.Document.CurrentSnapshot.GetParagraphLength(point.Paragraph);
                point = point with { Offset = location == "start" ? 0 : location == "end" ? length : point.Offset + 12 };
                await context.Select(point);
                var before = await context.SaveRich();
                var text = context.Text;
                var offset = context.Rich.Document.CurrentSnapshot.ProjectText().GetOffset(point);
                await context.ObserveEdit("type", () => context.Type("abc"));
                Equal(text.Insert(offset, "abc"), context.Text, "exact inserted text");
                Equal(offset + 3, context.Rich.NativeView.SelectedRange.Start, "caret after inserted text");
                await context.CheckUndo(before, () => Equal(text.Insert(offset, "abc"), context.Text, "redo restores input"));
            });

        foreach (var numbered in new[] { false, true })
            foreach (var edge in new[] { "middle", "end" })
                yield return new($"rich/list/{(numbered ? "numbered" : "bullet")}-{edge}", ["lists", "enter", "backspace", "caret", "scroll", "undo"], async context =>
                {
                    await context.OpenReference();
                    var snapshot = context.Rich.Document.CurrentSnapshot;
                    var list = snapshot.Nodes.Values.OfType<RichList>().First(item => item.Numbered == numbered);
                    var paragraph = snapshot.Get<RichListItem>(list.Items[0]).Blocks[0];
                    var length = snapshot.GetParagraphLength(paragraph);
                    var point = new RichTextPoint(snapshot.Body, paragraph, edge == "end" ? length : Math.Min(3, length));
                    await context.Select(point);
                    var before = await context.SaveRich();
                    await context.ObserveEdit("list Enter", () => context.Type("\n"));
                    Equal(list.Items.Count + 1, context.Rich.Document.CurrentSnapshot.Get<RichList>(list.Id).Items.Count, "new item");
                    await context.ObserveEdit("join item", context.Backspace, stableScroll: false);
                    var restored = await context.SaveRich();
                    Check(before.AsSpan().SequenceEqual(restored), "Backspace restores original list content and styles");
                });

        yield return new("rich/selection/reversed-replacement", ["selection", "typing", "caret", "scroll", "undo"], async context =>
        {
            await context.OpenReference();
            var point = context.Find("The text you are reading is aligned left", 4);
            await context.Select(point, 4, reversed: true);
            var original = context.Text; var position = context.Rich.Document.CurrentSnapshot.ProjectText().GetOffset(point);
            var saved = await context.SaveRich();
            await context.ObserveEdit("replace selection", () => context.Type("word"));
            Equal(original.Remove(position, 4).Insert(position, "word"), context.Text, "reversed replacement");
            await context.CheckUndo(saved, () => Equal(position + 4, context.Rich.NativeView.SelectedRange.Start, "redo caret"));
        });
        yield return new("rich/typing/read-only", ["read-only", "caret", "selection", "history"], async context =>
        {
            await context.OpenReference(); await context.Select(context.Find("The text you are reading is aligned left", 5));
            var saved = await context.SaveRich(); context.Rich.IsReadOnly = true;
            try { context.Type("rejected"); } catch (UnauthorizedAccessException) { /* Native TOM explicitly rejects read-only input. */ }
            await Settle(100);
            var after = await context.SaveRich();
            Check(saved.AsSpan().SequenceEqual(after), "read-only input changes no content");
            Check(!context.Rich.CanUndo, "read-only input creates no history");
        });
        foreach (var test in ContentCases()) yield return test;
        foreach (var test in ServiceCases()) yield return test;
    }

    private static IEnumerable<(string, RichCharacterStyle)> CharacterStyles()
    {
        yield return ("bold", new() { Bold = true });
        yield return ("italic", new() { Italic = true });
        yield return ("font-family", new() { FontFamily = "Arial" });
        yield return ("font-size", new() { FontSize = RichFontSize.FromPoints(18) });
        foreach (var value in Enum.GetValues<RichUnderline>()) yield return ("underline-" + value, new() { Underline = value });
        yield return ("underline-color", new() { Underline = RichUnderline.Single, UnderlineColor = new RichColor(180, 40, 40) });
        foreach (var value in new[] { RichStrike.Single, RichStrike.Double }) yield return ("strike-" + value, new() { Strike = value });
        yield return ("foreground", new() { Foreground = new RichColor(40, 70, 180) });
        yield return ("highlight", new() { Background = new RichColor(245, 220, 100) });
        yield return ("shading", new() { Shading = new RichShading(5000, new(80, 120, 160), new(245, 245, 245)) });
        yield return ("superscript", new() { VerticalPosition = RichVerticalPosition.Superscript });
        yield return ("subscript", new() { VerticalPosition = RichVerticalPosition.Subscript });
        yield return ("baseline", new() { VerticalPosition = RichVerticalPosition.Offset(4) });
        yield return ("spacing", new() { CharacterSpacing = RichLength.FromPoints(1) });
        yield return ("width", new() { HorizontalScalePercent = 120 });
        yield return ("small-caps", new() { SmallCaps = true });
        yield return ("all-caps", new() { AllCaps = true });
        yield return ("outline", new() { Outline = true });
        yield return ("shadow", new() { Shadow = true });
        yield return ("hidden", new() { Hidden = true });
        yield return ("language", new() { Language = "en-US" });
        yield return ("direction", new() { Direction = RichDirection.RightToLeft });
    }

    private static IEnumerable<(string, RichParagraphStyle)> ParagraphStyles()
    {
        foreach (var value in Enum.GetValues<RichAlignment>()) yield return ("alignment-" + value, new() { Alignment = value });
        yield return ("direction", new() { Direction = RichDirection.RightToLeft });
        yield return ("indents", new() { LeadingIndent = new RichLength(240), TrailingIndent = new RichLength(120), FirstLineIndent = new RichLength(120) });
        yield return ("spacing", new() { SpaceBefore = new RichLength(120), SpaceAfter = new RichLength(240) });
        yield return ("line-exact", new() { LineSpacing = RichLineSpacing.Exactly(new(420)) });
        yield return ("line-minimum", new() { LineSpacing = RichLineSpacing.AtLeast(new(420)) });
        yield return ("line-multiple", new() { LineSpacing = RichLineSpacing.Multiple(1.5) });
        yield return ("tabs", new() { TabStops = new RichTabStops(new RichTabStop[] { new(new(720)), new(new(1440), RichTabAlignment.Right, RichTabLeader.Dots) }) });
        yield return ("heading-flow", new() { HeadingLevel = 2, KeepWithNext = true, KeepTogether = true, WidowControl = true });
        yield return ("hyphenation", new() { Hyphenation = true });
        yield return ("border", new() { Border = new RichBorder(RichBorderSides.All, RichBorderStyle.Single, new(20), new(40, 80, 120)) });
        yield return ("shading", new() { Shading = new RichShading(5000, new(80, 120, 160), new(245, 245, 245)) });
    }

    private static bool V<T>(StyleValue<T> expected, T actual) => expected.Kind != StyleValueKind.Value || EqualityComparer<T>.Default.Equals(expected.Value, actual);
    private static bool Matches(RichCharacterStyle x, ResolvedCharacterStyle y) =>
        V(x.Bold, y.Bold) && V(x.Italic, y.Italic) && V(x.FontFamily, y.FontFamily) && V(x.FontSize, y.FontSize) &&
        V(x.Underline, y.Underline) && V(x.UnderlineColor, y.UnderlineColor) && V(x.Strike, y.Strike) &&
        V(x.Foreground, y.Foreground) && V(x.Background, y.Background) && V(x.Shading, y.Shading) &&
        V(x.VerticalPosition, y.VerticalPosition) && V(x.CharacterSpacing, y.CharacterSpacing) && V(x.HorizontalScalePercent, y.HorizontalScalePercent) &&
        V(x.SmallCaps, y.SmallCaps) && V(x.AllCaps, y.AllCaps) && V(x.Outline, y.Outline) && V(x.Shadow, y.Shadow) &&
        V(x.Hidden, y.Hidden) && V(x.Language, y.Language) && V(x.Direction, y.Direction);
    private static bool Matches(RichParagraphStyle x, ResolvedParagraphStyle y) =>
        V(x.Alignment, y.Alignment) && V(x.Direction, y.Direction) && V(x.LeadingIndent, y.LeadingIndent) && V(x.TrailingIndent, y.TrailingIndent) &&
        V(x.FirstLineIndent, y.FirstLineIndent) && V(x.SpaceBefore, y.SpaceBefore) && V(x.SpaceAfter, y.SpaceAfter) && V(x.LineSpacing, y.LineSpacing) &&
        V(x.TabStops, y.TabStops) && V(x.HeadingLevel, y.HeadingLevel) && V(x.KeepWithNext, y.KeepWithNext) && V(x.KeepTogether, y.KeepTogether) &&
        V(x.WidowControl, y.WidowControl) && V(x.Hyphenation, y.Hyphenation) && V(x.Border, y.Border) && V(x.Shading, y.Shading);
}
#endif
