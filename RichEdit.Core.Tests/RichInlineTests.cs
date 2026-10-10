using Xunit;

namespace RichEdit.Core.Tests;

public sealed class RichInlineTests
{
    [Fact]
    public void FieldAtomsAndHyperlinkBoundariesSurviveEditsAndUndo()
    {
        var document = RichDocument.FromPlainText("ab");
        using var session = document.CreateSession();
        RichNodeId field = default;
        session.Edit(edit =>
        {
            edit.SetHyperlink(new(edit.Body, new(2), 0), 1, new("#target"));
            field = edit.InsertField(new(edit.Body, new(2), 2), "PAGE", "1");
            edit.SetSelection(new RichObjectSelection(field));
        });
        Assert.IsType<RichObjectSelection>(session.Selection);
        var runs = document.CurrentSnapshot.Nodes.Values.OfType<RichText>().ToArray();
        Assert.Equal(2, runs.Length);
        Assert.Single(runs, run => run.Link is not null);
        session.Edit(edit => edit.ReplaceText(new(edit.Body, new(2), 2), 1, "\uFFFC"));
        Assert.DoesNotContain(field, document.CurrentSnapshot.Nodes.Keys);
        Assert.Empty(document.CurrentSnapshot.ProjectText().Objects);
        session.Undo();
        Assert.Equal(new RichObjectSelection(field), session.Selection);
        Assert.Equal("1", document.CurrentSnapshot.Get<RichField>(field).Result);
    }

    [Fact]
    public void InvalidInlinePropertiesRejectTheEntireCandidate()
    {
        var document = RichDocument.FromPlainText("ab");
        var before = document.CurrentSnapshot;
        Assert.Throws<ArgumentException>(() => document.Edit(edit =>
        {
            edit.ReplaceText(new(edit.Body, new(2), 0), 0, "X");
            edit.SetHyperlink(new(edit.Body, new(2), 0), 1, new("bad\nlink"));
        }));
        Assert.Same(before, document.CurrentSnapshot);
        Assert.False(document.CanUndo);
        Assert.Throws<ArgumentException>(() => document.Edit(edit => edit.InsertField(new(edit.Body, new(2), 0), "PAGE", "a\nb")));
        Assert.Same(before, document.CurrentSnapshot);
    }
}
