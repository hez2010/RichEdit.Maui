using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls;

namespace RichEdit.Maui.Tests;

[Collection("Native editor")]
public class NativeTableContractTests
{
    [Fact]
    public Task NativeStructuralMapRetainsEmptyCellsLiteralMarkersAndFieldAddresses() => WindowsTestHost.RunAsync(async () =>
    {
        var snapshot = RichEdit.Core.RichDocumentSnapshot.Build(edit =>
        {
            var outer = edit.InsertTable(edit.Body, 1, 2, (RichEdit.Core.RichLength[])[new(2400), new(2400)]);
            var initial = edit.Snapshot;
            var first = initial.Get<RichEdit.Core.RichTableCell>(initial.GetTableGrid(outer)[0, 0]).Blocks[0];
            edit.ReplaceText(new(edit.Body, first, 0), 0, "literal\uFFF9\uFFFB\uFFFC");
            edit.InsertField(new(edit.Body, first, 10), "DATE", "2026");
            edit.SetHyperlink(new(edit.Body, first, 0), 7, new("https://example.test"));
            var cell = initial.GetTableGrid(outer)[1, 1];
            edit.InsertTable(cell, 1, 1, (RichEdit.Core.RichLength[])[new(900)]);
            edit.DeleteBlock(new(2));
        });
        var prepared = await StructuredRichNativeMap.PrepareAsync(snapshot);
        var native = new RichEditBox();
        native.Document.SetText(TextSetOptions.FormatRtf, prepared.Rtf);
        var story = native.Document.GetRange(0, 0);
        story.SetRange(0, story.StoryLength);
        var resolved = prepared.Resolve(story.Text);
        foreach (var removal in resolved.Removals.Reverse())
            native.Document.GetRange(removal.Start, removal.End).SetText(TextSetOptions.None, "");
        story.SetRange(0, story.StoryLength);
        Assert.Equal(resolved.Map.NativeText, story.Text);
        foreach (var paragraph in snapshot.Nodes.Values.OfType<RichEdit.Core.RichParagraph>())
            for (var offset = 0; offset <= snapshot.GetParagraphLength(paragraph.Id); offset++)
            {
                var point = new RichEdit.Core.RichTextPoint(snapshot.Body, paragraph.Id, offset);
                Assert.Equal(point, resolved.Map.ToRich(resolved.Map.ToNative(point)));
            }
        native.Document.GetText(TextGetOptions.FormatRtf, out var saved);
        Assert.Contains(@"\nestrow", saved);
        Assert.Contains(@"\trowd", saved);
        Assert.DoesNotContain("RichMap", story.Text);
    });

    [Fact]
    public Task StructuredNestedTablesRemainNativeTables() => WindowsTestHost.RunAsync(async () =>
    {
        var snapshot = RichEdit.Core.RichDocumentSnapshot.Build(edit =>
        {
            var outer = edit.InsertTable(edit.Body, 1, 1, (RichEdit.Core.RichLength[])[new(4000)]);
            var cell = edit.Snapshot.GetTableGrid(outer)[0, 0];
            var inner = edit.InsertTable(cell, 1, 1, (RichEdit.Core.RichLength[])[new(1500), new(1500)]);
            var current = edit.Snapshot;
            var paragraph = current.Get<RichEdit.Core.RichTableCell>(current.GetTableGrid(inner)[0, 0]).Blocks[0];
            edit.ReplaceText(new(edit.Body, paragraph, 0), 0, "inside");
            edit.DeleteBlock(new(2));
        });
        var native = new RichEditBox();
        native.Document.SetText(TextSetOptions.FormatRtf, await StructuredRichProjection.CreateRtfAsync(snapshot));
        native.Document.GetText(TextGetOptions.None, out var text);
        Assert.Contains("inside", text);
        native.Document.GetText(TextGetOptions.FormatRtf, out var saved);
        Assert.Contains(@"\nestrow", saved);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task StructuredModelTablesProjectIntoTheNativeEngineWithCharacterFormatting(bool persistent)
    {
        return WindowsTestHost.RunAsync(async () =>
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
                        var point = new RichEdit.Core.RichTextPoint(edit.Body, paragraph, 0);
                        var text = labels[row * 2 + column];
                        edit.ReplaceText(point, 0, text);
                        if (column == 1) edit.SetCharacterFormat(point, text.Length, new() { Bold = true, Foreground = new RichEdit.Core.RichColor(255, 0, 0) });
                    }
                edit.DeleteBlock(new(2));
        });
        var native = new RichEditBox();
        string rtf;
        if (persistent)
        {
            using var stream = new MemoryStream();
            await RichEdit.Core.Rtf.RtfCodec.WriteAsync(snapshot, stream);
            rtf = System.Text.Encoding.ASCII.GetString(stream.ToArray());
        }
        else rtf = await StructuredRichProjection.CreateRtfAsync(snapshot);
        native.Document.SetText(TextSetOptions.FormatRtf, rtf);
        native.Document.GetText(TextGetOptions.None, out var text);
        Assert.Contains("alpha", text);
        Assert.Contains("beta", text);
        Assert.Contains("gamma", text);
        Assert.Contains("delta", text);
        var start = text.IndexOf("beta", StringComparison.Ordinal);
        var format = native.Document.GetRange(start, start + 4).CharacterFormat;
        Assert.Equal(FormatEffect.On, format.Bold);
        Assert.Equal(Windows.UI.Color.FromArgb(255, 255, 0, 0), format.ForegroundColor);
        native.Document.GetText(TextGetOptions.FormatRtf, out var saved);
        Assert.Equal(2, saved.Split(@"\trowd", StringSplitOptions.None).Length - 1);
        });
    }

    private const string TableRtf = @"{\rtf1\ansi\deff0{\fonttbl{\f0 Arial;}}\fs24
