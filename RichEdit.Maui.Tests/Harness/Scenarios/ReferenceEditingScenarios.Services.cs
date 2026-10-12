#if WINDOWS || ((DEBUG || EDITOR_TESTS) && (ANDROID || IOS || MACCATALYST))
using RichEdit.Core;
using CoreSelection = RichEdit.Core.RichTextSelection;
using static RichEdit.Maui.Tests.EditingScenarioContext;

namespace RichEdit.Maui.Tests;

internal static partial class ReferenceEditingScenarios
{
    private static IEnumerable<EditingScenario> ServiceCases()
    {
#if IOS || MACCATALYST
        yield return new("rich/native/viewport-baseline", ["native reference baseline"], context => context.CheckNativeViewportBaseline(reference: true));
#endif
        yield return new("rich/views/shared-editing", ["shared document", "independent selections", "native peer text", "caret", "scroll", "undo"], async context =>
        {
            await context.OpenReference(); var point = context.Find("The text you are reading is aligned left", 4);
            var otherPoint = context.Find("The text you are reading is aligned right", 4); var otherSelection = new CoreSelection(otherPoint, otherPoint);
            var peer = await context.AddRichPeer(otherSelection); await context.Select(point); var before = await context.SaveRich();
            await context.ObserveEdit("shared native typing", () => context.Type("SharedScenario"));
            Equal<RichSelection>(otherSelection, peer.Selection, "peer caret remains independent");
            var peerSurface = peer.NativeView.FindStructuredSurface(new CoreSelection(point, point)) ?? peer.NativeView;
            Check(NativeText(peerSurface).Contains("SharedScenario", StringComparison.Ordinal), "peer native text updates");
            await context.CheckUndo(before, () => Check(NativeText(peerSurface).Contains("SharedScenario", StringComparison.Ordinal), "peer native text follows redo"));
        });
        yield return new("rich/formatting/typing-state", ["typing format", "native attributes", "caret", "undo"], async context =>
        {
            await context.OpenReference(); var point = context.Find("The text you are reading is aligned left", 4);
            await context.Select(point); var before = await context.SaveRich();
            var style = new RichCharacterStyle { Bold = true, Italic = true, FontSize = RichFontSize.FromPoints(18) };
            await context.ObserveEdit("choose typing style", () => context.Rich.TypingCharacterFormat = style, sameSelection: true);
            await context.ObserveEdit("formatted typing", () => context.Type("styled"));
            await context.Select(point, 6);
            Check(context.Rich.Document.CurrentSnapshot.GetSelectedCharacterFormats(context.Rich.Selection).All(format => Matches(style, format)), "typing inherits chosen attributes");
            context.AssertNativeCharacterStyle(style);
            await context.CheckUndo(before, () => Check(context.Text.Contains("styled", StringComparison.Ordinal), "styled redo"));
        });
        yield return new("rich/styles/named-paragraph", ["named styles", "cascade", "caret", "scroll", "undo"], async context =>
        {
            await context.OpenReference(); var point = context.Find("The text you are reading is aligned left", 4);
            await context.Select(point, 4); var before = await context.SaveRich();
            var id = Enumerable.Range(1, short.MaxValue).Select(value => new RichStyleId(value)).First(value => !context.Rich.Document.CurrentSnapshot.Styles.Definitions.ContainsKey(value));
            await context.ObserveEdit("apply named style", () => context.Rich.Edit(edit =>
            {
                edit.SetStyles(new(context.Rich.Document.CurrentSnapshot.Styles.Definitions.Values.Append(
                    new(id, "Scenario paragraph", RichStyleKind.Paragraph) { Paragraph = new() { KeepWithNext = true } })));
                edit.SetParagraphStyle(point.Paragraph, id);
                edit.SetParagraphFormat(point.Paragraph, context.Rich.Document.CurrentSnapshot.Get<RichParagraph>(point.Paragraph).Format with { KeepWithNext = default });
            }), sameSelection: true);
            Check(context.Rich.Document.CurrentSnapshot.ResolveParagraph(point.Paragraph).KeepWithNext, "named style resolves");
            await context.CheckUndo(before, () => Equal(id, context.Rich.Document.CurrentSnapshot.Get<RichParagraph>(point.Paragraph).Style!.Value, "named style redo"));
        });
        yield return new("rich/selections/multiple-carets", ["multiple carets", "typing", "selection", "scroll", "undo"], async context =>
        {
            await context.OpenReference(); var first = context.Find("The text you are reading is aligned left", 4); var second = first with { Offset = first.Offset + 12 };
            await context.Select(first); var before = await context.SaveRich(); var original = context.Text;
            var projection = context.Rich.Document.CurrentSnapshot.ProjectText();
            var firstOffset = projection.GetOffset(first); var secondOffset = projection.GetOffset(second);
            context.Rich.Selection = new RichTextSelectionSet(context.Rich.Document.CurrentSnapshot, new[] { new RichTextSelectionItem(new(first, first)), new RichTextSelectionItem(new(second, second)) });
            await context.ObserveEdit("multiple rich carets", () => context.Type("XY"));
            Equal(original.Insert(secondOffset, "XY").Insert(firstOffset, "XY"), context.Text, "native input reaches both rich carets");
            await context.CheckUndo(before, () => Equal(2, ((RichTextSelectionSet)context.Rich.Selection).Count, "redo retains carets"));
        });
        yield return new("rich/clipboard/rtf-cut-paste", ["RTF clipboard", "selection", "caret", "undo", "resources"], async context =>
        {
            await context.OpenReference(); var point = context.Find("The text you are reading is aligned left", 4);
            await context.Select(point, 4); var original = context.Text; var before = await context.SaveRich();
            await context.Rich.CopyAsync(); await context.Select(point);
            var offset = context.Rich.Document.CurrentSnapshot.ProjectText().GetOffset(point);
            await context.Rich.PasteAsync(); await Settle(100); context.AssertNativeSelection();
            Equal(original.Insert(offset, original.Substring(offset, 4)), context.Text, "RTF clipboard inserts at caret");
            context.Rich.Undo(); await Settle(100);
            await context.Select(point, 4); await context.Rich.CutAsync(); await Settle(100); context.AssertNativeSelection();
            Equal(original.Remove(offset, 4), context.Text, "cut removes selected content");
            await context.CheckUndo(before, () => Equal(original.Remove(offset, 4), context.Text, "cut redo"));
        });
        foreach (var accept in new[] { false, true })
            yield return new("rich/revisions/" + (accept ? "accept" : "reject"), ["revision tracking", "original review", "final review", "undo", "RTF"], async context =>
            {
                await context.OpenReference(); var point = context.Find("The text you are reading is aligned left", 4);
                await context.Select(point, 4); var original = context.Text; var group = new RichRevisionInfo("Scenario editor");
                context.Rich.Session.RevisionTracking = group;
                await context.ObserveEdit("tracked replacement", () => context.Type("change"));
                context.Rich.Session.RevisionTracking = null;
                var changed = context.Text; var saved = await context.SaveRich();
                var review = new RichReviewView { Source = context.Rich.Document, Mode = RichReviewMode.Original };
                Equal(original, review.Projection.Snapshot.ProjectText().Text.ToString(), "original review retains deleted text");
                review.Mode = RichReviewMode.Final;
                Equal(changed, review.Projection.Snapshot.ProjectText().Text.ToString(), "final review includes new text");
                await context.ObserveEdit("resolve revision", () => context.Rich.Edit(edit => { if (accept) edit.AcceptRevision(group.Group); else edit.RejectRevision(group.Group); }), stableScroll: accept);
                Equal(accept ? changed : original, context.Text, "resolved review text");
                Check(context.Rich.Document.CurrentSnapshot.Revisions.IsEmpty, "revision resolved");
                await context.CheckUndo(saved, () => Check(context.Rich.Document.CurrentSnapshot.Revisions.IsEmpty, "resolution redo"));
            });
        yield return new("rich/presentation/decorations-and-widget", ["decorations", "widgets", "native child views", "stale publication", "selection", "history"], async context =>
        {
            await context.OpenReference(); var point = context.Find("The text you are reading is aligned left", 4);
            await context.Select(point, 4); var before = await context.SaveRich();
            using var layer = context.Rich.Presentation.CreateDecorationLayer();
            var stale = layer.BeginUpdate(); var current = layer.BeginUpdate();
            RichEditorDecoration[] items = [new(new(point, point with { Offset = point.Offset + 4 }), new() { ForegroundColor = Colors.Blue })];
            Check(!layer.TryPublish(stale, items), "stale rich decoration rejected");
            await context.ObserveEdit("rich decoration", () => Check(layer.TryPublish(current, items), "current decoration accepted"), sameSelection: true);
            View? realized = null;
            using var source = context.Rich.Presentation.CreateWidgetSource(_ => realized = new Label { Text = "Note", WidthRequest = 40, HeightRequest = 20 });
            Check(source.TryPublish(source.BeginUpdate(), new[] { new RichEditorWidget("note", point, new(40, 20)) }), "widget accepted");
            for (var i = 0; i < 100 && realized?.Handler is null; i++) await Task.Delay(20);
            Check(realized?.Handler is not null, "visible rich widget has a native view");
            var after = await context.SaveRich();
            Check(before.AsSpan().SequenceEqual(after), "presentation leaves RTF unchanged");
            Check(!context.Rich.CanUndo, "presentation creates no history"); context.AssertNativeSelection();
        });
        yield return new("rich/table/navigation", ["tables", "cell navigation", "caret", "selection", "history"], async context =>
        {
            await context.OpenReference(); var snapshot = context.Rich.Document.CurrentSnapshot; var table = snapshot.Nodes.Values.OfType<RichTable>().First();
            var grid = snapshot.GetTableGrid(table.Id); var first = snapshot.Get<RichTableCell>(grid[0, 0]).Blocks[0]; var second = snapshot.Get<RichTableCell>(grid[0, 1]).Blocks[0];
            await context.Select(new(snapshot.Body, first, 0));
            Check(context.Rich.MoveToAdjacentCell(), "next cell"); await Settle(100); context.AssertNativeSelection();
            static RichTextPoint Active(RichSelection selection) => selection is RichTextSelectionSet set ? set.Primary.Selection.Active : ((CoreSelection)selection).Active;
            Equal(second, Active(context.Rich.Selection).Paragraph, "next cell caret");
            Check(context.Rich.MoveToAdjacentCell(false), "previous cell"); await Settle(100); context.AssertNativeSelection();
            Equal(first, Active(context.Rich.Selection).Paragraph, "previous cell caret");
            Check(!context.Rich.CanUndo, "cell navigation creates no history");
        });
        yield return new("rich/document/saved-and-checked-edits", ["saved state", "checked transactions", "selection generation", "undo", "redo"], async context =>
        {
            await context.OpenReference(); var point = context.Find("The text you are reading is aligned left", 4); await context.Select(point);
            context.Rich.Document.MarkSaved(context.Rich.Document.CaptureSave()); var revision = context.Rich.Document.Revision; var generation = context.Rich.Session.SelectionGeneration;
            context.Type("x"); await Settle(80); Check(context.Rich.IsModified, "typing marks dirty");
            Check(!context.Rich.Edit(edit => edit.ReplaceText(point, 0, "stale"), expectedRevision: revision, expectedSelectionGeneration: generation).Accepted, "stale edit rejected");
            context.Rich.Undo(); await Settle(80); Check(!context.Rich.IsModified, "undo returns to save point");
            context.Rich.Redo(); await Settle(80); Check(context.Rich.IsModified, "redo leaves save point"); context.AssertNativeSelection();
        });
#if ANDROID || IOS || MACCATALYST
        yield return new("rich/input/native-composition", ["native IME protocol", "caret", "selection", "scroll", "undo"], async context =>
        {
            await context.OpenReference(); var point = context.Find("The text you are reading is aligned left", 4); await context.Select(point);
            var before = await context.SaveRich(); var original = context.Text; var offset = context.Rich.Document.CurrentSnapshot.ProjectText().GetOffset(point);
            await context.ObserveEdit("IME commit", () => context.Compose("かな", "仮名"));
            Equal(original.Insert(offset, "仮名"), context.Text, "marked text commits exactly once");
            await context.CheckUndo(before, () => Equal(original.Insert(offset, "仮名"), context.Text, "IME redo"));
        });
#endif
    }
}
#endif
