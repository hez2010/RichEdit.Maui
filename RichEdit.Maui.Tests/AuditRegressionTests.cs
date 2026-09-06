using Microsoft.Maui.Graphics;

namespace RichEdit.Maui.Tests;

public sealed class AuditRegressionTests
{
    [Fact]
    public void CopyingPartOfALaterParagraphPreservesItsFormattingAndList()
    {
        var document = new RichTextDocument();
        document.Edit(edit =>
        {
            edit.InsertText(0, "first\nsecond");
            var listId = edit.CreateList(new RichTextListDefinition(
            [
                new RichTextListLevelDefinition
                {
                    Marker = new RichTextListMarker.Bullet("*"),
                    Prefix = string.Empty,
                    Suffix = string.Empty,
                    LeadingIndent = 24,
                    FirstLineIndent = -12,
                    MarkerTab = 24,
                },
            ]));
            edit.ApplyList(new RichTextRange(6, 6), listId);
            edit.UpdateParagraphFormat(new RichTextRange(6, 6), format => format with
            {
                Alignment = RichTextAlignment.Center,
            });
        });

        var fragment = RichTextDocumentFragment.FromRange(document.CurrentSnapshot, new RichTextRange(7, 3));

        Assert.Equal("eco", fragment.Text);
        Assert.Equal(document.CurrentSnapshot.GetParagraphFormat(7), fragment.Snapshot.GetParagraphFormat(0));
        Assert.Single(fragment.Snapshot.Lists);
    }

    [Fact]
    public void RichFragmentPreservesSourceDocumentFontDefaults()
    {
        var source = new RichTextDocument();
        source.Edit(edit =>
        {
            edit.SetDefaultCharacterFormat(RichTextCharacterFormat.Default with
            {
                FontFamily = "Georgia",
                FontSize = 24,
                ForegroundColor = Colors.Red,
            });
            edit.InsertText(0, "source");
        });
        var target = new RichTextDocument();
        target.Edit(edit =>
        {
            edit.SetDefaultCharacterFormat(RichTextCharacterFormat.Default with { FontSize = 12 });
            edit.InsertText(0, "target");
        });

        var fragment = RichTextDocumentFragment.FromRange(source.CurrentSnapshot, new RichTextRange(0, source.Length));
        target.Edit(edit => edit.ReplaceFragment(new RichTextRange(3, 0), fragment));

        var format = target.CurrentSnapshot.ResolveCharacterFormat(target.CurrentSnapshot.GetCharacterFormat(3));
        Assert.Equal("Georgia", format.FontFamily);
        Assert.Equal(24, format.FontSize);
        Assert.Equal(Colors.Red, format.ForegroundColor);
        Assert.Equal(12, target.DefaultCharacterFormat.FontSize);
        Assert.Null(target.CurrentSnapshot.GetCharacterFormat(0).FontSize);
    }

    [Fact]
    public void NativeFormattingDeltaIncludesChangesInsideMergedRuns()
    {
        var before = new RichTextDocumentSnapshot("abcd", runs:
        [
            new RichTextRun(0, 1, RichTextCharacterFormat.Default),
            new RichTextRun(1, 1, RichTextCharacterFormat.Default with { FontWeight = 700 }),
            new RichTextRun(2, 2, RichTextCharacterFormat.Default with { Italic = true }),
        ]);
        var after = before.ApplyCharacterFormat(1..2, _ => RichTextCharacterFormat.Default)
            .ApplyCharacterFormat(2..4, _ => RichTextCharacterFormat.Default with { Underline = RichTextUnderlineStyle.Single });
        var document = new RichTextDocument(before);

        var changes = document.ReplaceSnapshotFromNative(after, new object(), nativeUndoOwned: true);

        var formatting = Assert.Single(changes.Changes, change => change.Kind == RichTextChangeKind.CharacterFormat);
        Assert.Equal(new RichTextRange(1, 3), formatting.NewRange);
    }

