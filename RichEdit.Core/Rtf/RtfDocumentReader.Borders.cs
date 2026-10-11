namespace RichEdit.Core.Rtf;

internal sealed partial class RtfDocumentReader
{
    private static readonly RichBorderSides[] BorderEdges = [RichBorderSides.Left, RichBorderSides.Top, RichBorderSides.Right, RichBorderSides.Bottom];
    private bool BorderControl(string word, int number)
    {
        var side = word switch { "brdrl" => RichBorderSides.Left, "brdrt" => RichBorderSides.Top, "brdrr" => RichBorderSides.Right,
            "brdrb" => RichBorderSides.Bottom, "box" => RichBorderSides.All, _ => RichBorderSides.None };
        if (side != RichBorderSides.None)
        {
            _state.BorderSide = side;
            foreach (var edge in BorderEdges.Where(edge => (side & edge) != 0))
                if (!_state.Borders.ContainsKey(edge)) _state.Borders = _state.Borders.Add(edge, new(edge, RichBorderStyle.Single, new(0)));
        }
        else
        {
            if (word is not ("brdrs" or "brdrdb" or "brdrdot" or "brdrdash" or "brdrnil" or "brdrnone" or "brdrw" or "brdrcf")) return false;
            if (word == "brdrw" && number is < 0 or > 255) throw Error("Border widths must be from zero through 255 twips");
            foreach (var edge in BorderEdges.Where(edge => (_state.BorderSide & edge) != 0))
            {
                if (word is "brdrnil" or "brdrnone") { _state.Borders = _state.Borders.Remove(edge); continue; }
                var border = _state.Borders.GetValueOrDefault(edge) ?? new(edge, RichBorderStyle.Single, new(0));
                border = word switch
                {
                    "brdrs" => border with { Style = RichBorderStyle.Single }, "brdrdb" => border with { Style = RichBorderStyle.Double },
                    "brdrdot" => border with { Style = RichBorderStyle.Dotted }, "brdrdash" => border with { Style = RichBorderStyle.Dashed },
                    "brdrw" => border with { Width = new(number) }, "brdrcf" => border with { Color = Color(number) }, _ => border,
                };
                _state.Borders = _state.Borders.SetItem(edge, border);
            }
        }
        var selected = _state.Borders.Values.FirstOrDefault(border => (border.Sides & _state.BorderSide) != 0) ?? _state.Borders.Values.FirstOrDefault();
        var sides = _state.Borders.Keys.Aggregate(RichBorderSides.None, (value, edge) => value | edge);
        _state.Paragraph = _state.Paragraph with { Border = selected is null ? null : selected with { Sides = sides } };
        return true;
    }
    private void ReportBorderFidelity(State state)
    {
        if (state.Borders.Values.Select(border => border with { Sides = RichBorderSides.All }).Distinct().Skip(1).Any())
            Report("paragraph-border-sides", "Different paragraph border styles on individual sides were approximated as one uniform border.");
    }
}
