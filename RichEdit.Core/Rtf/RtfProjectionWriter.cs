using System.Collections.Immutable;
using System.Globalization;
using TextEdit.Core;

namespace RichEdit.Core.Rtf;

// A native display projection deliberately excludes annotations and authored
// cascade metadata. Persistence must use the semantic codec, not this projection.
internal sealed class RtfProjectionWriter(RichDocumentSnapshot snapshot, Stream output,
    string fallbackFont = "Arial", RichFontSize? fallbackSize = null, RichColor? fallbackColor = null)
{
    private readonly RtfOutput _output = new(output);
    private readonly Dictionary<string, int> _fonts = new(StringComparer.Ordinal);
    private readonly Dictionary<RichColor, int> _colors = [];
    private readonly Dictionary<RichNodeId, int> _lists = [];
    private CancellationToken _cancellation;

    internal async Task WriteAsync(CancellationToken cancellationToken = default)
    {
        _cancellation = cancellationToken;
        _fonts.Add(fallbackFont, 0);
        foreach (var list in snapshot.Nodes.Values.OfType<RichList>()) _lists.Add(list.Id, _lists.Count + 1);
        if (_lists.Count > 2000) throw new InvalidOperationException("RTF supports at most 2,000 list overrides.");
        foreach (var inline in snapshot.Nodes.Values.OfType<RichInline>())
        {
            var format = snapshot.ResolveCharacter(inline.Id);
            Collect(format);
        }
        Color(fallbackColor);
        foreach (var paragraph in snapshot.Nodes.Values.OfType<RichParagraph>())
        {
            Color(snapshot.ResolveParagraph(paragraph.Id).Background);
            Collect(snapshot.Styles.ResolveCharacter(paragraph.Style, defaults: snapshot.DefaultCharacter, paragraphDirect: paragraph.Character));
        }
        await S(@"{\rtf1\ansi\ansicpg1252\uc1\deff0{\fonttbl");
        foreach (var font in _fonts)
        {
            await S("{");
            await C("f", font.Value);
            await T(font.Key);
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
        await ListTables();
        await Blocks(snapshot.Get<RichStory>(snapshot.Body).Blocks, 0, false);
        await S("}");
        await _output.FlushAsync(_cancellation).ConfigureAwait(false);
    }

    private async Task Blocks(ImmutableList<RichNodeId> blocks, int tableLevel, bool terminateLast, RichList? listContext = null)
    {
        for (var index = 0; index < blocks.Count; index++)
        {
            _cancellation.ThrowIfCancellationRequested();
            var terminate = terminateLast || index + 1 < blocks.Count;
            switch (snapshot.Get<RichBlock>(blocks[index]))
            {
                case RichParagraph paragraph: await Paragraph(paragraph, tableLevel, terminate, listContext, index == 0); break;
                case RichTable table: await Table(table, tableLevel + 1); break;
                case RichList list:
                    for (var itemIndex = 0; itemIndex < list.Items.Count; itemIndex++)
                    {
                        var item = snapshot.Get<RichListItem>(list.Items[itemIndex]);
                        await Blocks(item.Blocks, tableLevel, terminate || itemIndex + 1 < list.Items.Count, list);
                    }
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
        await C(format.Alignment switch { RichAlignment.Center => "qc", RichAlignment.Right => "qr", RichAlignment.Justify => "qj", _ => "ql" });
        if (format.Direction != RichDirection.Automatic) await C(format.Direction == RichDirection.RightToLeft ? "rtlpar" : "ltrpar");
        var listIndent = list is null ? 0 : ListDepth(list) * 720;
        await C("li", checked(format.LeadingIndent.Twips + listIndent));
        await C("ri", format.TrailingIndent.Twips);
        await C("fi", firstInItem && list is not null ? -360 : format.FirstLineIndent.Twips);
        await C("sb", format.SpaceBefore.Twips);
        await C("sa", format.SpaceAfter.Twips);
        await C("keep", format.KeepTogether ? 1 : 0);
        await C("keepn", format.KeepWithNext ? 1 : 0);
        await C(format.WidowControl ? "widctlpar" : "nowidctlpar");
        if (format.HeadingLevel is { } heading) await C("outlinelevel", heading - 1);
        if (format.Background is { } background) { await C("shading", 10000); await C("cfpat", _colors[background]); }
        if (firstInItem && list is not null) { await C("ls", _lists[list.Id]); await C("ilvl", 0); }
        await Character(snapshot.Styles.ResolveCharacter(paragraph.Style, defaults: snapshot.DefaultCharacter, paragraphDirect: paragraph.Character));
        foreach (var id in paragraph.Inlines)
        {
            var inline = snapshot.Get<RichInline>(id);
            await S("{");
            await Character(snapshot.ResolveCharacter(id));
            switch (inline)
            {
                case RichText text:
                    foreach (var chunk in text.Content.GetChunks(new(0, text.Content.Length))) await _output.TextAsync(chunk, _cancellation).ConfigureAwait(false);
                    break;
                case RichSoftBreak: await C("line"); break;
                case RichImage image: await Image(image); break;
            }
            await S("}");
        }
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
            RichUnderline.Dashed => "uldash", RichUnderline.Wave => "ulwave", RichUnderline.Words => "ulw", _ => "ulnone",
        });
        await C("ulc", Index(format.UnderlineColor));
        await C("strike", format.Strike == RichStrike.Single ? 1 : 0);
        await C("striked", format.Strike == RichStrike.Double ? 1 : 0);
        await C("cf", Index(format.Foreground ?? fallbackColor));
        await C("highlight", Index(format.Background));
        await C(format.Script switch { RichScript.Superscript => "super", RichScript.Subscript => "sub", _ => "nosupersub" });
        await C("expndtw", format.CharacterSpacing.Twips);
        await C("scaps", format.SmallCaps ? 1 : 0);
        await C("caps", format.AllCaps ? 1 : 0);
        await C("outl", format.Outline ? 1 : 0);
        await C("shad", format.Shadow ? 1 : 0);
        await C("v", format.Hidden ? 1 : 0);
        if (format.Language is { } language) await C("lang", CultureInfo.GetCultureInfo(language).LCID);
        if (format.Direction != RichDirection.Automatic) await C(format.Direction == RichDirection.RightToLeft ? "rtlch" : "ltrch");
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
            if (level == 1) await C("row");
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
                await C("cellx", boundaries[column]);
            }
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
            await C("levelnfc", list.Numbered ? 0 : 23);
            await C("levelnfcn", list.Numbered ? 0 : 23);
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
    private void Color(RichColor? color) { if (color is { } value && !_colors.ContainsKey(value)) _colors.Add(value, _colors.Count + 1); }
    private int Index(RichColor? color) => color is { } value ? _colors[value] : 0;
    private ValueTask S(string syntax) => _output.SyntaxAsync(syntax, _cancellation);
    private ValueTask C(string control, int? number = null) => _output.ControlAsync(control, number, _cancellation);
    private ValueTask T(string text) => _output.TextAsync(text.AsMemory(), _cancellation);
}