    [Fact]
    public void NativeTextDeltaAlsoIncludesFormattingOutsideTheReplacedText()
    {
        var before = new RichTextDocumentSnapshot("first\nsecond");
        var after = before.Replace(0..0, "x")
            .ApplyCharacterFormat(7..13, format => format with { FontWeight = 700 })
            .ApplyParagraphFormat(7..13, format => format with { Alignment = RichTextAlignment.Center });
        var document = new RichTextDocument(before);

        var changes = document.ReplaceSnapshotFromNative(after, new object(), nativeUndoOwned: true);

        var characterChange = Assert.Single(changes.Changes, change => change.Kind == RichTextChangeKind.CharacterFormat);
        var paragraphChange = Assert.Single(changes.Changes, change => change.Kind == RichTextChangeKind.ParagraphFormat);
        Assert.Equal(13, characterChange.NewRange.End);
        Assert.Equal(13, paragraphChange.NewRange.End);
    }

    [Fact]
    public void PlainTextFragmentUsesTheDestinationCaretAndParagraphFormats()
    {
        var document = new RichTextDocument();
        document.Edit(edit =>
        {
            edit.InsertText(0, "before after", RichTextCharacterFormat.Default with { FontWeight = 700, FontSize = 20 });
            edit.UpdateParagraphFormat(RichTextRange.Empty, format => format with { Alignment = RichTextAlignment.Center });
        });

        document.Edit(edit => edit.ReplaceFragment(new RichTextRange(7, 0), RichTextDocumentFragment.FromPlainText("plain")));

        Assert.Equal("before plainafter", document.Text);
        var format = document.CurrentSnapshot.GetCharacterFormat(7);
        Assert.True(format.Bold);
        Assert.Equal(20, format.FontSize);
        Assert.Equal(RichTextAlignment.Center, document.CurrentSnapshot.GetParagraphFormat(7).Alignment);
    }

    [Fact]
    public void EmptyFieldCanShareTheStartOfANonemptyField()
    {
        var document = new RichTextDocument();
        document.Edit(edit => edit.InsertField(0, "DATE", "today"));

        document.Edit(edit => edit.InsertField(0, "PAGE", string.Empty));

        Assert.Equal(2, document.CurrentSnapshot.Fields.Length);
        var restored = RichTextDocument.FromRtf(document.RtfText);
        Assert.Equal(document.Text, restored.Text);
        Assert.Equal(2, restored.CurrentSnapshot.Fields.Length);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("one", "two")]
    public void DistinctFieldsWithTheSameInstructionRemainIndividuallyEditable(string firstResult, string secondResult)
    {
        var document = new RichTextDocument();
        document.Edit(edit =>
        {
            edit.InsertField(0, "MERGEFIELD Name", firstResult);
            edit.InsertField(firstResult.Length, "MERGEFIELD Name", secondResult);
        });

        var restored = RichTextDocument.FromRtf(document.RtfText);

        Assert.Equal(2, restored.CurrentSnapshot.Fields.Length);
        var second = restored.CurrentSnapshot.Fields[1];
        restored.Edit(edit => edit.UpdateField(second.Id, second.Instruction, "changed"));
        Assert.Equal(firstResult + "changed", restored.Text);
    }

    [Theory]
    [InlineData("", 0, 0, "insert")]
    [InlineData("abcdef", 0, 6, "")]
    [InlineData("abcdef", 0, 2, "long prefix")]
    [InlineData("abcdef", 2, 2, "X")]
    [InlineData("abcdef", 6, 0, "\nend")]
    [InlineData("a\nb\nc", 1, 3, "")]
    public void TextDeltasReplayAndKeepFormatRangesWithinBothSnapshots(string text, int start, int length, string replacement)
    {
        var before = new RichTextDocumentSnapshot(text);
        var after = before.Replace(start..(start + length), replacement);
        if (after.Length > 0)
        {
            after = after.ApplyCharacterFormat((after.Length - 1)..after.Length, format => format with { Italic = true });
        }
        var document = new RichTextDocument(before);

        var changes = document.ReplaceSnapshotFromNative(after, new object(), nativeUndoOwned: true);

        var replayedText = before.Text;
        foreach (var change in changes.Changes)
        {
            Assert.InRange(change.OldRange.End, 0, before.Length);
            Assert.InRange(change.NewRange.End, 0, after.Length);
            if (change is RichTextTextChange textChange)
            {
                replayedText = replayedText.Remove(textChange.OldRange.Start, textChange.RemovedLength)
                    .Insert(textChange.OldRange.Start, textChange.InsertedText);
            }
        }
        Assert.Equal(after.Text, replayedText);
        Assert.Equal(after.Length, changes.GetAffectedRange(after.Length).End);
    }
}
