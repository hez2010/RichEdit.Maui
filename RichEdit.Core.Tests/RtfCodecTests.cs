using System.Text;
using RichEdit.Core.Rtf;
using Xunit;

namespace RichEdit.Core.Tests;

public sealed class RtfCodecTests
{
    [Theory]
    [InlineData(@"{\rtf1 aaa\qc bbb\par}", RichAlignment.Center)]
    [InlineData(@"{\rtf1 aaa{\qc bbb}ccc\par}", RichAlignment.Left)]
    [InlineData(@"{\rtf1 aaa{\qc bbb}\par}", RichAlignment.Left)]
    [InlineData(@"{\rtf1{\qc aaa}}", RichAlignment.Left)]
    [InlineData(@"{\rtf1\qc aaa}", RichAlignment.Left)]
    [InlineData(@"{\rtf1{\qc aaa\par}}", RichAlignment.Center)]
    public async Task ExternalParagraphPropertiesAreCapturedAtTheParagraphMark(string rtf, RichAlignment alignment)
    {
        using var input = new MemoryStream(Encoding.ASCII.GetBytes(rtf));
        var imported = await RtfCodec.ReadAsync(input, cancellationToken: TestContext.Current.CancellationToken);
        var paragraph = imported.Snapshot.Get<RichStory>(imported.Snapshot.Body).Blocks[0];
        Assert.Equal(alignment, imported.Snapshot.ResolveParagraph(paragraph).Alignment);
    }

    [Fact]
    public async Task EveryUnderlineAndDistributedAlignmentRoundTripsWithoutApproximation()
    {
        foreach (var underline in Enum.GetValues<RichUnderline>())
        {
            var snapshot = RichDocumentSnapshot.Build(edit =>
            {
                edit.ReplaceText(new(edit.Body, new(2), 0), 0, "underlined words");
                edit.SetCharacterFormat(new(edit.Body, new(2), 0), 16, new() { Underline = underline });
                edit.SetParagraphFormat(new(2), new() { Alignment = RichAlignment.Distributed });
            });
            var result = await RoundTrip(snapshot);
            Assert.Equal(underline, result.ResolveCharacter(Assert.Single(result.Nodes.Values.OfType<RichText>()).Id).Underline);
            Assert.Equal(RichAlignment.Distributed, result.ResolveParagraph(result.Get<RichStory>(result.Body).Blocks[0]).Alignment);
        }
    }