\trowd\trgaph108\trleft0\cellx1440\cellx2880
\intbl alpha\cell beta\cell\row
\trowd\trgaph108\trleft0\cellx1440\cellx2880
\intbl gamma\cell delta\cell\row
\pard\par}";

    [Fact]
    public Task NativeRichEditRetainsTableStructureAcrossCellTypingAndUndo()
    {
        var output = TestContext.Current.TestOutputHelper!;
        return WindowsTestHost.RunAsync(() =>
        {
            var native = new RichEditBox();
            native.Document.SetText(TextSetOptions.FormatRtf, TableRtf);
            native.Document.ClearUndoRedoHistory();
            native.Document.GetText(TextGetOptions.None, out var before);
            output.WriteLine($"Native table text: {Escape(before)}");
            output.WriteLine($"Native text units: {string.Join(", ", Enum.GetNames<TextRangeUnit>())}");
            Assert.Contains("alpha", before);
            Assert.Contains("beta", before);
            Assert.Contains("gamma", before);
            Assert.Contains("delta", before);
            var position = before.IndexOf("beta", StringComparison.Ordinal);
            native.Document.Selection.SetRange(position, position + 4);
            native.Document.Selection.SetText(TextSetOptions.None, "BETA");
            native.Document.GetText(TextGetOptions.None, out var after);
            Assert.Equal(before.Replace("beta", "BETA", StringComparison.Ordinal), after);
            native.Document.GetText(TextGetOptions.FormatRtf, out var saved);
            Assert.Contains(@"\trowd", saved);
            Assert.Contains(@"\cellx", saved);
            Assert.Contains(@"\cell", saved);
            Assert.True(native.Document.CanUndo());
            native.Document.Undo();
            native.Document.GetText(TextGetOptions.None, out var restored);
            Assert.Equal(before, restored);
        });
    }

    [Fact]
    public Task NativeTableEndIncludesAnAddressableFinalParagraph()
    {
        var output = TestContext.Current.TestOutputHelper!;
        return WindowsTestHost.RunAsync(() =>
        {
            var native = new RichEditBox();
            native.Document.SetText(TextSetOptions.FormatRtf, TableRtf.Replace(@"\pard\par", "", StringComparison.Ordinal));
            native.Document.GetText(TextGetOptions.None, out var before);
            output.WriteLine($"Table ending without explicit final paragraph: {Escape(before)}");
            native.Document.Selection.EndKey(TextRangeUnit.Story, false);
            output.WriteLine($"Native end caret: {native.Document.Selection.StartPosition}");
            native.Document.Selection.TypeText("tail");
            native.Document.GetText(TextGetOptions.None, out var after);
            native.Document.GetText(TextGetOptions.FormatRtf, out var rtf);
            output.WriteLine($"Typed at native end: {Escape(after)}");
            Assert.Equal(before[..^1] + "tail\r", after);
            Assert.Contains(@"\trowd", rtf);
        });
    }

    private static string Escape(string value) => string.Concat(value.Select(character => char.IsControl(character)
        ? $"\\u{(int)character:x4}" : character.ToString()));
}
