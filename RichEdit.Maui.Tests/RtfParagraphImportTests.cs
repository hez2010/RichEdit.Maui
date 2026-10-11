using System.Text;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls;
using RichEdit.Core;

namespace RichEdit.Maui.Tests;

public sealed class RtfParagraphImportTests
{
    [Fact]
    public Task FinalParagraphPropertiesSurviveIndependentNativeRtfImport() => WindowsTestHost.RunAsync(async () =>
    {
        var snapshot = RichDocumentSnapshot.Build(edit =>
        {
            edit.ReplaceText(new(edit.Body, new(2), 0), 0, "last paragraph");
            edit.SetParagraphFormat(new(2), new() { Alignment = RichAlignment.Center });
        });
        using var output = new MemoryStream();
        await RichEdit.Core.Rtf.RtfCodec.WriteAsync(snapshot, output);
        var native = new RichEditBox();
        native.Document.SetText(TextSetOptions.FormatRtf, Encoding.ASCII.GetString(output.ToArray()));
        Assert.Equal(ParagraphAlignment.Center, native.Document.GetRange(0, 0).ParagraphFormat.Alignment);
    });

    [Theory]
    [InlineData(@"{\rtf1 aaa\qc bbb\par}")]
    [InlineData(@"{\rtf1 aaa{\qc bbb}ccc\par}")]
    [InlineData(@"{\rtf1 aaa{\qc bbb}\par}")]
    [InlineData(@"{\rtf1\qc}")]
    [InlineData(@"{\rtf1 aaa\qc}")]
    [InlineData(@"{\rtf1{\qc aaa}}")]
    [InlineData(@"{\rtf1\qc aaa}")]
    [InlineData(@"{\rtf1\qc aaa\par}")]
    [InlineData(@"{\rtf1{\qc aaa\par}}")]
    [InlineData(@"{\rtf1 aaa\qc bbb}")]
    public Task ParagraphFormattingMatchesNativeRtfAcrossGroupAndParagraphBoundaries(string rtf) => WindowsTestHost.RunAsync(async () =>
    {
        var native = new RichEditBox();
        native.Document.SetText(TextSetOptions.FormatRtf, rtf);
        var expected = native.Document.GetRange(0, 0).ParagraphFormat.Alignment switch
        {
            ParagraphAlignment.Center => RichAlignment.Center,
            ParagraphAlignment.Right => RichAlignment.Right,
            ParagraphAlignment.Justify => RichAlignment.Justify,
            _ => RichAlignment.Left,
        };
        using var input = new MemoryStream(Encoding.ASCII.GetBytes(rtf));
        var result = await RichEdit.Core.Rtf.RtfCodec.ReadAsync(input);
        var paragraph = result.Snapshot.Get<RichStory>(result.Snapshot.Body).Blocks[0];
        Assert.Equal(expected, result.Snapshot.ResolveParagraph(paragraph).Alignment);
    });
}