    [Fact]
    public async Task LineSpacingRetainsAbsoluteMultipleAndCascadeIntent()
    {
        foreach (var spacing in new[] { RichLineSpacing.Automatic, RichLineSpacing.Multiple(1), RichLineSpacing.Multiple(1.5),
            RichLineSpacing.Multiple(2), RichLineSpacing.Multiple(1.2), RichLineSpacing.AtLeast(new(271)), RichLineSpacing.Exactly(new(319)) })
        {
            var snapshot = RichDocumentSnapshot.Build(edit =>
            {
                edit.SetDefaultParagraphFormat(new() { LineSpacing = RichLineSpacing.Multiple(2) });
                edit.SetParagraphFormat(new(2), new() { LineSpacing = spacing });
            });
            var loaded = await RoundTrip(snapshot);
            Assert.Equal(snapshot.DefaultParagraph, loaded.DefaultParagraph);
            Assert.Equal(spacing, loaded.ResolveParagraph(loaded.Get<RichStory>(loaded.Body).Blocks[0]).LineSpacing);
        }
        var reset = RichDocumentSnapshot.Build(edit => edit.SetParagraphFormat(new(2), new() { LineSpacing = StyleValue<RichLineSpacing>.Initial }));
        var restored = await RoundTrip(reset);
        Assert.Equal(StyleValueKind.Initial, restored.Get<RichParagraph>(restored.Get<RichStory>(restored.Body).Blocks[0]).Format.LineSpacing.Kind);
        using var legacy = new MemoryStream(Encoding.ASCII.GetBytes(@"{\rtf1{\*\remodel1}\pard{\*\rep\reinherit4095\reinitial0\renull0}old}"));
        var old = await RtfCodec.ReadAsync(legacy, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(StyleValueKind.Inherit, old.Snapshot.Get<RichParagraph>(old.Snapshot.Get<RichStory>(old.Snapshot.Body).Blocks[0]).Format.LineSpacing.Kind);
        Assert.Empty(old.Diagnostics);
        using var external = new MemoryStream(Encoding.ASCII.GetBytes(@"{\rtf1\pard\slmult1\sl288 text\par}"));
        var imported = await RtfCodec.ReadAsync(external, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(RichLineSpacing.Multiple(1.2), imported.Snapshot.ResolveParagraph(imported.Snapshot.Get<RichStory>(imported.Snapshot.Body).Blocks[0]).LineSpacing);
    }

    [Fact]
    public async Task TabStopsLeadersHyphenationAndExplicitClearingRoundTrip()
    {
        var stops = new RichTabStops(Enum.GetValues<RichTabLeader>().Select((leader, index) =>
            new RichTabStop(new(720 + index * 480), (RichTabAlignment)(index % 4), leader)));
        Assert.Equal(stops, new RichTabStops(stops.Reverse()));
        Assert.Equal(stops.GetHashCode(), new RichTabStops(stops.Reverse()).GetHashCode());
        var snapshot = RichDocumentSnapshot.Build(edit =>
        {
            edit.SetDefaultParagraphFormat(new() { TabStops = stops, Hyphenation = true });
            edit.SetParagraphFormat(new(2), new() { TabStops = RichTabStops.Empty, Hyphenation = StyleValue<bool>.Initial });
            var second = edit.InsertParagraph(edit.Body, 1, "a\tb");
            edit.SetParagraphFormat(second, new() { TabStops = stops, Hyphenation = true });
        });
        var loaded = await RoundTrip(snapshot);
        Assert.Equal(snapshot.DefaultParagraph, loaded.DefaultParagraph);
        var paragraphs = loaded.Get<RichStory>(loaded.Body).Blocks;
        Assert.Empty(loaded.ResolveParagraph(paragraphs[0]).TabStops);
        Assert.False(loaded.ResolveParagraph(paragraphs[0]).Hyphenation);
        Assert.Equal(stops, loaded.ResolveParagraph(paragraphs[1]).TabStops);
        Assert.True(loaded.ResolveParagraph(paragraphs[1]).Hyphenation);
        using var external = new MemoryStream(Encoding.ASCII.GetBytes(@"{\rtf1\pard\tqr\tldot\tx1440\tqc\tx720\tql\tx720 a\tab b\par}"));
        var imported = await RtfCodec.ReadAsync(external, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(imported.Diagnostics);
        var importedStops = imported.Snapshot.ResolveParagraph(imported.Snapshot.Get<RichStory>(imported.Snapshot.Body).Blocks[0]).TabStops.ToArray();
        Assert.Equal(new RichTabStop(new(720)), importedStops[0]);
        Assert.Equal(new RichTabStop(new(1440), RichTabAlignment.Right, RichTabLeader.Dots), importedStops[1]);
        Assert.Throws<ArgumentException>(() => new RichTabStops([new(new(100)), new(new(100))]));
        Assert.Throws<ArgumentException>(() => RichDocumentSnapshot.Build(edit => edit.SetParagraphFormat(new(2), new() { TabStops = StyleValue<RichTabStops>.FromValue(null!) })));
    }

    [Fact]
    public async Task CharacterAndParagraphShadingPreserveCoverageColorsAndIndependentBackgrounds()
    {
        foreach (var amount in new[] { 0, 3333, 10000 })
        {
            var shading = new RichShading(amount, new(200, 30, 20), new(40, 50, 210));
            var character = new RichCharacterStyle { Shading = shading, Background = new RichColor(1, 200, 30) };
            var paragraph = new RichParagraphStyle { Shading = shading, Background = new RichColor(220, 230, 30) };
            var snapshot = RichDocumentSnapshot.Build(edit =>
            {
                edit.SetDefaultCharacterFormat(character);
                edit.SetDefaultParagraphFormat(paragraph);
                edit.SetStyles(new([new(new(1), "Shaded", RichStyleKind.Paragraph) { Character = character, Paragraph = paragraph }]));
                edit.ReplaceText(new(edit.Body, new(2), 0), 0, "shaded");
                edit.SetParagraphStyle(new(2), new(1));
                edit.SetParagraphFormat(new(2), paragraph);
                edit.SetCharacterFormat(new(edit.Body, new(2), 0), 6, character);
            });
            var result = await RoundTrip(snapshot);
            Assert.Equal(character, result.DefaultCharacter);
            Assert.Equal(paragraph, result.DefaultParagraph);
            Assert.Equal(snapshot.Styles.Definitions[new(1)], result.Styles.Definitions[new(1)]);
            Assert.Equal(character, Assert.Single(result.Nodes.Values.OfType<RichText>()).Format);
            Assert.Equal(paragraph, Assert.Single(result.Nodes.Values.OfType<RichParagraph>()).Format);
        }
        var cleared = RichDocumentSnapshot.Build(edit =>
        {
            edit.ReplaceText(new(edit.Body, new(2), 0), 0, "reset");
            edit.SetCharacterFormat(new(edit.Body, new(2), 0), 5, new() { Shading = StyleValue<RichShading?>.Initial });
            edit.SetParagraphFormat(new(2), new() { Shading = StyleValue<RichShading?>.FromValue(null) });
        });
        var restored = await RoundTrip(cleared);
        Assert.Equal(StyleValueKind.Initial, Assert.Single(restored.Nodes.Values.OfType<RichText>()).Format.Shading.Kind);
        Assert.Equal(StyleValue<RichShading?>.FromValue(null), Assert.Single(restored.Nodes.Values.OfType<RichParagraph>()).Format.Shading);
    }

    [Theory]
    [InlineData("")]
    [InlineData("one\ntwo\n")]
    [InlineData("a{b}\\c\t日本語😀\u2028e\uFFFC")]
    public async Task PlainContentRoundTripsAsSemanticParagraphs(string text)
    {
        var snapshot = RichDocumentSnapshot.FromPlainText(text);
        var loaded = await RoundTrip(snapshot);
        Assert.Equal(text, loaded.ProjectText().Text.ToString());
        Assert.Equal(snapshot.Nodes.Values.OfType<RichParagraph>().Count(), loaded.Nodes.Values.OfType<RichParagraph>().Count());
        Assert.All(loaded.Nodes.Values.OfType<RichInline>(), inline => Assert.Equal(new RichCharacterStyle(), inline.Format));
    }

    [Fact]
    public async Task StylesDefaultsAndAllCascadeOperationsRoundTrip()
    {
        var character = new RichCharacterStyle
        {
            FontFamily = "Example; Family", FontSize = new RichFontSize(29), Bold = false,
            Italic = StyleValue<bool>.Initial, Underline = RichUnderline.Wave, UnderlineColor = new RichColor(2, 3, 4),
            Foreground = StyleValue<RichColor?>.FromValue(null), Background = new RichColor(11, 12, 13), Strike = RichStrike.Double,
            Script = RichScript.Superscript, CharacterSpacing = new RichLength(17), SmallCaps = true,
            AllCaps = false, Shadow = true, Outline = false, Hidden = true, Language = "ja-JP", Direction = RichDirection.RightToLeft,
        };
        var paragraph = new RichParagraphStyle { Alignment = RichAlignment.Justify, Direction = RichDirection.LeftToRight,
            LeadingIndent = new RichLength(121), TrailingIndent = new RichLength(80), FirstLineIndent = new RichLength(-20),
            SpaceBefore = new RichLength(24), SpaceAfter = new RichLength(30), HeadingLevel = 3, KeepTogether = true,
            KeepWithNext = StyleValue<bool>.Initial, WidowControl = false, Background = new RichColor(42, 43, 44) };
        var snapshot = RichDocumentSnapshot.Build(edit =>
        {
            edit.SetDefaultCharacterFormat(new() { FontFamily = "Default", FontSize = new RichFontSize(24) });
            edit.SetDefaultParagraphFormat(new() { SpaceAfter = new RichLength(60) });
            edit.SetStyles(new([
                new(new(1), "Body; name", RichStyleKind.Paragraph) { Character = new() { Bold = true }, Paragraph = new() { Alignment = RichAlignment.Center }, NextStyle = new(1) },
                new(new(2), "Child", RichStyleKind.Paragraph) { BasedOn = new(1), Character = character, Paragraph = paragraph },
            ]));
            edit.SetParagraphStyle(new(2), new(2));
            edit.ReplaceText(new(edit.Body, new(2), 0), 0, "styled");
            edit.SetCharacterFormat(new(edit.Body, new(2), 0), 6, character);
            edit.SetParagraphFormat(new(2), paragraph);
        });
        var loaded = await RoundTrip(snapshot);
        Assert.Equal(snapshot.DefaultCharacter, loaded.DefaultCharacter);
        Assert.Equal(snapshot.DefaultParagraph, loaded.DefaultParagraph);
        foreach (var style in snapshot.Styles.Definitions) Assert.Equal(style.Value, loaded.Styles.Definitions[style.Key]);
        Assert.Equal(character, Assert.Single(loaded.Nodes.Values.OfType<RichText>()).Format);
        Assert.Equal(paragraph, Assert.Single(loaded.Nodes.Values.OfType<RichParagraph>()).Format);
    }

    [Fact]
    public async Task NestedMergedTablesAndListsRetainStructure()
    {
        var snapshot = RichDocumentSnapshot.Build(edit =>
        {
            var table = edit.InsertTable(edit.Body, 1, 3, [new(1000), new(2000), new(1400)]);
            var initial = edit.Snapshot;
            var cell = initial.GetTableGrid(table)[0, 0];
            var paragraph = initial.Get<RichTableCell>(cell).Blocks[0];
            edit.ReplaceText(new(edit.Body, paragraph, 0), 0, "merged");
            edit.MergeCells(new(table, 0, 0, 2, 2));
            edit.InsertTable(cell, 1, 2, [new(700)]);
            var list = edit.WrapInList(edit.Body, 0, 2, true, 4);
            var item = edit.Snapshot.Get<RichList>(list).Items[0];
            edit.InsertParagraph(item, 1, "continuation");
        });
        var loaded = await RoundTrip(snapshot);
        Assert.Equal(Shape(snapshot), Shape(loaded));
        Assert.Equal(snapshot.ProjectText().Text.ToString(), loaded.ProjectText().Text.ToString());
    }

    [Fact]
    public async Task AdjacentTablesRetainSeparateIdentitiesWithoutAuthoringSpacerParagraphs()
    {
        var snapshot = RichDocumentSnapshot.Build(edit =>
        {
            var outer = edit.InsertTable(edit.Body, 1, 1, [new(3000)]);
            var cell = edit.Snapshot.GetTableGrid(outer)[0, 0];
            edit.InsertTable(cell, 1, 1, [new(1000)]);
            edit.InsertTable(cell, 2, 1, [new(500), new(800)]);
            edit.InsertTable(edit.Body, 2, 2, [new(700), new(900)]);
            edit.DeleteBlock(new(2));
        });
        var loaded = await RoundTrip(snapshot);
        Assert.Equal(Shape(snapshot), Shape(loaded));
    }

    [Fact]
    public async Task ImagesResourcesAnnotationsAndOrphansRoundTrip()
    {
        var document = new RichDocument(RichDocumentSnapshot.FromPlainText("abcdef\nsecond"));
        document.Edit(edit =>
        {
            var paragraph = new RichNodeId(2);
            var range = new RichTextSelection(new(edit.Body, paragraph, 4, TextEdit.Core.CaretAffinity.Upstream), new(edit.Body, paragraph, 1));
            edit.AddBookmark("Selection", range);
            edit.AddComment(range, "a\r\nb\nc\rd😀", "Author", new DateTimeOffset(2026, 10, 11, 3, 25, 0, TimeSpan.Zero));
            var orphan = edit.AddComment(new(new(edit.Body, paragraph, 0), new(edit.Body, paragraph, 1)), "keep");
            edit.ReplaceText(new(edit.Body, paragraph, 0), 1, "X");
            var asset = edit.AddImageAsset(RichImageKind.Png, new byte[] { 1, 2, 3, 4 });
            edit.InsertImage(new(edit.Body, paragraph, 6), asset, new(100), new(200), "Alt😀");
            edit.InsertImage(new(edit.Body, paragraph, 7), asset, new(300), new(400), "");
            edit.AddImageAsset(RichImageKind.Jpeg, new byte[] { 6, 7, 8 });
        });
        var snapshot = document.CurrentSnapshot;
        var loaded = await RoundTrip(snapshot);
        Assert.Equal(snapshot.ProjectText().Text.ToString(), loaded.ProjectText().Text.ToString());
        Assert.Equal(2, loaded.Assets.Count);
        var images = loaded.Nodes.Values.OfType<RichImage>().OrderBy(image => image.Width.Twips).ToArray();
        Assert.Equal(images[0].Asset, images[1].Asset);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, loaded.Assets[images[0].Asset].Data);
        Assert.Equal("Alt😀", images[0].AlternativeText);
        Assert.Equal("", images[1].AlternativeText);
        Assert.Equal(2, loaded.Comments.Count);
        var comment = Assert.Single(loaded.Comments.Values, comment => !comment.IsOrphaned);
        Assert.Equal("a\r\nb\nc\rd😀", comment.Text);
        Assert.Equal("Author", comment.Author);
        Assert.Equal(new DateTimeOffset(2026, 10, 11, 3, 25, 0, TimeSpan.Zero), comment.Created);
        Assert.Equal(4, comment.Range!.Anchor.Offset);
        Assert.Equal(1, comment.Range.Active.Offset);
        Assert.Equal(TextEdit.Core.CaretAffinity.Upstream, comment.Range.Anchor.Affinity);
        Assert.Equal("Selection", Assert.Single(loaded.Bookmarks.Values).Name);
    }

    [Fact]
    public async Task ExternalRtfDecodesDbcsAndReportsUnsupportedDestinations()
    {
        using var input = new MemoryStream(Encoding.ASCII.GetBytes(@"{\rtf1\ansi\ansicpg932 A\'82\'a0\u12354?{\*\unknown hidden}{\b B}{\pict\pngblip\picwgoal20\pichgoal20 0102}}"));
        var result = await RtfCodec.ReadAsync(input, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("AああB\uFFFC", result.Snapshot.ProjectText().Text.ToString());
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Feature == "unknown");
        input.Position = 0;
        await Assert.ThrowsAsync<RtfFidelityException>(() => RtfCodec.ReadAsync(input, new() { RequireLossless = true }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UnsupportedHyperlinkBehaviorRetainsTheInstructionAndReportsItsEditingLimit()
    {
        using var input = new MemoryStream(Encoding.ASCII.GetBytes(@"{\rtf1{\field{\*\fldinst HYPERLINK ""https://example.test"" \\t ""frame""}{\fldrslt result}}}"));
        var result = await RtfCodec.ReadAsync(input, cancellationToken: TestContext.Current.CancellationToken);
        var field = Assert.Single(result.Snapshot.Nodes.Values.OfType<RichField>());
        Assert.Contains("frame", field.Instruction);
        Assert.Equal("result", field.Result);
        Assert.Contains(result.Diagnostics, value => value.Feature == "hyperlink-options");
        var loaded = await RoundTrip(result.Snapshot);
        Assert.Equal(field.Instruction, Assert.Single(loaded.Nodes.Values.OfType<RichField>()).Instruction);
    }

    [Fact]
    public async Task LimitsCancellationAndStreamOwnershipAreEnforced()
    {
        using var input = new MemoryStream(Encoding.ASCII.GetBytes(@"{\rtf1 abcdef}"));
        await Assert.ThrowsAsync<FormatException>(() => RtfCodec.ReadAsync(input, new() { MaximumTextLength = 3 }, TestContext.Current.CancellationToken));
        Assert.True(input.CanRead);
        using var output = new MemoryStream();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RtfCodec.WriteAsync(RichDocumentSnapshot.Empty, output, new(true)));
        Assert.Equal(0, output.Length);
        Assert.True(output.CanWrite);
    }

    [Fact]
    public async Task ResourceBudgetIncludesUnreferencedDefinitionsPendingAnchorsAndDiagnostics()
    {
        var entries = Enumerable.Range(1, 20);
        string Join(Func<int, string> entry) => string.Concat(entries.Select(entry));
        var payloads = new[]
        {
            @"{\fonttbl" + Join(index => $@"{{\f{index} Font{index};}}") + "}",
            @"{\colortbl;" + Join(index => $@"\red{index};") + "}",
            @"{\stylesheet" + Join(index => $@"{{\s{index} Style{index};}}") + "}",
            @"{\*\listtable" + Join(index => $@"{{\list\listid{index}}}") + "}",
            @"{\*\listoverridetable" + Join(index => $@"{{\listoverride\listid{index}\ls{index}}}") + "}",
            Join(index => $@"{{\*\bkmkstart bookmark{index}}}"),
            Join(index => $@"\unknown{(char)('a' + index)} "),
        };
        foreach (var payload in payloads)
        {
            using var input = new MemoryStream(Encoding.ASCII.GetBytes(@"{\rtf1" + payload + " x}"));
            var error = await Assert.ThrowsAsync<FormatException>(() => RtfCodec.ReadAsync(input, new() { MaximumNodes = 8 }, TestContext.Current.CancellationToken));
            Assert.Contains("node/resource limit", error.Message);
            Assert.True(input.CanRead);
        }
    }

    [Fact]
    public async Task StreamingDecoderRetainsDbcsPairsAcrossBufferBoundaries()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var expected = "a" + new string('あ', 10_000);
        using var stream = new MemoryStream();
        stream.Write(Encoding.ASCII.GetBytes(@"{\rtf1\ansi\ansicpg932 "));
        stream.Write(Encoding.GetEncoding(932).GetBytes(expected));
        stream.WriteByte((byte)'}');
        stream.Position = 0;
        var result = await RtfCodec.ReadAsync(stream, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(expected, result.Snapshot.ProjectText().Text.ToString());
    }

    [Fact]
    public async Task LinksFieldsAndCharacterStylesRetainAuthoredSemantics()
    {
        var snapshot = RichDocumentSnapshot.Build(edit =>
        {
            edit.SetStyles(new([new(new(5), "Link style", RichStyleKind.Character) { Character = new() { Underline = RichUnderline.Single } }]));
            edit.ReplaceText(new(edit.Body, new(2), 0), 0, "one two");
            edit.SetHyperlink(new(edit.Body, new(2), 0), 3, new("https://example.test/a?x=\"q\"&y=\\", "Open \"page\""));
            edit.SetCharacterStyle(new(edit.Body, new(2), 0), 3, new(5));
            edit.SetParagraphCharacterFormat(new(2), new() { Italic = true });
            var field = edit.InsertField(new(edit.Body, new(2), 7), " DATE \\@ \"yyyy-MM-dd\" ", "2026-10-11");
            edit.SetFieldResult(field, "2026-10-12");
            edit.SetHyperlink(new(edit.Body, new(2), 7), 1, null);
            edit.InsertField(new(edit.Body, new(2), 8), "HYPERLINK \"#bookmark\"", "");
        });
        var loaded = await RoundTrip(snapshot);
        Assert.Equal("one two\uFFFC\uFFFC", loaded.ProjectText().Text.ToString());
        var link = Assert.Single(loaded.Nodes.Values.OfType<RichText>(), run => run.Link is not null);
        Assert.Equal("one", link.Content.ToString());
        Assert.Equal(new RichStyleId(5), link.Style);
        Assert.Equal("https://example.test/a?x=\"q\"&y=\\", link.Link!.Target);
        Assert.Equal("Open \"page\"", link.Link.ToolTip);
        Assert.Equal(new RichCharacterStyle { Italic = true }, Assert.Single(loaded.Nodes.Values.OfType<RichParagraph>()).Character);
        var fields = loaded.Nodes.Values.OfType<RichField>().OrderBy(field => field.Id.Value).ToArray();
        Assert.Equal(2, fields.Length);
        Assert.Equal(" DATE \\@ \"yyyy-MM-dd\" ", fields[0].Instruction);
        Assert.Equal("2026-10-12", fields[0].Result);
        Assert.Equal("HYPERLINK \"#bookmark\"", fields[1].Instruction);
        Assert.Equal("", fields[1].Result);
    }

    [Fact]
    public async Task ExternalListsAndTablesImportAsStructure()
    {
        const string rtf = @"{\rtf1\ansi{\*\listtable{\list{\listlevel\levelnfc0\levelstartat3}\listid1}}{\*\listoverridetable{\listoverride\listid1\ls1}}
\pard\ls1 first\par\pard\ls1 second\par\pard tail\par
\trowd\cellx1000\cellx2000\intbl A\cell B\cell\row
\trowd\cellx1000\cellx2000\intbl C\cell D\cell\row}";
        using var input = new MemoryStream(Encoding.ASCII.GetBytes(rtf));
        var result = await RtfCodec.ReadAsync(input, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(result.Diagnostics);
        var list = Assert.Single(result.Snapshot.Nodes.Values.OfType<RichList>());
        Assert.True(list.Numbered);
        Assert.Equal(3, list.StartNumber);
        Assert.Equal(2, list.Items.Count);
        var table = Assert.Single(result.Snapshot.Nodes.Values.OfType<RichTable>());
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal("first\nsecond\ntail\nA\tB\nC\tD", result.Snapshot.ProjectText().Text.ToString());
    }

    private static async Task<RichDocumentSnapshot> RoundTrip(RichDocumentSnapshot snapshot)
    {
        using var output = new MemoryStream();
        await RtfCodec.WriteAsync(snapshot, output, TestContext.Current.CancellationToken);
        output.Position = 0;
        var result = await RtfCodec.ReadAsync(output, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(result.Diagnostics);
        return result.Snapshot;
    }

    private static string Shape(RichDocumentSnapshot snapshot)
    {
        return Visit(snapshot.Body);
        string Visit(RichNodeId id)
        {
            var node = snapshot.Get<RichNode>(id);
            var detail = node switch
            {
                RichText text => text.Content.ToString(),
                RichTable table => string.Join(',', table.Columns.Select(width => width.Twips)),
                RichTableCell cell => $"{cell.Column},{cell.ColumnSpan},{cell.RowSpan}",
                RichList list => $"{list.Numbered},{list.StartNumber}",
                _ => "",
            };
            return $"{node.GetType().Name}({detail})[" + string.Join('|', RichDocumentSnapshot.Children(node).Select(Visit)) + "]";
        }
    }
}
