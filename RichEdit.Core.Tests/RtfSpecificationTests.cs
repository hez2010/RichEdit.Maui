using System.Text;
using RichEdit.Core.Rtf;
using Xunit;

namespace RichEdit.Core.Tests;

// RTF 1.9.1: Unicode RTF, font/color tables, styles, lists, tables,
// pictures and annotations. Each fixture exercises the wire syntax directly.
public sealed class RtfSpecificationTests
{
    public static IEnumerable<object[]> UnsupportedDestinations()
    {
        var handled = new HashSet<string>("""
            rtf upr ud fonttbl colortbl stylesheet defchp defpap listtable listoverridetable list listlevel listoverride lfolevel
            leveltext levelnumbers listtext pntext pn pntxta pntxtb generator rsidtbl filetbl latentstyles nonesttables nesttableprops
            pict blipuid sn sv picprop sp shppict bkmkstart bkmkend atrfstart atrfend annotation atnid atnauthor atndate atnref
            field fldinst fldrslt
            """.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        using var input = new StreamReader(typeof(RtfSpecificationTests).Assembly.GetManifestResourceStream("Rtf191Destinations")!);
        while (input.ReadLine() is { } line)
            if (!line.StartsWith('#') && !handled.Contains(line)) yield return [line];
    }

    [Theory]
    [MemberData(nameof(UnsupportedDestinations))]
    public async Task EveryUnsupportedSpecificationDestinationIsIsolated(string destination)
    {
        foreach (var prefix in new[] { "", @"\*" })
        {
            var rtf = @"{\rtf1 before{" + prefix + @"\" + destination + @" hidden\par{\b hidden}\cellx1}after}";
            var result = await Read(rtf);
            Assert.Equal("beforeafter", result.Snapshot.ProjectText().Text.ToString());
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Feature == destination);
            await Assert.ThrowsAsync<RtfFidelityException>(() => Read(rtf, strict: true));
        }
    }

    private static async Task<RtfReadResult> Read(string rtf, bool strict = false)
    {
        using var input = new MemoryStream(Encoding.Latin1.GetBytes(rtf));
        return await RtfCodec.ReadAsync(input, new() { RequireLossless = strict }, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(@"\b")]
    [InlineData(@"\par")]
    [InlineData(@"\line")]
    [InlineData(@"\emdash")]
    [InlineData(@"\unknown")]
    [InlineData(@"\bin3 {\}")]
    [InlineData(@"\~")]
    [InlineData(@"\*")]
    [InlineData(@"\'")] // Hex escape supplied below.
    public async Task UnicodeFallbackCountsControlsAsOneCharacterWithoutApplyingThem(string fallback)
    {
        if (fallback == @"\'") fallback = @"\'3f";
        var result = await Read(@"{\rtf1\uc1\u945" + fallback + @"\ulnone X}", strict: true);
        Assert.Equal("αX", result.Snapshot.ProjectText().Text.ToString());
        Assert.All(result.Snapshot.Nodes.Values.OfType<RichText>(), run => Assert.False(result.Snapshot.ResolveCharacter(run.Id).Bold));
    }

    [Fact]
    public async Task UnicodeFallbackScopeAndLargeCountsFollowTheWireCount()
    {
        var result = await Read(@"{\rtf1\uc300\u945 " + new string('?', 300) + @"{\uc0\u946}\u947{\b x}\u948}", strict: true);
        Assert.Equal("αβγxδ", result.Snapshot.ProjectText().Text.ToString());
    }

    [Theory]
    [InlineData(@"{\rtf1{\upr{ansi}{\*\ud\uc0\u945}}tail}", "αtail")]
    [InlineData(@"{\rtf1{\upr{\b ansi}{\*\ud unicode}}tail}", "unicodetail")]
    [InlineData(@"{\rtf1{\*\unknown{\upr{ansi}{\*\ud leak}}}body}", "body")]
    [InlineData(@"{\rtf1{\upr{{\*\ud leak}}{\*\ud unicode}}tail}", "unicodetail")]
    public async Task UnicodeAlternativesSelectOnlyTheUnicodeBranch(string rtf, string expected)
    {
        var result = await Read(rtf);
        Assert.Equal(expected, result.Snapshot.ProjectText().Text.ToString());
    }

    [Fact]
    public async Task UnicodeAlternativesWorkInBookmarkDestinations()
    {
        var result = await Read(@"{\rtf1{\upr{\*\bkmkstart nameA}{\*\ud{\*\bkmkstart name\u945?}}}body{\*\bkmkend name\u945?}}", strict: true);
        Assert.Equal("nameα", Assert.Single(result.Snapshot.Bookmarks.Values).Name);
        Assert.Equal("body", result.Snapshot.ProjectText().Text.ToString());
    }

    [Theory]
    [InlineData(@"\cpg1251\fcharset0")]
    [InlineData(@"\fcharset0\cpg1251")]
    [InlineData(@"\fcharset1")]
    public async Task FontCodePagePrecedenceAndDefaultCharsetDecodeText(string fontControls)
    {
        var result = await Read(@"{\rtf1\ansi\ansicpg1251\deff0{\fonttbl{\f0" + fontControls + @" Font;}}\f0 \'cf}", strict: true);
        Assert.Equal("П", result.Snapshot.ProjectText().Text.ToString());
    }

    [Fact]
    public async Task AnsiCharsetUses1252AndRunCodePagesOverrideTheSelectedFont()
    {
        var result = await Read(@"{\rtf1\ansi\ansicpg1251\deff0{\fonttbl{\f0\fcharset0 Font;}}\f0 \'cf{\cpg1251 \'cf}\'cf}", strict: true);
        Assert.Equal("ÏПÏ", result.Snapshot.ProjectText().Text.ToString());
    }

    [Theory]
    [InlineData(@"\red12\green34\blue56;", true)]
    [InlineData(";", false)]
    public async Task FirstColorEntryIsAutomaticOnlyWhenItsDefinitionIsEmpty(string entry, bool defined)
    {
        var result = await Read(@"{\rtf1{\colortbl" + entry + @"}\cf0\highlight0 text}", strict: true);
        var format = result.Snapshot.ResolveCharacter(Assert.Single(result.Snapshot.Nodes.Values.OfType<RichText>()).Id);
        RichColor? expected = defined ? new(12, 34, 56) : null;
        Assert.Equal(expected, format.Foreground);
        Assert.Equal(expected, format.Background);
    }

    [Theory]
    [InlineData("headerf")]
    [InlineData("footerf")]
    [InlineData("nonesttables")]
    [InlineData("xe")]
    [InlineData("tc")]
    [InlineData("moMath")]
    [InlineData("docvar")]
    [InlineData("mmathPr")]
    public async Task UnsupportedDestinationsCannotLeakContentOrStructuralControls(string destination)
    {
        var result = await Read(@"{\rtf1 before{\" + destination + " ignored\\\r\n" + @"\b\par\trowd\cellx100\intbl data\cell\row}after}");
        Assert.Equal("beforeafter", result.Snapshot.ProjectText().Text.ToString());
        Assert.Empty(result.Snapshot.Nodes.Values.OfType<RichTable>());
    }

    [Theory]
    [InlineData(-32768)]
    [InlineData(-1)]
    [InlineData(32767)]
    [InlineData(65535)]
    public async Task StyleHandlesAreRemappedWithoutCollidingWithNormal(int handle)
    {
        var result = await Read($@"{{\rtf1{{\stylesheet{{\s0\snext{handle} Normal;}}{{\s{handle}\sbasedon0\snext0\b Child;}}}}\s{handle} text\par}}", strict: true);
        var normal = Assert.Single(result.Snapshot.Styles.Definitions.Values, style => style.Name == "Normal");
        var child = Assert.Single(result.Snapshot.Styles.Definitions.Values, style => style.Name == "Child");
        Assert.NotEqual(normal.Id, child.Id);
        Assert.Equal(child.Id, normal.NextStyle);
        Assert.Equal(normal.Id, child.BasedOn);
        Assert.Equal(normal.Id, child.NextStyle);
        Assert.True(result.Snapshot.ResolveCharacter(Assert.Single(result.Snapshot.Nodes.Values.OfType<RichText>()).Id).Bold);
    }

    [Fact]
    public async Task StyleNamesAreOptionalAndAuthoredStyle222CanBeABase()
    {
        var unnamed = await Read(@"{\rtf1{\stylesheet{\s0;}{\s1\b;}}\s1 text\par}", strict: true);
        Assert.Equal(2, unnamed.Snapshot.Styles.Definitions.Count);
        var document = RichDocumentSnapshot.Build(edit =>
        {
            edit.SetStyles(new([
                new(new(222), "Base", RichStyleKind.Paragraph) { Character = new() { Bold = true } },
                new(new(300), "Child", RichStyleKind.Paragraph) { BasedOn = new(222) },
            ]));
            edit.SetParagraphStyle(new(2), new(300));
        });
        using var stream = new MemoryStream();
        await RtfCodec.WriteAsync(document, stream, TestContext.Current.CancellationToken);
        stream.Position = 0;
        var restored = await RtfCodec.ReadAsync(stream, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(new RichStyleId(222), restored.Snapshot.Styles.Definitions[new(300)].BasedOn);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-720)]
    public async Task NonpositiveTabStopsProduceDiagnosticsInsteadOfRejectingTheDocument(int position)
    {
        var rtf = $@"{{\rtf1\tqc\tx{position}\tx1440 text\tab tabbed\par}}";
        var result = await Read(rtf);
        var paragraph = Assert.Single(result.Snapshot.Nodes.Values.OfType<RichParagraph>());
        var tab = Assert.Single(result.Snapshot.ResolveParagraph(paragraph.Id).TabStops);
        Assert.Equal(1440, tab.Position.Twips);
        Assert.Equal(RichTabAlignment.Left, tab.Alignment);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Feature == "tab-position");
        await Assert.ThrowsAsync<RtfFidelityException>(() => Read(rtf, strict: true));
    }

    [Fact]
    public async Task StandardCommentMetadataRetainsAuthorDateAndRange()
    {
        var date = new DateTimeOffset(2026, 10, 14, 13, 47, 0, TimeSpan.Zero);
        var packed = (date.Year - 1900) << 20 | date.Month << 16 | date.Day << 11 | date.Hour << 6 | date.Minute;
        var result = await Read(@"{\rtf1{\*\atrfstart 7}body{\*\atrfend 7}{\*\atnid AB}{\*\atnauthor Author}" +
            @"{\*\annotation{\*\atndate " + packed + @"}{\*\atnref7}comment}}");
        var comment = Assert.Single(result.Snapshot.Comments.Values);
        Assert.Equal("Author", comment.Author);
        Assert.Equal(date, comment.Created);
        Assert.Equal("comment", comment.Text);
        Assert.NotNull(comment.Range);
        Assert.Equal(0, comment.Range.Anchor.Offset);
        Assert.Equal(4, comment.Range.Active.Offset);
        Assert.Equal("body", result.Snapshot.ProjectText().Text.ToString());
    }

    [Fact]
    public async Task PictureScalingIsAppliedToTheDisplayDimensions()
    {
        var result = await Read(@"{\rtf1{\pict\pngblip\picwgoal1440\pichgoal720\picscalex50\picscaley200 0102}}", strict: true);
        var image = Assert.Single(result.Snapshot.Nodes.Values.OfType<RichImage>());
        Assert.Equal(720, image.Width.Twips);
        Assert.Equal(1440, image.Height.Twips);
    }

    [Fact]
    public async Task ListOverridesKeepStartAndFormattingIndependent()
    {
        var result = await Read(@"{\rtf1{\*\listtable{\list{\listlevel\levelnfc0\levelstartat3}\listid9}}" +
            @"{\*\listoverridetable{\listoverride\listid9\listoverridecount1{\lfolevel\listoverridestartat\levelstartat7}\ls1}" +
            @"{\listoverride\listid9\listoverridecount1{\lfolevel\listoverrideformat1{\listlevel\levelnfc1\levelstartat99}}\ls2}}" +
            @"\pard\ls1 first\par\pard\ls1 second\par\pard\ls2 third\par}", strict: true);
        var lists = result.Snapshot.Nodes.Values.OfType<RichList>().OrderBy(list => list.Id.Value).ToArray();
        Assert.Equal(2, lists.Length);
        Assert.Equal(7, lists[0].StartNumber);
        Assert.Equal(2, lists[0].Items.Count);
        Assert.Equal(3, lists[1].StartNumber);
        Assert.Equal(RichListNumberingStyle.UpperRoman, lists[1].NumberStyle);
    }

    [Fact]
    public async Task ModernListFormatOverridesFallbackAndNoNumberCreatesNoMarker()
    {
        var result = await Read(@"{\rtf1{\*\listtable{\list{\listlevel\levelnfcn255\levelnfc0}\listid9}}" +
            @"{\*\listoverridetable{\listoverride\listid9\ls1}}\pard\ls1 text\par}", strict: true);
        Assert.Empty(result.Snapshot.Nodes.Values.OfType<RichList>());
        Assert.Equal("text", result.Snapshot.ProjectText().Text.ToString());
    }

    [Fact]
    public async Task TableCoordinatesAreRelativeToEachRowsLeftEdge()
    {
        var result = await Read(@"{\rtf1\trowd\trleft-1500\cellx-500\cellx500\intbl a\cell b\cell\row" +
            @"\trowd\trleft200\cellx1200\cellx2200\intbl c\cell d\cell\row}");
        var table = Assert.Single(result.Snapshot.Nodes.Values.OfType<RichTable>());
        Assert.Equal(new[] { 1000, 1000 }, table.Columns.Select(column => column.Twips));
        Assert.Equal("a\tb\nc\td", result.Snapshot.ProjectText().Text.ToString());
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Feature == "table-row-offset");
    }

    [Fact]
    public async Task UnequalTableRowWidthsKeepTheirCellsInsteadOfRejectingTheDocument()
    {
        var rtf = @"{\rtf1\trowd\cellx1000\intbl a\cell\row\trowd\cellx1000\cellx2000\intbl b\cell c\cell\row}";
        var result = await Read(rtf);
        var table = Assert.Single(result.Snapshot.Nodes.Values.OfType<RichTable>());
        Assert.Equal(2, result.Snapshot.GetTableGrid(table.Id).Columns);
        Assert.Equal("a\t\nb\tc", result.Snapshot.ProjectText().Text.ToString());
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Feature == "table-row-width");
        await Assert.ThrowsAsync<RtfFidelityException>(() => Read(rtf, strict: true));
    }

    [Fact]
    public async Task NestedTableDescriptorsMayFollowTheCellContent()
    {
        var result = await Read(@"{\rtf1\trowd\cellx3000\pard\intbl outer\par\pard\intbl\itap2 nested\nestcell" +
            @"{\*\nesttableprops\trowd\cellx1500\nestrow}{\nonesttables fallback}\pard\intbl\itap1\cell\row}", strict: true);
        Assert.Equal(2, result.Snapshot.Nodes.Values.OfType<RichTable>().Count());
        Assert.Equal("outer\nnested", result.Snapshot.ProjectText().Text.ToString());
    }

    [Fact]
    public async Task ListsInSeparateTableCellsStayInTheirOwnCell()
    {
        var result = await Read(@"{\rtf1{\*\listtable{\list{\listlevel\levelnfc0}\listid9}}{\*\listoverridetable{\listoverride\listid9\ls1}}" +
            @"\trowd\cellx1000\cellx2000\pard\intbl\ls1 first\par second\cell\pard\intbl\ls1 third\cell\row}", strict: true);
        var table = Assert.Single(result.Snapshot.Nodes.Values.OfType<RichTable>());
        var grid = result.Snapshot.GetTableGrid(table.Id);
        Assert.Equal(2, result.Snapshot.Nodes.Values.OfType<RichList>().Count());
        foreach (var column in new[] { 0, 1 })
            Assert.IsType<RichList>(result.Snapshot.Get<RichBlock>(Assert.Single(result.Snapshot.Get<RichTableCell>(grid[0, column]).Blocks)));
        Assert.Equal("first\nsecond\tthird", result.Snapshot.ProjectText().Text.ToString());
    }

    [Theory]
    [InlineData(@"\ltrmark", "\u200e")]
    [InlineData(@"\rtlmark", "\u200f")]
    [InlineData(@"\zwbo", "\u200b")]
    [InlineData(@"\zwnbo", "\ufeff")]
    [InlineData(@"\zwj", "\u200d")]
    [InlineData(@"\zwnj", "\u200c")]
    public async Task SpecialCharacterControlsRetainTheirUnicodeCharacters(string control, string character)
    {
        var result = await Read(@"{\rtf1 a" + control + " b}", strict: true);
        Assert.Equal("a" + character + "b", result.Snapshot.ProjectText().Text.ToString());
    }

    [Fact]
    public async Task ParagraphAndCharacterResetsUseDocumentDefaults()
    {
        var result = await Read(@"{\rtf1\deflang1041{\*\defchp\fs30}{\*\defpap\qc\sa120\sl480\slmult1}" +
            @"\pard\plain first\par\ql\sa0\fs20\lang1033 other\par\pard\plain last\par}", strict: true);
        foreach (var run in result.Snapshot.Nodes.Values.OfType<RichText>().Where(run => run.Content.ToString() != "other"))
        {
            Assert.Equal(15d, result.Snapshot.ResolveCharacter(run.Id).FontSize?.Points);
            // defchp without a language must not erase the document's deflang.
            Assert.Equal("ja-JP", result.Snapshot.ResolveCharacter(run.Id).Language);
            var paragraph = result.Snapshot.ResolveParagraph(run.Parent!.Value);
            Assert.Equal(RichAlignment.Center, paragraph.Alignment);
            Assert.Equal(120, paragraph.SpaceAfter.Twips);
            Assert.Equal(480, paragraph.LineSpacing.RtfValue);
        }
        var minimal = await Read(@"{\rtf1 text}", strict: true);
        Assert.Equal(12d, minimal.Snapshot.ResolveCharacter(Assert.Single(minimal.Snapshot.Nodes.Values.OfType<RichText>()).Id).FontSize?.Points);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData(@"\additive", true)]
    public async Task CharacterStylesRespectTheirAdditiveFlag(string additive, bool bold)
    {
        var result = await Read(@"{\rtf1{\stylesheet{\s0\b Normal;}{\*\cs1" + additive + @"\i Emphasis;}}\s0\cs1 text\par}", strict: true);
        var format = result.Snapshot.ResolveCharacter(Assert.Single(result.Snapshot.Nodes.Values.OfType<RichText>()).Id);
        Assert.Equal(bold, format.Bold);
        Assert.True(format.Italic);
    }

    [Fact]
    public async Task NestedFieldsFlattenIntoTheirContainingResult()
    {
        var result = await Read(@"{\rtf1 before{\field{\*\fldinst OUTER}{\fldrslt a{\field{\*\fldinst INNER}{\fldrslt b}}c}}after}");
        var field = Assert.Single(result.Snapshot.Nodes.Values.OfType<RichField>());
        Assert.Equal("abc", field.Result);
        Assert.Equal("before\ufffcafter", result.Snapshot.ProjectText().Text.ToString());
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Feature == "nested-field");
    }

    [Theory]
    [InlineData(@"HYPERLINK ""one"" ""two""")]
    [InlineData(@"HYPERLINK ""one\u10?two""")]
    public async Task UnrepresentableHyperlinksRemainFieldsInsteadOfFailingOrLosingArguments(string instruction)
    {
        var result = await Read(@"{\rtf1{\field{\*\fldinst " + instruction + @"}{\fldrslt label}}}");
        var field = Assert.Single(result.Snapshot.Nodes.Values.OfType<RichField>());
        Assert.Equal("label", field.Result);
        Assert.Contains("two", field.Instruction, StringComparison.Ordinal);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Feature == "hyperlink-options");
    }

    [Theory]
    [InlineData(0, 1, "picture-size")]
    [InlineData(-50, 50, "picture-transform")]
    public async Task UnrepresentablePictureTransformsRetainTheImageAndReportTheApproximation(int scale, int width, string feature)
    {
        var result = await Read($@"{{\rtf1{{\pict\pngblip\picwgoal100\pichgoal100\picscalex{scale} 0102}}}}");
        Assert.Equal(width, Assert.Single(result.Snapshot.Nodes.Values.OfType<RichImage>()).Width.Twips);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Feature == feature);
    }

    [Fact]
    public async Task ImageIdentifiersCanNameDifferentRepresentationsWithoutDiscardingBytes()
    {
        var result = await Read(@"{\rtf1{\pict\pngblip\picwgoal100\pichgoal100{\*\blipuid abcd}0102}" +
            @"{\pict\jpegblip\picwgoal100\pichgoal100{\*\blipuid abcd}0304}}", strict: true);
        Assert.Equal(2, result.Snapshot.Assets.Count);
        Assert.Equal(2, result.Snapshot.Nodes.Values.OfType<RichImage>().Count());
    }

    [Fact]
    public async Task LegacyListItemsAndContinuationRetainTheirStructure()
    {
        var result = await Read(@"{\rtf1\pard{\pntext 4.\tab}{\*\pn\pnlvlbody\pndec\pnstart4}first\par" +
            @"\pard{\*\pn\pnlvlcont\pndec\pnstart4}continued\par" +
            @"\pard{\pntext 5.\tab}{\*\pn\pnlvlbody\pndec\pnstart4}second\par}", strict: true);
        var list = Assert.Single(result.Snapshot.Nodes.Values.OfType<RichList>());
        Assert.True(list.Numbered);
        Assert.Equal(4, list.StartNumber);
        Assert.Equal(2, list.Items.Count);
        Assert.Equal(2, result.Snapshot.Get<RichListItem>(list.Items[0]).Blocks.Count);
        Assert.Equal("first\ncontinued\nsecond", result.Snapshot.ProjectText().Text.ToString());
    }

    [Theory]
    [InlineData("page")]
    [InlineData("sect")]
    [InlineData("column")]
    public async Task PaginationBreaksKeepTextSeparatedInContinuousDocuments(string control)
    {
        var result = await Read(@"{\rtf1 first\" + control + " second}");
        Assert.Equal("first\nsecond", result.Snapshot.ProjectText().Text.ToString());
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Feature == "pagination");
    }

    [Fact]
    public async Task ParagraphStylesCanReferToTheImplicitNormalStyle()
    {
        var result = await Read(@"{\rtf1{\stylesheet{\s1\sbasedon0\snext0\b Heading;}}\s1 text\par}", strict: true);
        var heading = Assert.Single(result.Snapshot.Styles.Definitions.Values, style => style.Name == "Heading");
        var normal = Assert.Single(result.Snapshot.Styles.Definitions.Values, style => style.Name == "Normal");
        Assert.Equal(normal.Id, heading.BasedOn);
        Assert.Equal(normal.Id, heading.NextStyle);
    }

    [Theory]
    [InlineData(932, 0x83, 0x5c)]
    [InlineData(932, 0x81, 0x7b)]
    [InlineData(932, 0x81, 0x7d)]
    [InlineData(936, 0x81, 0x5c)]
    [InlineData(950, 0xa5, 0x5c)]
    public async Task RawDbcsTrailBytesCannotBecomeRtfDelimiters(int codePage, byte lead, byte trail)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var expected = Encoding.GetEncoding(codePage).GetString([lead, trail]);
        var bytes = Encoding.ASCII.GetBytes($@"{{\rtf1\ansi\ansicpg{codePage} ").Concat(new byte[] { lead, trail }).Concat("}"u8.ToArray()).ToArray();
        using var input = new OneByteStream(bytes);
        var imported = await RtfCodec.ReadAsync(input, new() { RequireLossless = true }, TestContext.Current.CancellationToken);
        Assert.Equal(expected, imported.Snapshot.ProjectText().Text.ToString());
    }

    [Fact]
    public async Task EscapedDbcsDelimitersRemainInTheSameEncodedRun()
    {
        var result = await Read(@"{\rtf1\ansi\ansicpg932 \'83\\\'81\{\'81\}}", strict: true);
        Assert.Equal("ソ＋±", result.Snapshot.ProjectText().Text.ToString());
    }

    [Fact]
    public async Task EscapedSemicolonsBelongToFontAndStyleNames()
    {
        var result = await Read(@"{\rtf1\deff0{\fonttbl{\f0 Font\'3bName;}}{\stylesheet{\s1 Style\'3bName;}}\s1 text\par}", strict: true);
        Assert.Equal("Font;Name", result.Snapshot.DefaultCharacter.FontFamily.Value);
        Assert.Equal("Style;Name", Assert.Single(result.Snapshot.Styles.Definitions.Values).Name);
    }

    private sealed class OneByteStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(1, buffer.Length)], cancellationToken);
    }
}
