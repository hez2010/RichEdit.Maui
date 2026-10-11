namespace RichEdit.Core.Rtf;

internal sealed partial class RtfDocumentReader
{
    private bool CellFormatControl(string word, int number)
    {
        if (_tables.Count == 0 || _tables[^1].Row is not { Complete: false }) return false;
        var frame = _tables[^1];
        var format = frame.CellFormat;
        var side = word switch { "clbrdrl" => RichBorderSides.Left, "clbrdrt" => RichBorderSides.Top,
            "clbrdrr" => RichBorderSides.Right, "clbrdrb" => RichBorderSides.Bottom, _ => RichBorderSides.None };
        if (side != RichBorderSides.None) { frame.CellBorderSide = side; return true; }
        if (frame.CellBorderSide != RichBorderSides.None && word is "brdrs" or "brdrdb" or "brdrdot" or "brdrdash" or "brdrnil" or "brdrnone" or "brdrw" or "brdrcf")
        {
            if (word == "brdrw" && number is < 0 or > 255) throw Error("Border widths must be from zero through 255 twips");
            var borders = format.Borders;
            var border = (frame.CellBorderSide switch { RichBorderSides.Left => borders.Left, RichBorderSides.Top => borders.Top,
                RichBorderSides.Right => borders.Right, _ => borders.Bottom }) ?? new RichBorderLine(RichBorderStyle.Single, new(0));
            border = word switch
            {
                "brdrs" => border with { Style = RichBorderStyle.Single }, "brdrdb" => border with { Style = RichBorderStyle.Double },
                "brdrdot" => border with { Style = RichBorderStyle.Dotted }, "brdrdash" => border with { Style = RichBorderStyle.Dashed },
                "brdrw" => border with { Width = new(number) }, "brdrcf" => border with { Color = Color(number) },
                "brdrnil" or "brdrnone" => null, _ => border,
            };
            frame.CellFormat = format with { Borders = frame.CellBorderSide switch
            { RichBorderSides.Left => borders with { Left = border }, RichBorderSides.Top => borders with { Top = border },
                RichBorderSides.Right => borders with { Right = border }, _ => borders with { Bottom = border } } };
            return true;
        }
        switch (word)
        {
            case "clvertalt": frame.CellFormat = format with { VerticalAlignment = RichCellVerticalAlignment.Top }; return true;
            case "clvertalc": frame.CellFormat = format with { VerticalAlignment = RichCellVerticalAlignment.Center }; return true;
            case "clvertalb": frame.CellFormat = format with { VerticalAlignment = RichCellVerticalAlignment.Bottom }; return true;
            case "clpadfl": case "clpadft": case "clpadfr": case "clpadfb":
                if (number is not (0 or 3)) Report("cell-padding-unit", "Unsupported cell padding units were replaced with row defaults.");
                side = PaddingSide(word);
                frame.CellPaddingUnits = number == 3 ? frame.CellPaddingUnits | side : frame.CellPaddingUnits & ~side;
                return true;
            case "clpadl": case "clpadt": case "clpadr": case "clpadb":
                if (number < 0) throw Error("Cell padding cannot be negative");
                frame.CellFormat = format with { Padding = word switch
                { "clpadl" => format.Padding with { Left = new(number) }, "clpadt" => format.Padding with { Top = new(number) },
                    "clpadr" => format.Padding with { Right = new(number) }, _ => format.Padding with { Bottom = new(number) } } }; return true;
            case "trgaph":
                if (number < 0) throw Error("Table cell gaps cannot be negative");
                frame.Row!.CellGap = number; return true;
            case "trpaddfl": case "trpaddft": case "trpaddfr": case "trpaddfb":
                if (number is not (0 or 3)) Report("row-padding-unit", "Unsupported row padding units were replaced with legacy cell gaps.");
                side = PaddingSide(word);
                frame.Row!.PaddingUnits = number == 3 ? frame.Row.PaddingUnits | side : frame.Row.PaddingUnits & ~side;
                return true;
            case "trpaddl": case "trpaddt": case "trpaddr": case "trpaddb":
                if (number < 0) throw Error("Row padding cannot be negative");
                var padding = frame.Row!.Padding;
                frame.Row.Padding = word switch
                { "trpaddl" => padding with { Left = new(number) }, "trpaddt" => padding with { Top = new(number) },
                    "trpaddr" => padding with { Right = new(number) }, _ => padding with { Bottom = new(number) } };
                return true;
            case "clshdng": case "clcfpat": case "clcbpat":
                if (word == "clshdng" && number is < 0 or > 10000) throw Error("Cell shading must be from zero through 10000");
                var shading = format.Shading ?? new RichShading(0);
                frame.CellFormat = format with { Shading = word switch
                { "clshdng" => new(number, shading.Foreground, shading.Background), "clcfpat" => new(shading.Coverage, Color(number), shading.Background),
                    _ => new(shading.Coverage, shading.Foreground, Color(number)) } }; return true;
            default: return false;
        }
    }

    private static RichBorderSides PaddingSide(string word) => word[^1] switch
    { 'l' => RichBorderSides.Left, 't' => RichBorderSides.Top, 'r' => RichBorderSides.Right, _ => RichBorderSides.Bottom };

    private static RichCellFormat ResolveCellFormat(Row row, CellSpec cell)
    {
        var padding = cell.Format.Padding;
        return cell.Format with { Padding = new(
            Resolve(RichBorderSides.Left, padding.Left, row.Padding.Left, row.CellGap),
            Resolve(RichBorderSides.Top, padding.Top, row.Padding.Top, 0),
            Resolve(RichBorderSides.Right, padding.Right, row.Padding.Right, row.CellGap),
            Resolve(RichBorderSides.Bottom, padding.Bottom, row.Padding.Bottom, 0)) };
        RichLength Resolve(RichBorderSides side, RichLength value, RichLength inherited, int gap) =>
            (cell.PaddingUnits & side) != 0 ? value : (row.PaddingUnits & side) != 0 ? inherited : new(gap);
    }
}
