using System.Text;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls;

namespace RichEdit.Maui.Tests;

public sealed class RtfSpecificationNativeTests
{
    public static IEnumerable<object[]> UnsupportedDestinations()
    {
        var handled = new HashSet<string>("""
            rtf upr ud fonttbl colortbl defchp defpap listtable listoverridetable list listlevel listoverride lfolevel
            leveltext listtext pntext listpicture picprop sp sn sv fldrslt field fldinst object result shp shpgrp
            shpinst shptxt pict shppict nesttableprops
            """.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        using var input = new StreamReader(typeof(RtfSpecificationNativeTests).Assembly.GetManifestResourceStream("Rtf191Destinations")!);
        while (input.ReadLine() is { } line)
            if (!line.StartsWith('#') && !handled.Contains(line)) yield return [line];
    }

    [Theory]
    [MemberData(nameof(UnsupportedDestinations))]
    public void EveryUnsupportedSpecificationDestinationIsIsolated(string destination)
    {
        foreach (var prefix in new[] { "", @"\*" })
        {
            var rtf = @"{\rtf1 before{" + prefix + @"\" + destination + @" hidden\par{\b hidden}}after}";
            Assert.Equal("beforeafter", RichTextDocument.FromRtf(rtf).Text);
        }
    }

    [Fact]
    public void BinaryDataInAnIgnoredPictureSubgroupDoesNotModifyTheImage()
    {
        var document = RichTextDocument.FromRtf(@"{\rtf1{\pict\pngblip\picwgoal100\pichgoal100 01{\*\unknown\bin1 Z}02}}");
        Assert.Equal(new byte[] { 1, 2 }, Assert.Single(document.CurrentSnapshot.Images).Data.ToArray());
    }

    [Fact]
    public void DbcsDelimiterBytesRemainEncodedText()
    {
        var raw = @"{\rtf1\ansi\ansicpg932 " + "\x83\x5c\x81\x7b\x81\x7d" + "}";
        var escaped = @"{\rtf1\ansi\ansicpg932 \'83\\\'81\{\'81\}}";
        Assert.Equal("ソ＋±", RichTextDocument.FromRtf(raw).Text);
        Assert.Equal("ソ＋±", RichTextDocument.FromRtf(escaped).Text);
    }

    [Theory]
    [InlineData(@"\fcharset0", "Ï")]
    [InlineData(@"\fcharset1", "П")]
    [InlineData(@"\cpg1251\fcharset0", "П")]
    [InlineData(@"\fcharset0\cpg1251", "П")]
    public void FontCodePagePrecedenceMatchesTheSpecification(string fontControls, string expected)
    {
        var rtf = @"{\rtf1\ansi\ansicpg1251\deff0{\fonttbl{\f0" + fontControls + @" Font;}}\f0 \'cf}";
        Assert.Equal(expected, RichTextDocument.FromRtf(rtf).Text);
    }

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
