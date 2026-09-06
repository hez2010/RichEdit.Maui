namespace RichEdit.Maui.Tests;

public class EditingRoundTripTests
{
    [Fact]
    public void NativeProjectionCompletionMergesOnlyWithItsExactDocumentVersion()
    {
        var document = new RichTextDocument();
        var source = new object();
        document.Edit(edit => edit.InsertText(0, "onetwo"));
        document.ReplaceSnapshotFromNative(document.CurrentSnapshot.RemapText("one two"), source, false, projectedVersion: document.Version);
        document.Undo();
        Assert.Empty(document.Text);
        document.Redo();
        Assert.Equal("one two", document.Text);

        var staleVersion = document.Version;
        document.Edit(edit => edit.InsertText(document.Length, "!"));
        document.ReplaceSnapshotFromNative(document.CurrentSnapshot.RemapText("one two! "), source, false, projectedVersion: staleVersion);
        document.Undo();
        Assert.Equal("one two!", document.Text);
    }

    [Fact]
    public void NativeFollowupMergesIntoTypingButNotAnInterveningApplicationEdit()
    {
        var document = new RichTextDocument();
        var source = new object();
        document.ReplaceSnapshotFromNative(RichTextDocumentSnapshot.FromPlainText("word"), source, false);
        document.ReplaceSnapshotFromNative(document.CurrentSnapshot.RemapText("word "), source, false, mergeWithPrevious: true);
        document.Undo();
        Assert.Empty(document.Text);
        document.Redo();
        document.Edit(edit => edit.SetMetadata("author", "test"));
        document.ReplaceSnapshotFromNative(document.CurrentSnapshot.RemapText("corrected "), source, false, mergeWithPrevious: true);
        document.Undo();
        Assert.Equal("word ", document.Text);
        Assert.Equal("test", document.CurrentSnapshot.Metadata["author"]);
        document.Undo();
        Assert.Empty(document.CurrentSnapshot.Metadata);
    }

    [Theory]
    [InlineData(0, "\n")]
    [InlineData(3, "\n")]
    [InlineData(3, "\ntwo")]
    public void SplittingAListItemDoesNotDuplicateItsRestart(int position, string text)
    {
        var document = new RichTextDocument();
        document.Edit(edit =>
        {
            edit.InsertText(0, "one");
            var list = edit.CreateList(new RichTextListDefinition([
                new RichTextListLevelDefinition
                {
                    Marker = new RichTextListMarker.Number(RichTextListNumberStyle.Arabic, 1),
                    Prefix = "", Suffix = ".", LeadingIndent = 24, FirstLineIndent = -12, MarkerTab = 24,
                }]));
            edit.ApplyList(new RichTextRange(0, 3), list);
            edit.RestartList(new RichTextRange(0, 3), 7);
        });
        document.Edit(edit => edit.InsertText(position, text));
        Assert.Equal(7, document.CurrentSnapshot.Paragraphs[0].Format.List!.RestartAt);
        Assert.Null(document.CurrentSnapshot.Paragraphs[1].Format.List!.RestartAt);
    }
    [Theory]
    [InlineData("one\ntwo")]
    [InlineData("\n")]
    [InlineData("\n\n")]
    [InlineData("one\n")]
    [InlineData("one\u2028two")]
    public void MultilineFieldRemainsOneEditableFieldAfterRtfRoundTrip(string result)
    {
        var document = new RichTextDocument();
        document.Edit(edit =>
        {
            edit.InsertText(0, "prefix suffix");
            edit.InsertField(7, "MERGEFIELD value", result);
            edit.UpdateParagraphFormat(new RichTextRange(0, edit.Snapshot.Length), format => format with { Alignment = RichTextAlignment.Center });
        });
        var restored = RichTextDocument.FromRtf(document.RtfText);
        Assert.Equal(document.Text, restored.Text);
        Assert.All(restored.CurrentSnapshot.Paragraphs, paragraph => Assert.Equal(RichTextAlignment.Center, paragraph.Format.Alignment));
        var field = Assert.Single(restored.CurrentSnapshot.Fields);
        Assert.Equal(new RichTextRange(7, result.Length), field.Range);
        restored.Edit(edit => edit.UpdateField(field.Id, "MERGEFIELD replacement", "new"));
        Assert.Equal("prefix newsuffix", restored.Text);
    }

    [Fact]
    public void NativeUndoRestoresTheSnapshotWithoutEchoingToItsSource()
    {
        var document = new RichTextDocument();
        document.Edit(edit => edit.InsertField(0, "DATE", "today"));
        var before = document.CurrentSnapshot;
        var token = new object();
        document.ReplaceSnapshotFromNative(new RichTextDocumentSnapshot(""), token, nativeUndoOwned: true);
        var change = document.RestoreSnapshotFromNativeUndo(before, RichTextChangeOrigin.Undo, token);
        Assert.True(before.ContentEquals(document.CurrentSnapshot));
        Assert.Same(token, change.SourceToken);
        Assert.False(document.CanUndo);
    }

    [Fact]
    public void BreakingTypingGroupKeepsSeparateUndoUnits()
    {
        var document = new RichTextDocument();
        var token = new object();
        document.ReplaceSnapshotFromNative(new RichTextDocumentSnapshot("a"), token, nativeUndoOwned: false);
        document.BreakUndoGroup();
        document.ReplaceSnapshotFromNative(new RichTextDocumentSnapshot("ab"), token, nativeUndoOwned: false);
        document.Undo();
        Assert.Equal("a", document.Text);
        document.Undo();
        Assert.Empty(document.Text);
    }

    [Fact]
    public void MixedTransactionsReplayTextAndRestoreEverySnapshot()
    {
        var random = new Random(7321);
        var document = new RichTextDocument();
        var snapshots = new List<RichTextDocumentSnapshot> { document.CurrentSnapshot };
        string[] insertions = ["a", "漢字", "\n", "\u2028", "\t", "😀", ""];
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var before = document.Text;
            var start = random.Next(document.Length + 1);
            var length = random.Next(document.Length - start + 1);
            var change = document.Edit(edit =>
            {
                edit.ReplaceText(new RichTextRange(start, length), insertions[random.Next(insertions.Length)]);
                if (edit.Snapshot.Length > 0)
                {
                    var position = random.Next(edit.Snapshot.Length);
                    edit.UpdateCharacterFormat(new RichTextRange(position, 1), format => format with { Italic = !format.Italic });
                }
                edit.SetMetadata("iteration", iteration.ToString());
            });
            foreach (var textChange in change.Changes.OfType<RichTextTextChange>())
            {
                before = before[..textChange.OldRange.Start] + textChange.InsertedText + before[textChange.OldRange.End..];
            }
            Assert.Equal(document.Text, before);
            Assert.Equal(document.Text, RichTextDocument.FromRtf(document.RtfText).Text);
            snapshots.Add(document.CurrentSnapshot);
        }

        for (var index = snapshots.Count - 2; index >= 0; index--)
        {
            document.Undo();
            Assert.True(snapshots[index].ContentEquals(document.CurrentSnapshot));
        }
        Assert.False(document.CanUndo);
        for (var index = 1; index < snapshots.Count; index++)
        {
            document.Redo();
            Assert.True(snapshots[index].ContentEquals(document.CurrentSnapshot));
        }
        Assert.False(document.CanRedo);
    }
}
