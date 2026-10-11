using Microsoft.UI.Text;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Controls.RichEdit;

namespace RichEdit.Maui.Platforms.Windows.NativeText;

internal sealed unsafe partial class WindowsTextParagraph(WindowsTextServices owner, nint pointer)
    : WindowsTextObject<ITextPara2>(owner, pointer), ITextParagraphFormat
{
    public FormatEffect KeepTogether
    {
        get { tomConstants value; Pointer->GetKeepTogether(&value); return WindowsTextValues.Effect((int)value); }
        set => Pointer->SetKeepTogether(WindowsTextValues.Effect(value));
    }
    public FormatEffect KeepWithNext
    {
        get { tomConstants value; Pointer->GetKeepWithNext(&value); return WindowsTextValues.Effect((int)value); }
        set => Pointer->SetKeepWithNext(WindowsTextValues.Effect(value));
    }
    public FormatEffect NoLineNumber
    {
        get { int value; Pointer->GetNoLineNumber(&value); return WindowsTextValues.Effect((int)value); }
        set => Pointer->SetNoLineNumber(WindowsTextValues.Effect(value));
    }
    public FormatEffect PageBreakBefore
    {
        get { int value; Pointer->GetPageBreakBefore(&value); return WindowsTextValues.Effect((int)value); }
        set => Pointer->SetPageBreakBefore(WindowsTextValues.Effect(value));
    }
    public FormatEffect WidowControl
    {
        get { int value; Pointer->GetWidowControl(&value); return WindowsTextValues.Effect((int)value); }
        set => Pointer->SetWidowControl(WindowsTextValues.Effect(value));
    }
    public float FirstLineIndent
    {
        get { float value; Pointer->GetFirstLineIndent(&value); return value; }
    }
    public float LeftIndent
    {
        get { float value; Pointer->GetLeftIndent(&value); return value; }
    }
    public float LineSpacing
    {
        get { float value; Pointer->GetLineSpacing(&value); return value; }
    }
    public float RightIndent
    {
        get { float value; Pointer->GetRightIndent(&value); return value; }
        set => Pointer->SetRightIndent(value);
    }
    public float SpaceAfter
    {
        get { float value; Pointer->GetSpaceAfter(&value); return value; }
        set => Pointer->SetSpaceAfter(value);
    }
    public float SpaceBefore
    {
        get { float value; Pointer->GetSpaceBefore(&value); return value; }
        set => Pointer->SetSpaceBefore(value);
    }
    public float ListTab
    {
        get { float value; Pointer->GetListTab(&value); return value; }
        set => Pointer->SetListTab(value);
    }
    public int ListLevelIndex
    {
        get { int value; Pointer->GetListLevelIndex(&value); return value; }
        set => Pointer->SetListLevelIndex(value);
    }
    public int ListStart
    {
        get { int value; Pointer->GetListStart(&value); return value; }
        set => Pointer->SetListStart(value);
    }
    public int TabCount
    {
        get { int value; Pointer->GetTabCount(&value); return value; }
    }
    public ParagraphAlignment Alignment
    {
        get { int value; Pointer->GetAlignment(&value); return (ParagraphAlignment)WindowsTextValues.Option(value); }
        set => Pointer->SetAlignment(WindowsTextValues.NativeOption((int)value));
    }
    public LineSpacingRule LineSpacingRule
    {
        get { int value; Pointer->GetLineSpacingRule(&value); return (LineSpacingRule)WindowsTextValues.Option(value); }
    }
    public MarkerAlignment ListAlignment
    {
        get { int value; Pointer->GetListAlignment(&value); return (MarkerAlignment)WindowsTextValues.Option(value); }
        set => Pointer->SetListAlignment(WindowsTextValues.NativeOption((int)value));
    }
    public ParagraphStyle Style
    {
        get { int value; Pointer->GetStyle(&value); return value == (int)tomConstants.tomUndefined ? ParagraphStyle.Undefined : (ParagraphStyle)(1 - value); }
        set => Pointer->SetStyle(value == ParagraphStyle.Undefined ? (int)tomConstants.tomUndefined : 1 - (int)value);
    }
    public FormatEffect RightToLeft
    {
        get
        {
            int value, mask; Pointer->GetEffects(&value, &mask);
            return (mask & 1) == 0 ? FormatEffect.Undefined : (value & 1) != 0 ? FormatEffect.On : FormatEffect.Off;
        }
        set
        {
            if (value == FormatEffect.Undefined) return;
            var enabled = value == FormatEffect.On || value == FormatEffect.Toggle && RightToLeft != FormatEffect.On;
            Pointer->SetEffects(enabled ? 1 : 0, 1);
        }
    }
    private int NativeList { get { int value; Pointer->GetListType(&value); return value; } set => Pointer->SetListType(value); }
    public MarkerType ListType
    {
        get { var value = NativeList; return value == (int)tomConstants.tomUndefined ? MarkerType.Undefined : (MarkerType)((value & 0xffff) + 1); }
        set { var old = NativeList; NativeList = value == MarkerType.Undefined ? (int)tomConstants.tomUndefined : (old < 0 ? 0 : old & ~0xffff) | ((int)value - 1); }
    }
    public MarkerStyle ListStyle
    {
        get => NativeList switch
        {
            (int)tomConstants.tomUndefined => MarkerStyle.Undefined,
            var value => (value & 0xf0000) switch
            {
                0 => MarkerStyle.Parenthesis, 0x10000 => MarkerStyle.Parentheses,
                0x20000 => MarkerStyle.Period, 0x30000 => MarkerStyle.Plain,
                0x40000 => MarkerStyle.NoNumber, 0x80000 => MarkerStyle.Minus,
                _ => MarkerStyle.Undefined,
            },
        };
        set
        {
            if (value == MarkerStyle.Undefined) { NativeList = (int)tomConstants.tomUndefined; return; }
            var bits = value switch
            {
                MarkerStyle.Parenthesis => 0, MarkerStyle.Parentheses => 0x10000,
                MarkerStyle.Period => 0x20000, MarkerStyle.Plain => 0x30000,
                MarkerStyle.NoNumber => 0x40000, MarkerStyle.Minus => 0x80000,
                _ => throw new ArgumentOutOfRangeException(nameof(value)),
            };
            var old = NativeList; NativeList = (old < 0 ? 0 : old & 0xffff) | bits;
        }
    }
    public void SetIndents(float firstLine, float left, float right) => Pointer->SetIndents(firstLine, left, right);
    public void SetLineSpacing(LineSpacingRule rule, float value) => Pointer->SetLineSpacing(WindowsTextValues.NativeOption((int)rule), value);
    public void AddTab(float position, TabAlignment alignment, TabLeader leader) => Pointer->AddTab(position, (int)alignment, (int)leader);
    public void ClearAllTabs() => Pointer->ClearAllTabs();
    public void DeleteTab(float position) => Pointer->DeleteTab(position);
    public void GetTab(int index, out float position, out TabAlignment alignment, out TabLeader leader)
    {
        float p; int a, l; Pointer->GetTab(index, &p, &a, &l);
        position = p; alignment = (TabAlignment)a; leader = (TabLeader)l;
    }
    public ITextParagraphFormat GetClone()
    {
        ITextPara2* value; Pointer->GetDuplicate2(&value); return new WindowsTextParagraph(Owner, (nint)value);
    }
    public void SetClone(ITextParagraphFormat value) => Pointer->SetDuplicate((ITextPara*)RequireNative(value).Pointer);
    public bool IsEqual(ITextParagraphFormat value)
    {
        if (value is not WindowsTextParagraph native) return false;
        int equal; Pointer->IsEqual((ITextPara*)native.Pointer, &equal); return equal != 0;
    }
    internal static WindowsTextParagraph RequireNative(ITextParagraphFormat value) => value as WindowsTextParagraph
        ?? throw new ArgumentException("The format must come from the same Windows text backend.", nameof(value));
}
