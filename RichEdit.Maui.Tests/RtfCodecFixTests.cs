namespace RichEdit.Maui.Tests;

using RichTextDocument = global::RichEdit.Maui.RichTextDocumentSnapshot;

public sealed class RtfCodecFixTests
{
    [Fact]
    public void AnsiDocumentsDefaultToWindows1252()
    {
        var document = RichTextDocument.FromRtf(@"{\rtf1\ansi caf\'e9}");

        Assert.Equal("café", document.Text);
    }

    [Fact]
    public void AnsiCharsetFontsFollowTheDeclaredAnsiCodePage()
    {
        var document = RichTextDocument.FromRtf(
            @"{\rtf1\ansi\ansicpg1251\deff0{\fonttbl{\f0\fcharset0 Arial;}}\f0 \'cf}");

        Assert.Equal("П", document.Text);
    }

    [Fact]
    public void DefaultFontCodePageAppliesWithoutAnExplicitFontControl()
    {
        var document = RichTextDocument.FromRtf(
            "{\\rtf1\\ansi\\deff0{\\fonttbl{\\f0\\fcharset128 MS Gothic;}}\r\n\\'82\\'a0}");

        Assert.Equal("あ", document.Text);
    }

    [Fact]
    public void UnicodePreferredDestinationReplacesTheAnsiAlternative()
    {
        var document = RichTextDocument.FromRtf(
            @"{\rtf1\ansi a{\upr{G}{\*\ud{\u915?}}}b}");

        Assert.Equal("aΓb", document.Text);
    }

    [Fact]
    public void UnknownControlWordsWithHugeParametersAreIgnored()
    {
        var document = RichTextDocument.FromRtf(
            @"{\rtf1\ansi\qwertyuiop99999999999999999999 x}");

        Assert.Equal("x", document.Text);
    }

    [Fact]
    public void ExtremeFormattingParametersDoNotLeakModelValidationExceptions()
    {
        var document = RichTextDocument.FromRtf(@"{\rtf1\ansi\up-2147483648 x}");

        Assert.Equal("x", document.Text);
        Assert.True(double.IsFinite(document.GetCharacterFormat(0).BaselineOffset));

        Assert.Throws<FormatException>(() =>
            RichTextDocument.FromRtf(@"{\rtf1\ansi\u-99999999999 x}"));
    }

    [Fact]
    public void ListOverrideFormatLevelsReplaceTheBaseListDefinition()
    {
        var document = RichTextDocument.FromRtf(
            @"{\rtf1\ansi" +
            @"{\*\listtable{\list\listtemplateid1{\listlevel\levelnfc0\levelstartat1" +
            @"{\leveltext \'02\'00.;}}\listid1{\listname ;}}}" +
            @"{\*\listoverridetable" +
            @"{\listoverride\listid1\listoverridecount0\ls1}" +
            @"{\listoverride\listid1\listoverridecount1\ls2" +
            @"{\lfolevel\listoverrideformat1{\listlevel\levelnfc1\levelstartat1" +
            @"{\leveltext \'02\'00.;}}}}}" +
            @"{\pard\ls1\ilvl0 arabic\par}{\pard\ls2\ilvl0 roman}}");

        var paragraphs = document.Paragraphs;
        var arabicList = paragraphs[0].Format.List;
        var romanList = paragraphs[1].Format.List;
        Assert.NotNull(arabicList);
        Assert.NotNull(romanList);
        Assert.NotEqual(arabicList.ListId, romanList.ListId);
        var arabicMarker = Assert.IsType<RichTextListMarker.Number>(
            document.Lists[arabicList.ListId].Levels[0].Marker);
        var romanMarker = Assert.IsType<RichTextListMarker.Number>(
            document.Lists[romanList.ListId].Levels[0].Marker);
        Assert.Equal(RichTextListNumberStyle.Arabic, arabicMarker.Style);
        Assert.Equal(RichTextListNumberStyle.UpperRoman, romanMarker.Style);
    }

