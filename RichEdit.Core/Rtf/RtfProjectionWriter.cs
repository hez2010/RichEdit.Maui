using System.Collections.Immutable;
using System.Globalization;
using TextEdit.Core;

namespace RichEdit.Core.Rtf;

// Native display omits annotation and cascade metadata. Semantic persistence
// enters through RtfCodec so fidelity assessment happens before output begins.
internal sealed partial class RtfProjectionWriter(RichDocumentSnapshot snapshot, Stream output,
    string fallbackFont = "Arial", RichFontSize? fallbackSize = null, RichColor? fallbackColor = null, bool semantic = false,
    Func<RichNodeId, bool, string?>? marker = null, Func<string, string>? nativeText = null,
    RichCellSelection? selectedCells = null, RichColor? selectionBackground = null, RichColor? selectionForeground = null,
    bool includeIdentities = false, IReadOnlySet<RichNodeId>? contextScaffolding = null,
    IReadOnlySet<RichNodeId>? nativePrivateParagraphs = null)
{
    private readonly RtfOutput _output = new(output);
    private readonly Dictionary<string, int> _fonts = new(StringComparer.Ordinal);
    private readonly Dictionary<RichColor, int> _colors = [];
    private readonly Dictionary<RichNodeId, int> _lists = [];
    private CancellationToken _cancellation;
    private readonly HashSet<RichNodeId> _selectedCells = [];

    internal async Task WriteAsync(CancellationToken cancellationToken = default)
    {
        _cancellation = cancellationToken;
        includeIdentities |= semantic && !snapshot.Revisions.IsEmpty;
        if (includeIdentities && !semantic) throw new InvalidOperationException("Retained identities require semantic RTF.");
        if (!semantic && selectedCells is { } selection)
        {
            var grid = snapshot.GetTableGrid(selection.Table);
            selection = grid.Expand(selection);
            for (var row = selection.Row; row < selection.Row + selection.RowCount; row++)
                for (var column = selection.Column; column < selection.Column + selection.ColumnCount; column++) _selectedCells.Add(grid[row, column]);
            Color(selectionBackground ?? new RichColor(0, 120, 215));
            Color(selectionForeground ?? new RichColor(255, 255, 255));
        }
        _fonts.Add(fallbackFont, 0);
        if (marker is not null) Font("Arial");
        foreach (var list in snapshot.Nodes.Values.OfType<RichList>()) _lists.Add(list.Id, _lists.Count + 1);
        if (_lists.Count > 2000) throw new InvalidOperationException("RTF supports at most 2,000 list overrides.");
        foreach (var inline in snapshot.Nodes.Values.OfType<RichInline>())
        {
            var format = snapshot.ResolveCharacter(inline.Id);
            Collect(format);
        }
        Color(fallbackColor);
        foreach (var cell in snapshot.Nodes.Values.OfType<RichTableCell>())
        {
            Color(cell.Format.Shading?.Foreground); Color(cell.Format.Shading?.Background);
            foreach (var edge in new[] { cell.Format.Borders.Left, cell.Format.Borders.Top, cell.Format.Borders.Right, cell.Format.Borders.Bottom }) Color(edge?.Color);
        }
        foreach (var paragraph in snapshot.Nodes.Values.OfType<RichParagraph>())
        {
            Collect(snapshot.ResolveParagraph(paragraph.Id));
            Collect(snapshot.Styles.ResolveCharacter(paragraph.Style, defaults: snapshot.DefaultCharacter, paragraphDirect: paragraph.Character));
        }
        if (semantic) CollectSemanticResources();
        await S(@"{\rtf1\ansi\ansicpg1252\uc1\deff0{\fonttbl");
        foreach (var font in _fonts)
        {
            await S("{");
            await C("f", font.Value);
            await Name(font.Key);
            await S(";}");
        }
        await S(@"}{\colortbl;");
        foreach (var color in _colors.OrderBy(pair => pair.Value).Select(pair => pair.Key))
        {
            await C("red", color.Red);
            await C("green", color.Green);
            await C("blue", color.Blue);
            await S(";");
        }
        await S("}\n");
        if (semantic) await SemanticHeader();
        await ListTables();
        await Blocks(snapshot.Get<RichStory>(snapshot.Body).Blocks, 0, true);
        if (semantic) await OrphanAnnotations();
        if (semantic) await Revisions();
        await S("}");
        await _output.FlushAsync(_cancellation).ConfigureAwait(false);
    }

    private async Task Blocks(ImmutableList<RichNodeId> blocks, int tableLevel, bool terminateLast, RichList? listContext = null)
    {
        var firstListParagraph = true;
        for (var index = 0; index < blocks.Count; index++)
        {
            _cancellation.ThrowIfCancellationRequested();
            var terminate = terminateLast || index + 1 < blocks.Count;
            if (index > 0 && snapshot.Get<RichNode>(blocks[index - 1]) is RichTable && snapshot.Get<RichNode>(blocks[index]) is RichTable)
            {
                // Standard RTF needs a non-table paragraph between independent
                // adjacent tables. The marker identifies this structural gap;
                // it is not an authored paragraph in the document model.
                await C("pard");
                if (tableLevel > 0) { await C("intbl"); await C("itap", tableLevel); }
                await Character(snapshot.DefaultCharacter.Apply(new()));
                if (semantic) await S(@"{\*\regap}");
                if (marker?.Invoke(blocks[index - 1], true) is { } boundary) await ProjectionMarker(boundary);
                await C("par");
            }
            switch (snapshot.Get<RichBlock>(blocks[index]))
            {
                case RichParagraph paragraph:
                    var authored = nativePrivateParagraphs?.Contains(paragraph.Id) != true;
                    await Paragraph(paragraph, tableLevel, terminate, listContext, firstListParagraph && authored);
                    if (authored) firstListParagraph = false;
                    break;
                case RichTable table: await Table(table, tableLevel + 1); break;
                case RichList list:
                    if (semantic) { await S(@"{\*\relistbegin"); await C("ls", _lists[list.Id]); await S("}"); }
                    for (var itemIndex = 0; itemIndex < list.Items.Count; itemIndex++)
                    {
                        if (semantic) await S(@"{\*\relistitem}");
                        var item = snapshot.Get<RichListItem>(list.Items[itemIndex]);
                        await Blocks(item.Blocks, tableLevel, terminate || itemIndex + 1 < list.Items.Count, list);
                    }
                    if (semantic) await S(@"{\*\relistend}");
                    break;
                default: throw new NotSupportedException("The native RTF projection does not support this block kind.");
            }
        }
    }

    private async Task Paragraph(RichParagraph paragraph, int tableLevel, bool terminate, RichList? list, bool firstInItem)
    {
        await C("pard");
        if (tableLevel > 0) { await C("intbl"); await C("itap", tableLevel); }
        var format = snapshot.ResolveParagraph(paragraph.Id);
        await ParagraphProperties(format);
        var listIndent = list is null || semantic ? 0 : ListDepth(list) * 720;
        if (listIndent != 0) await C("li", checked(format.LeadingIndent.Twips + listIndent));
        if (firstInItem && list is not null && !semantic) await C("fi", -360);
        if (firstInItem && list is not null) { await C("ls", _lists[list.Id]); await C("ilvl", 0); }
        await Character(SelectionAppearance(paragraph.Id, snapshot.Styles.ResolveCharacter(paragraph.Style, defaults: snapshot.DefaultCharacter, paragraphDirect: paragraph.Character)));
        if (semantic)
        {
            if (paragraph.Style is { } style) await C("s", style.Value);
            await Masks("rep", RtfStyleState.Masks(paragraph.Format), paragraph.Format.Background.ApplyValue());
            await Masks("repch", RtfStyleState.Masks(paragraph.Character));
        }
        var position = 0;
        if (marker?.Invoke(paragraph.Id, false) is { } paragraphStart) await ProjectionMarker(paragraphStart);
        foreach (var id in paragraph.Inlines)
        {
            var inline = snapshot.Get<RichInline>(id);
            await S("{");
            if (includeIdentities && inline is RichText) await S(@"{\*\retext}");
            await Character(SelectionAppearance(paragraph.Id, snapshot.ResolveCharacter(id)));
            if (semantic)
            {
                if (inline.Style is { } style) await C("cs", style.Value);
                await Masks("rec", RtfStyleState.Masks(inline.Format));
            }
            if (inline.Link is { } link)
            {
                await S(@"{\field{\*\fldinst ");
                await T(RtfFieldSyntax.HyperlinkInstruction(link));
                await S(@"}{\fldrslt ");
            }
            if (marker?.Invoke(inline.Id, false) is { } inlineStart) await ProjectionMarker(inlineStart);
            switch (inline)
            {
                case RichText text:
                    if (semantic) await AnnotatedText(paragraph.Id, position, text.Content);
                    else foreach (var chunk in text.Content.GetChunks(new(0, text.Content.Length)))
                        await _output.TextAsync(nativeText is null ? chunk : nativeText(chunk.ToString()).AsMemory(), _cancellation).ConfigureAwait(false);
                    break;
                case RichSoftBreak:
                    if (semantic) await Anchors(paragraph.Id, position);
                    await C("line"); break;
                case RichImage image:
                    if (semantic) await Anchors(paragraph.Id, position);
                    await Image(image); break;
                case RichField field:
                    if (semantic) await Anchors(paragraph.Id, position);
                    await S(@"{\field");
                    if (semantic) await S(@"{\*\refieldatom}");
                    await S(@"{\*\fldinst "); await T(field.Instruction);
                    await S(@"}{\fldrslt "); await T(!semantic && field.Result.Length == 0 ? "□" : field.Result); await S("}}");
                    break;
            }
            if (marker?.Invoke(inline.Id, true) is { } inlineEnd) await ProjectionMarker(inlineEnd);
            if (inline.Link is not null) await S("}}");
            position += RichDocumentSnapshot.InlineLength(inline);
            await S("}");
        }
        if (semantic) await Anchors(paragraph.Id, position);
        if (marker?.Invoke(paragraph.Id, true) is { } paragraphEnd) await ProjectionMarker(paragraphEnd);
        if (terminate) await C("par");
        await S("\n");
    }

    private async Task Character(ResolvedCharacterStyle format)
    {
        await C("plain");
        await C("f", _fonts[format.FontFamily ?? fallbackFont]);
        await C("fs", (format.FontSize ?? fallbackSize ?? new RichFontSize(24)).HalfPoints);
        await C("b", format.Bold ? 1 : 0);
        await C("i", format.Italic ? 1 : 0);
        await C(format.Underline switch
        {
            RichUnderline.Single => "ul", RichUnderline.Double => "uldb", RichUnderline.Dotted => "uld",
            RichUnderline.Dashed => "uldash", RichUnderline.Wave => "ulwave", RichUnderline.Words => "ulw",
            RichUnderline.DashDot => "uldashd", RichUnderline.DashDotDot => "uldashdd", RichUnderline.Thick => "ulth",
            RichUnderline.DoubleWave => "ululdbwave", RichUnderline.HeavyWave => "ulhwave", RichUnderline.LongDash => "ulldash", _ => "ulnone",
        });
        await C("ulc", Index(format.UnderlineColor));
        await C("strike", format.Strike == RichStrike.Single ? 1 : 0);
        await C("striked", format.Strike == RichStrike.Double ? 1 : 0);
        await C("cf", Index(format.Foreground ?? fallbackColor));
        await C("highlight", Index(format.Background));
        if (format.Shading is { } shading)
        {
            await C("chshdng", shading.Coverage);
            await C("chcfpat", Index(shading.Foreground));
            await C("chcbpat", Index(shading.Background));
        }
        await C(format.Script switch { RichScript.Superscript => "super", RichScript.Subscript => "sub", _ => "nosupersub" });
        if (format.VerticalPosition.Kind == RichVerticalPositionKind.Offset)
            await C(format.VerticalPosition.HalfPoints < 0 ? "dn" : "up", Math.Abs(format.VerticalPosition.HalfPoints));
        await C("expndtw", format.CharacterSpacing.Twips);
        await C("charscalex", format.HorizontalScalePercent);
        await C("scaps", format.SmallCaps ? 1 : 0);
        await C("caps", format.AllCaps ? 1 : 0);
        await C("outl", format.Outline ? 1 : 0);
        await C("shad", format.Shadow ? 1 : 0);
        await C("v", format.Hidden ? 1 : 0);
        if (format.Language is { } language) await C("lang", CultureInfo.GetCultureInfo(language).LCID);
        if (format.Direction != RichDirection.Automatic) await C(format.Direction == RichDirection.RightToLeft ? "rtlch" : "ltrch");
    }

    private async Task ProjectionMarker(string text)
    {
        // A symbol font must not recode the temporary marker's ASCII identity.
        // This group is removed in the detached native document before display.
        await S(@"{\plain");
        await C("f", _fonts["Arial"]);
        await T(text);
        await S("}");
    }

    private async Task Table(RichTable table, int level)
    {
        var grid = snapshot.GetTableGrid(table.Id);
        var boundaries = new int[grid.Columns];
        var width = 0;
        for (var column = 0; column < boundaries.Length; column++) boundaries[column] = width = checked(width + table.Columns[column].Twips);
        for (var row = 0; row < grid.Rows; row++)
        {
            await RowProperties(row);
            for (var column = 0; column < grid.Columns; column++)
            {
                var id = grid[row, column];
                var extent = grid.GetExtent(id);
                if (extent.Row == row && extent.Column == column) await Blocks(snapshot.Get<RichTableCell>(id).Blocks, level, false);
                else { await C("pard"); await C("intbl"); await C("itap", level); }
                await C(level == 1 ? "cell" : "nestcell");
            }
            if (level == 1)
            {
                // Paragraph resets inside cells can replace RichEdit's pending
                // row properties. Repeat the descriptor at the row terminator;
                // this is standard RTF and retains widths, merges, and shading.
                await RowProperties(row);
                await C("row");
            }
            else
            {
                await S(@"{\*\nesttableprops ");
                await RowProperties(row);
                await C("nestrow");
                await S("}");
            }
        }
        await C("pard");
        if (level > 1) { await C("intbl"); await C("itap", level - 1); }

        async Task RowProperties(int row)
        {
            // RichEdit discards the table if a new nesting level is selected after
            // starting its row definition. UIKit also accepts this ordering.
            await C("itap", level);
            await C("trowd");
            await C("trgaph", 0);
            await C("trleft", 0);
            await C("trautofit", 0);
            await C("trftsWidth", 3);
            await C("trwWidth", width);
            for (var column = 0; column < grid.Columns; column++)
            {
                var extent = grid.GetExtent(grid[row, column]);
                if (extent.ColumnCount > 1) await C(extent.Column == column ? "clmgf" : "clmrg");
                if (extent.RowCount > 1) await C(extent.Row == row ? "clvmgf" : "clvmrg");
                await C("clftsWidth", 3);
                await C("clwWidth", table.Columns[column].Twips);
                await CellProperties(snapshot.Get<RichTableCell>(grid[row, column]).Format);
                if (IsSelectedCell(grid[row, column]))
                {
                    await C("clshdng", 10000);
                    await C("clcfpat", _colors[selectionBackground ?? new RichColor(0, 120, 215)]);
                    await C("clcbpat", _colors[selectionBackground ?? new RichColor(0, 120, 215)]);
                }
                await C("cellx", boundaries[column]);
            }
        }
    }

    private async Task CellProperties(RichCellFormat format)
    {
        await C(format.VerticalAlignment switch { RichCellVerticalAlignment.Center => "clvertalc", RichCellVerticalAlignment.Bottom => "clvertalb", _ => "clvertalt" });
        foreach (var (side, padding, border) in new[]
        {
            ("l", format.Padding.Left, format.Borders.Left), ("t", format.Padding.Top, format.Borders.Top),
            ("r", format.Padding.Right, format.Borders.Right), ("b", format.Padding.Bottom, format.Borders.Bottom),
        })
        {
            await C("clpadf" + side, 3); await C("clpad" + side, padding.Twips);
            await C("clbrdr" + side);
            if (border is null) { await C("brdrnil"); continue; }
            await C(border.Style switch { RichBorderStyle.Double => "brdrdb", RichBorderStyle.Dotted => "brdrdot", RichBorderStyle.Dashed => "brdrdash", _ => "brdrs" });
            await C("brdrw", border.Width.Twips); await C("brdrcf", Index(border.Color));
        }
        if (format.Shading is { } shading)
        {
            await C("clshdng", shading.Coverage); await C("clcfpat", Index(shading.Foreground)); await C("clcbpat", Index(shading.Background));
        }
    }

    private async Task ListTables()
    {
        if (_lists.Count == 0) return;
        await S(@"{\*\listtable ");
        foreach (var pair in _lists)
        {
            var list = snapshot.Get<RichList>(pair.Key);
            await S(@"{\list ");
            await C("listtemplateid", pair.Value);
            await C("listsimple", 1);
            await S(@"{\listlevel ");
            await C("levelnfc", list.Numbered ? (int)list.NumberStyle : 23);
            await C("levelnfcn", list.Numbered ? (int)list.NumberStyle : 23);
            await C("leveljc", 0);
            await C("leveljcn", 0);
            await C("levelfollow", 0);
            await C("levelstartat", list.StartNumber);
            if (list.Numbered) await S(@"{\leveltext\'02\'00.;}{\levelnumbers\'01;}");
            else { await S(@"{\leveltext\'01"); await T("•"); await S(@";}{\levelnumbers;}"); }
            await C("f", 0);
            await C("fi", -360);
            await C("li", ListDepth(list) * 720);
            await C("tx", ListDepth(list) * 720);
            await S("}");
            await C("listid", pair.Value);
            await S("}");
        }
        await S(@"}{\*\listoverridetable ");
        foreach (var pair in _lists)
        {
            await S(@"{\listoverride ");
            await C("listid", pair.Value);
            await C("listoverridecount", 0);
            await C("ls", pair.Value);
            await S("}");
        }
        await S("}");
    }

    private int ListDepth(RichList list)
    {
        var depth = 1;
        for (var parent = list.Parent; parent is { } id; parent = snapshot.Get<RichNode>(id).Parent)
            if (snapshot.Get<RichNode>(id) is RichList) depth++;
        return depth;
    }

    private void Collect(ResolvedCharacterStyle format)
    {
        Font(format.FontFamily);
        Color(format.Foreground);
        Color(format.Background);
        Color(format.UnderlineColor);
        Color(format.Shading?.Foreground);
        Color(format.Shading?.Background);
    }

    private void Collect(ResolvedParagraphStyle format)
    {
        Color(format.Background);
        Color(format.Shading?.Foreground);
        Color(format.Shading?.Background);
        Color(format.Border?.Color);
    }

    private async Task Image(RichImage image)
    {
        var asset = snapshot.Assets[image.Asset];
        await S(@"{\pict ");
        await C(asset.Kind switch
        {
            RichImageKind.Png => "pngblip", RichImageKind.Jpeg => "jpegblip", RichImageKind.Emf => "emfblip",
            RichImageKind.Wmf => "wmetafile", _ => "dibitmap",
        }, asset.Kind is RichImageKind.Wmf ? 8 : asset.Kind is RichImageKind.Dib ? 0 : null);
        await C("picwgoal", image.Width.Twips);
        await C("pichgoal", image.Height.Twips);
        if (semantic)
        {
            await S(@"{\*\blipuid ");
            await S(image.Asset.Value.ToString("x32", CultureInfo.InvariantCulture));
            await S("}");
            if (image.AlternativeText == "") await S(@"{\*\reemptyalt}");
        }
        if (image.AlternativeText is { Length: > 0 } text)
        {
            await S(@"{\*\picprop{\sp{\sn wzDescription}{\sv ");
            await T(text);
            await S("}}}");
        }
        await _output.HexAsync(asset.Data.AsMemory(), _cancellation).ConfigureAwait(false);
        await S("}");
    }

    private void Font(string? name) { if (name is not null && !_fonts.ContainsKey(name)) _fonts.Add(name, _fonts.Count); }
    private bool IsSelectedCell(RichNodeId id)
    {
        if (_selectedCells.Count == 0) return false;
        for (var node = snapshot.Get<RichNode>(id); ; node = snapshot.Get<RichNode>(node.Parent!.Value))
        { if (_selectedCells.Contains(node.Id)) return true; if (node.Parent is null) return false; }
    }
    private ResolvedCharacterStyle SelectionAppearance(RichNodeId paragraph, ResolvedCharacterStyle format) =>
        IsSelectedCell(paragraph) ? format with { Foreground = selectionForeground ?? new RichColor(255, 255, 255) } : format;
    private void Color(RichColor? color) { if (color is { } value && !_colors.ContainsKey(value)) _colors.Add(value, _colors.Count + 1); }
    private int Index(RichColor? color) => color is { } value ? _colors[value] : 0;
    private ValueTask S(string syntax) => _output.SyntaxAsync(syntax, _cancellation);
    private ValueTask C(string control, int? number = null) => _output.ControlAsync(control, number, _cancellation);
    private ValueTask T(string text) => _output.TextAsync(text.AsMemory(), _cancellation);
}
