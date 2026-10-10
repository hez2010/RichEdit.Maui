using TextEdit.Core;
using Xunit;

namespace TextEdit.Core.Tests;

public sealed class VirtualSpaceTests
{
    [Fact]
    public void CompositionMirrorsWithIndependentPaddingAtCommitOnly()
    {
        var document = new TextDocument("a\nb");
        using var session = document.CreateSession();
        session.Selections = new((TextSelection[])[new(1, 1) { AnchorVirtualSpaces = 3, ActiveVirtualSpaces = 3 }, new(3, 3) { AnchorVirtualSpaces = 1, ActiveVirtualSpaces = 1 }]);
        var before = session.Selections;
        using var composition = session.BeginComposition();
        composition.Update(edit => edit.Insert(1, "日本"), new(3, 3));
        Assert.Equal("a\nb", document.Text);
        Assert.True(composition.Commit().Accepted);
        Assert.Equal("a   日本\nb 日本", document.Text);
        session.Undo();
        Assert.Equal(before.ToArray(), session.Selections.ToArray());
    }

    [Fact]
    public void PaddingMaterializesOnlyInTheReplacementTransactionAndUndoRestoresVirtualEndpoints()
    {
        var document = new TextDocument("a\r\nb");
        using var session = document.CreateSession();
        var selections = new TextSelectionSet((TextSelection[])
        [new(1, 1) { AnchorVirtualSpaces = 3, ActiveVirtualSpaces = 3 }, new(4, 4) { AnchorVirtualSpaces = 1, ActiveVirtualSpaces = 4 }]);
        session.Selections = selections;
        Assert.Equal("a\r\nb", document.Text);
        Assert.False(document.CanUndo);
        Assert.True(session.ReplaceSelections("X").Accepted);
        Assert.Equal("a   X\r\nb X", document.Text);
        Assert.All(session.Selections, item => Assert.Equal(0, item.ActiveVirtualSpaces));
        session.Undo();
        Assert.Equal("a\r\nb", document.Text);
        Assert.Equal(selections.ToArray(), session.Selections.ToArray());
        Assert.False(document.CanUndo);
        session.ReplaceSelections("");
        Assert.Equal("a\r\nb", document.Text);
        Assert.False(document.CanUndo);
        Assert.Equal(1, session.Selections[1].ActiveVirtualSpaces);
    }

    [Fact]
    public void VirtualEndpointsRequireLogicalLineEndsAndNormalizeAtOneSourceAddress()
    {
        var document = new TextDocument("abc\r\ndef");
        using var session = document.CreateSession();
        Assert.Throws<ArgumentException>(() => session.Selections = new((TextSelection[])[new(1, 1) { ActiveVirtualSpaces = 2 }]));
        var primary = new TextSelection(3, 3) { AnchorVirtualSpaces = 8, ActiveVirtualSpaces = 5 };
        session.Selections = new((TextSelection[])[new(3, 3) { AnchorVirtualSpaces = 2, ActiveVirtualSpaces = 3 }, primary], 1);
        Assert.Single(session.Selections);
        Assert.Equal(primary.Id, session.Selections.Primary.Id);
        Assert.Equal(8, session.Selections.Primary.AnchorVirtualSpaces);
        Assert.Equal(2, session.Selections.Primary.ActiveVirtualSpaces);
        session.ReplaceSelections("!");
        Assert.Equal("abc  !\r\ndef", document.Text);
    }

    [Fact]
    public void OtherViewEditsRetainVirtualSpaceWhileItsMappedEndpointIsStillALineEnd()
    {
        var document = new TextDocument("a\nb");
        using var session = document.CreateSession();
        session.Selections = new((TextSelection[])[new(1, 1) { AnchorVirtualSpaces = 4, ActiveVirtualSpaces = 4 }]);
        document.Edit(edit => edit.Insert(3, "!"));
        Assert.Equal(4, session.Selections.Primary.ActiveVirtualSpaces);
        document.Edit(edit => edit.Replace(new(1, 1), ""));
        Assert.Equal(0, session.Selections.Primary.ActiveVirtualSpaces);
    }
}
