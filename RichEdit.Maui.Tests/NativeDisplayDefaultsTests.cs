using TextEdit.Core;
using Xunit;

namespace RichEdit.Maui.Tests;

public sealed class NativeDisplayDefaultsTests
{
    [Fact]
    public void SourceDisplayDefaultsSurviveEditsWithoutNormalizingLineEndings()
    {
        var defaults = new RichTextCharacterFormat { FontFamily = "Times New Roman", FontSize = 12 };
        var display = RichTextDocumentSnapshot.CreateSourceProjection(TextSnapshot.FromText("a\r\nb"), defaults);
        var replaced = display.Replace(0..1, "c");
        var batch = replaced.ReplaceTextBatch(new RichTextReplacement[] { new(new(3, 1), "d", RichTextCharacterFormat.Default) });
        Assert.Equal("a\r\nb", display.Text);
        Assert.Equal("c\r\nb", replaced.Text);
        Assert.Equal("c\r\nd", batch.Text);
        Assert.Equal(defaults, display.DefaultCharacterFormat);
        Assert.Equal(defaults, replaced.DefaultCharacterFormat);
        Assert.Equal(defaults, batch.DefaultCharacterFormat);
    }
}
