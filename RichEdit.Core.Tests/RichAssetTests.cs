using Xunit;

namespace RichEdit.Core.Tests;

public class RichAssetTests
{
    [Fact]
    public void ImageResourcesOwnTheirBytesAndObjectSelectionSharesParentHistory()
    {
        var document = RichDocument.FromPlainText("ab");
        var paragraph = document.CurrentSnapshot.Get<RichStory>(document.CurrentSnapshot.Body).Blocks[0];
        using var view = document.CreateSession();
        var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR4nGNgAAIAAAUAAXpeqz8AAAAASUVORK5CYII=");
        var original = bytes.ToArray();
        RichAssetId asset = default;
        RichNodeId image = default;
        view.Edit(edit =>
        {
            asset = edit.AddImageAsset(RichImageKind.Png, bytes);
            image = edit.InsertImage(new(edit.Body, paragraph, 1), asset, RichLength.FromPoints(24), RichLength.FromPoints(16), "Example image");
            edit.SetSelection(new RichObjectSelection(image));
        });
        bytes[0] = 0;
        Assert.Equal(original, document.CurrentSnapshot.Assets[asset].Data);
        var projection = document.CurrentSnapshot.ProjectText();
        Assert.Equal("a\uFFFCb", projection.Text.ToString());
        Assert.Equal(image, Assert.Single(projection.Objects).Object);
        Assert.Equal(new RichObjectSelection(image), view.Selection);
        var retained = document.CurrentSnapshot;
        view.Undo();
        Assert.Equal("ab", document.CurrentSnapshot.ProjectText().Text.ToString());
        Assert.Empty(document.CurrentSnapshot.Assets);
        view.Redo();
        Assert.Equal(new RichObjectSelection(image), view.Selection);
        Assert.Same(retained.Assets[asset], document.CurrentSnapshot.Assets[asset]);
    }

    [Fact]
    public void LiteralObjectCharactersRemainDistinctFromAuthoredImages()
    {
        var document = RichDocument.FromPlainText("\uFFFC");
        var paragraph = document.CurrentSnapshot.Get<RichStory>(document.CurrentSnapshot.Body).Blocks[0];
        RichNodeId image = default;
        document.Edit(edit =>
        {
            var asset = edit.AddImageAsset(RichImageKind.Png, new byte[] { 1, 2, 3 });
            image = edit.InsertImage(new(edit.Body, paragraph, 1), asset, new(100), new(100));
        });
        Assert.Equal("\uFFFC\uFFFC", document.CurrentSnapshot.ProjectText().Text.ToString());
        Assert.Single(document.CurrentSnapshot.ProjectText().Objects);
        document.Edit(edit => edit.ReplaceText(new(edit.Body, paragraph, 1), 1, "\uFFFC"));
        Assert.Empty(document.CurrentSnapshot.ProjectText().Objects);
        Assert.DoesNotContain(image, document.CurrentSnapshot.Nodes.Keys);
        document.Undo();
        Assert.Single(document.CurrentSnapshot.ProjectText().Objects);
    }

    [Fact]
    public void RemovingAReferencedImageResourceRejectsTheCompleteTransaction()
    {
        var document = new RichDocument();
        RichAssetId asset = default;
        document.Edit(edit =>
        {
            asset = edit.AddImageAsset(RichImageKind.Jpeg, new byte[] { 1, 2, 3 });
            edit.InsertImage(new(edit.Body, new(2), 0), asset, new(200), new(300));
        });
        var before = document.CurrentSnapshot;
        Assert.Throws<ArgumentException>(() => document.Edit(edit => edit.RemoveImageAsset(asset)));
        Assert.Same(before, document.CurrentSnapshot);
        Assert.Throws<ArgumentException>(() => document.Edit(edit => edit.InsertImage(new(edit.Body, new(2), 0), asset, new(0), new(100))));
        Assert.Same(before, document.CurrentSnapshot);
    }
}
