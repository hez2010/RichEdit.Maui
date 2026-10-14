using System.Text;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls;

namespace RichEdit.Maui.Tests;

public sealed class RtfSpecificationNativeTests
{
    [Theory]
    [InlineData(@"{\rtf1\uc1\u945\par X}", "αX")]
    [InlineData(@"{\rtf1\uc1\u945\bin3 {\}X}", "αX")]
    [InlineData(@"{\rtf1{\upr{{\*\ud leak}}{\*\ud unicode}}tail}", "unicodetail")]
    [InlineData(@"{\rtf1{\*\unknown{\upr{ansi}{\*\ud leak}}}body}", "body")]
    [InlineData(@"{\rtf1{\headerf hidden}body}", "body")]
    [InlineData(@"{\rtf1{\xe hidden}body}", "body")]
    [InlineData(@"{\rtf1{\moMath hidden}body}", "body")]
    public void NativeProjectionReaderRespectsFallbackAndDestinationBoundaries(string rtf, string expected) =>
        Assert.Equal(expected, RichTextDocument.FromRtf(rtf).Text);

    [Theory]
    [InlineData(@"{\rtf1\uc1\u945\par X}")]
    [InlineData(@"{\rtf1{\upr{ansi}{\*\ud\uc0\u945}}tail}")]
    [InlineData(@"{\rtf1{\colortbl\red12\green34\blue56;}\cf0 text}")]
    [InlineData(@"{\rtf1\ansi\ansicpg1251\deff0{\fonttbl{\f0\cpg1251\fcharset0 Arial;}}\f0 \'cf}")]
    public Task ImportedTextAndColorSurviveWindowsNativeProjection(string rtf) => WindowsTestHost.RunAsync(async () =>
    {
        using var input = new MemoryStream(Encoding.ASCII.GetBytes(rtf));
        var imported = await RichEdit.Core.Rtf.RtfCodec.ReadAsync(input);
        using var output = new MemoryStream();
        await RichEdit.Core.Rtf.RtfCodec.WriteAsync(imported.Snapshot, output);
        var native = new RichEditBox();
        native.Document.SetText(TextSetOptions.FormatRtf, Encoding.ASCII.GetString(output.ToArray()));
        native.Document.GetText(TextGetOptions.None, out var nativeText);
        Assert.Equal(nativeText.TrimEnd('\r'), imported.Snapshot.ProjectText().Text.ToString());
        if (rtf.Contains("colortbl", StringComparison.Ordinal))
        {
            var color = native.Document.GetRange(0, 1).CharacterFormat.ForegroundColor;
            var run = Assert.Single(imported.Snapshot.Nodes.Values.OfType<RichEdit.Core.RichText>());
            Assert.Equal(new RichEdit.Core.RichColor(color.R, color.G, color.B), imported.Snapshot.ResolveCharacter(run.Id).Foreground);
        }
    });
}
