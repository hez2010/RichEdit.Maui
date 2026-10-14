namespace RichEdit.Core.Rtf;

internal sealed partial class RtfDocumentReader
{
    private readonly Dictionary<(bool Numbered, int Start, RichListNumberingStyle Style), int> _legacyLists = [];
    private int _nextLegacyList = int.MinValue;

    private sealed class LegacyList
    {
        internal readonly ListLevelDefinition Definition = new() { Numbered = true };
        internal int Level;
        internal bool Continue;
    }

    private bool LegacyListControl(string word, int number)
    {
        if (_state.LegacyList is not { } legacy) return false;
        switch (word)
        {
            case "pnlvl":
                if (number is < 1 or > 9) throw Error("RTF outline numbering levels must be from one through nine");
                legacy.Level = number - 1; return true;
            case "pnlvlblt": legacy.Definition.Numbered = false; return true;
            case "pnlvlbody": legacy.Level = 0; return true;
            case "pnlvlcont": legacy.Continue = true; return true;
            case "pnstart":
                legacy.Definition.Start = ListStart(number); return true;
            case "pndec": case "pnucrm": case "pnlcrm": case "pnucltr": case "pnlcltr":
                legacy.Definition.Numbered = true;
                legacy.Definition.NumberStyle = word switch
                {
                    "pnucrm" => RichListNumberingStyle.UpperRoman, "pnlcrm" => RichListNumberingStyle.LowerRoman,
                    "pnucltr" => RichListNumberingStyle.UpperLetter, "pnlcltr" => RichListNumberingStyle.LowerLetter,
                    _ => RichListNumberingStyle.Decimal,
                };
                return true;
            default: return false;
        }
    }

    private void FinishLegacyList(LegacyList legacy)
    {
        if (_state.ListOverride > 0) return; // Modern list properties take precedence over their compatibility data.
        if (legacy.Continue && _listStack.TryPeek(out var preceding) && preceding.External && preceding.List.Number < 0)
        {
            _state.ListOverride = preceding.List.Number;
            _state.ListLevel = preceding.Level;
            _state.ContinueList = true;
            return;
        }
        var definition = legacy.Definition;
        var key = (definition.Numbered, definition.Start, definition.NumberStyle);
        if (!_legacyLists.TryGetValue(key, out var number))
        {
            number = _nextLegacyList++;
            ChargeResource();
            _legacyLists[key] = number;
            var list = new ListDefinition();
            list.Default.Numbered = definition.Numbered;
            list.Default.Start = definition.Start;
            list.Default.NumberStyle = definition.NumberStyle;
            _listDefinitions.Add(number, list);
        }
        _state.ListOverride = number;
        _state.ListLevel = legacy.Level;
        _state.ContinueList = legacy.Continue;
    }
}
