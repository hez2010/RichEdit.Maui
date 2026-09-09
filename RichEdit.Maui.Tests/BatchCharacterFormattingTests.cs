using Microsoft.Maui.Graphics;

namespace RichEdit.Maui.Tests;

public class BatchCharacterFormattingTests
{
    [Fact]
    public void BatchMatchesSequentialFormattingAcrossRunsAndGaps()
    {
        var document = RichTextDocument.FromPlainText("abcdefghijkl\nmnopqrst");
        document.Edit(edit =>
        {
            edit.SetCharacterFormat(new RichTextRange(3, 6), new() { Italic = true });
            edit.SetParagraphFormat(new RichTextRange(0, 1), new() { Alignment = RichTextAlignment.Center });
            edit.SetLink(new RichTextRange(15, 3), "https://example.com");
            edit.SetMetadata("source", "test");
        });
        var before = document.CurrentSnapshot;
        RichTextRun[] replacements =
        [
            new(new RichTextRange(1, 2), new() { ForegroundColor = Colors.Red }),
            new(new RichTextRange(3, 4), new() { ForegroundColor = Colors.Red }),
            new(new RichTextRange(8, 7), new() { FontWeight = 700 }),
            new(new RichTextRange(18, 3), new() { BackgroundColor = Colors.Yellow }),
        ];
        var expected = before;
        foreach (var replacement in replacements)
            expected = expected.ApplyCharacterFormat(replacement.Range.ToRange(), _ => replacement.Format);
        var events = 0;
        document.Changed += (_, _) => events++;

        var changes = document.Edit(edit => edit.SetCharacterFormats(replacements));

        Assert.Equal(1, events);
        Assert.True(expected.ContentEquals(document.CurrentSnapshot));
        Assert.Equal(replacements.Select(run => run.Range), changes.Changes.Select(change => change.NewRange));
        Assert.All(changes.Changes, change => Assert.Equal(RichTextChangeKind.CharacterFormat, change.Kind));
        document.Undo();
        Assert.True(before.ContentEquals(document.CurrentSnapshot));
        document.Redo();
        Assert.True(expected.ContentEquals(document.CurrentSnapshot));
    }

    [Fact]
    public void BatchObservesEarlierEditsAndLaterOperationsObserveTheBatch()
    {
        var document = RichTextDocument.FromPlainText("ab");
        document.Edit(edit =>
        {
            edit.InsertText(2, "cd");
            edit.SetCharacterFormats((RichTextRun[])[new(new RichTextRange(1, 3), new() { Italic = true })]);
            edit.UpdateCharacterFormat(new RichTextRange(2, 2), format => format with { FontWeight = 700 });
        });
        Assert.False(document.CurrentSnapshot.GetCharacterFormat(0).Italic);
        Assert.True(document.CurrentSnapshot.GetCharacterFormat(1).Italic);
        Assert.True(document.CurrentSnapshot.GetCharacterFormat(2).Italic);
        Assert.True(document.CurrentSnapshot.GetCharacterFormat(2).Bold);
        document.Undo();
        Assert.Equal("ab", document.Text);
        Assert.False(document.CanUndo);
    }

    [Fact]
    public void InvalidBatchDoesNotApplyAnyRanges()
    {
        var format = new RichTextCharacterFormat { Italic = true };
        RichTextRun[][] invalidBatches =
        [
            [new(new RichTextRange(0, 2), format), new(new RichTextRange(1, 2), format)],
            [new(new RichTextRange(3, 1), format), new(new RichTextRange(1, 1), format)],
            [new(new RichTextRange(0, 1), format), new(new RichTextRange(4, 1), format)],
            [new(new RichTextRange(0, 1), format), new(new RichTextRange(2, 0), format)],
            [new(new RichTextRange(0, 1), format), new(new RichTextRange(2, 1), new() { FontSize = -1 })],
            [new(new RichTextRange(0, 1), format), null!],
        ];
        foreach (var batch in invalidBatches)
        {
            var document = RichTextDocument.FromPlainText("text");
            var before = document.CurrentSnapshot;
            var changes = document.Edit(edit => Assert.ThrowsAny<ArgumentException>(() => edit.SetCharacterFormats(batch)));
            Assert.True(changes.IsEmpty);
            Assert.Same(before, document.CurrentSnapshot);
            Assert.False(document.CanUndo);
        }
    }

    [Fact]
    public void EmptyAndUnchangedBatchesAreNoOps()
    {
        var document = RichTextDocument.FromPlainText("text");
        var before = document.CurrentSnapshot;
        Assert.True(document.Edit(edit => edit.SetCharacterFormats([])).IsEmpty);
        Assert.True(document.Edit(edit => edit.SetCharacterFormats(before.Runs)).IsEmpty);
        Assert.Same(before, document.CurrentSnapshot);
        Assert.False(document.CanUndo);
        Assert.True(new RichTextDocument().Edit(edit => edit.SetCharacterFormats([])).IsEmpty);
    }

    [Fact]
    public void ThousandsOfRangesDoNotRebuildTheDocumentForEachRange()
    {
        var document = RichTextDocument.FromPlainText(string.Concat(Enumerable.Repeat("return 42;\n", 2000)));
        var format = new RichTextCharacterFormat { ForegroundColor = Colors.Blue };
        var runs = Enumerable.Range(0, 2000).Select(index => new RichTextRun(new RichTextRange(index * 11, 6), format)).ToArray();
        var before = GC.GetAllocatedBytesForCurrentThread();

        document.Edit(edit => edit.SetCharacterFormats(runs));

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 20_000_000, $"Formatting 2,000 ranges allocated {allocated:N0} bytes.");
        Assert.Equal(4000, document.CurrentSnapshot.Runs.Length);
        Assert.Equal(format, document.CurrentSnapshot.GetCharacterFormat(1999 * 11));
        Assert.Null(document.CurrentSnapshot.GetCharacterFormat(document.Length - 1).ForegroundColor);
    }
}