    [Fact]
    public void ListOverrideStartAtRestartsWithoutACompatibilityListText()
    {
        var document = RichTextDocument.FromRtf(
            @"{\rtf1\ansi" +
            @"{\*\listtable{\list\listtemplateid1{\listlevel\levelnfc0\levelstartat1" +
            @"{\leveltext \'02\'00.;}}\listid1{\listname ;}}}" +
            @"{\*\listoverridetable" +
            @"{\listoverride\listid1\listoverridecount0\ls1}" +
            @"{\listoverride\listid1\listoverridecount1\ls2" +
            @"{\lfolevel\listoverridestartat\levelstartat5}}}" +
            @"{\pard\ls1\ilvl0 one\par}{\pard\ls1\ilvl0 two\par}{\pard\ls2\ilvl0 five}}");

        var restarted = document.Paragraphs[2].Format.List;
        Assert.NotNull(restarted);
        Assert.Equal(5, restarted.RestartAt);
        Assert.Equal(document.Paragraphs[0].Format.List!.ListId, restarted.ListId);

        var roundTripped = RichTextDocument.FromRtf(document.ToRtf());
        Assert.Equal(5, roundTripped.Paragraphs[2].Format.List?.RestartAt);
    }

    [Fact]
    public void ParagraphsCanClearDefaultTabStops()
    {
        var stop = new RichTextTabStop(36);
        var document = new RichTextDocument(
            "with\nwithout",
            paragraphs:
            [
                new RichTextParagraph(
                    0,
                    RichTextParagraphFormat.Default with { TabStops = [stop] }),
                new RichTextParagraph(5, RichTextParagraphFormat.Default),
            ],
            defaultParagraphFormat: RichTextParagraphFormat.Default with
            {
                TabStops = [stop],
            });

        var roundTripped = RichTextDocument.FromRtf(document.ToRtf());

        Assert.Equal([stop], roundTripped.DefaultParagraphFormat.TabStops);
        Assert.Equal([stop], roundTripped.Paragraphs[0].Format.TabStops);
        Assert.Empty(roundTripped.Paragraphs[1].Format.TabStops);
    }

    [Fact]
    public void ExplicitBorderNoneSurvivesADefaultBorder()
    {
        var borderNone = new RichTextBorder(
            RichTextBorderSides.None,
            RichTextBorderStyle.None,
            0);
        var document = new RichTextDocument(
            "boxed\nplain",
            paragraphs:
            [
                new RichTextParagraph(0, RichTextParagraphFormat.Default),
                new RichTextParagraph(
                    6,
                    RichTextParagraphFormat.Default with { Border = borderNone }),
            ],
            defaultParagraphFormat: RichTextParagraphFormat.Default with
            {
                Border = new RichTextBorder(
                    RichTextBorderSides.All,
                    RichTextBorderStyle.Single,
                    1),
            });

        var roundTripped = RichTextDocument.FromRtf(document.ToRtf());

        Assert.NotNull(roundTripped.DefaultParagraphFormat.Border);
        Assert.Equal(borderNone, roundTripped.Paragraphs[1].Format.Border);
    }

    [Fact]
    public void HyperlinkTargetsPreserveBackslashesAndQuotes()
    {
        var document = RichTextDocument.FromPlainText("share link")
            .SetLink(0..5, @"\\server\share", toolTip: "say \"hi\"")
            .SetLink(6..10, "https://example.test/?q=\"x\"");

        var roundTripped = RichTextDocument.FromRtf(document.ToRtf());

        Assert.Collection(
            roundTripped.Links,
            link =>
            {
                Assert.Equal(@"\\server\share", link.Target);
                Assert.Equal("say \"hi\"", link.ToolTip);
            },
            link => Assert.Equal("https://example.test/?q=\"x\"", link.Target));
    }

    [Fact]
    public void TransparentForegroundIsNormalizedToAColorReset()
    {
        var transparent = Microsoft.Maui.Graphics.Color.FromRgba(255, 0, 0, 0);
        var document = new RichTextDocument(
            "clear",
            runs:
            [
                new RichTextRun(
                    0,
                    5,
                    RichTextCharacterFormat.Default with { ForegroundColor = transparent }),
            ]);

        Assert.Null(document.GetCharacterFormat(0).ForegroundColor);
        Assert.Equal(
            [new RichTextRun(0, 5, RichTextCharacterFormat.Default)],
            RichTextDocument.FromRtf(document.ToRtf()).Runs);
    }

    [Fact]
    public void InvalidPictureHexRejectsTheDocument()
    {
        Assert.Throws<FormatException>(() => RichTextDocument.FromRtf(
            @"{\rtf1\ansi{\pict\pngblip\picw1\pich1 0g12}}"));
    }
}
