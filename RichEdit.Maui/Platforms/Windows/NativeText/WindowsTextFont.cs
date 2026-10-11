using Microsoft.UI.Text;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Controls.RichEdit;

namespace RichEdit.Maui.Platforms.Windows.NativeText;

internal sealed unsafe partial class WindowsTextFont(WindowsTextServices owner, nint pointer)
    : WindowsTextObject<ITextFont2>(owner, pointer), ITextCharacterFormat
{
    public FormatEffect AllCaps
    {
        get { int value; Pointer->GetAllCaps(&value); return WindowsTextValues.Effect(value); }
        set => Pointer->SetAllCaps(WindowsTextValues.Effect(value));
    }
    public FormatEffect Bold
    {
        get { int value; Pointer->GetBold(&value); return WindowsTextValues.Effect(value); }
        set => Pointer->SetBold(WindowsTextValues.Effect(value));
    }
    public FormatEffect Hidden
    {
        get { int value; Pointer->GetHidden(&value); return WindowsTextValues.Effect(value); }
        set => Pointer->SetHidden(WindowsTextValues.Effect(value));
    }
    public FormatEffect Italic
    {
        get { int value; Pointer->GetItalic(&value); return WindowsTextValues.Effect(value); }
        set => Pointer->SetItalic(WindowsTextValues.Effect(value));
    }
    public FormatEffect Outline
    {
        get { int value; Pointer->GetOutline(&value); return WindowsTextValues.Effect(value); }
        set => Pointer->SetOutline(WindowsTextValues.Effect(value));
    }
    public FormatEffect ProtectedText
    {
        get { int value; Pointer->GetProtected(&value); return WindowsTextValues.Effect(value); }
        set => Pointer->SetProtected(WindowsTextValues.Effect(value));
    }
    public FormatEffect SmallCaps
    {
        get { int value; Pointer->GetSmallCaps(&value); return WindowsTextValues.Effect(value); }
        set => Pointer->SetSmallCaps(WindowsTextValues.Effect(value));
    }
    public FormatEffect Strikethrough
    {
        get { int value; Pointer->GetStrikeThrough(&value); return WindowsTextValues.Effect(value); }
        set => Pointer->SetStrikeThrough(WindowsTextValues.Effect(value));
    }
    public FormatEffect Subscript
    {
        get { int value; Pointer->GetSubscript(&value); return WindowsTextValues.Effect(value); }
        set => Pointer->SetSubscript(WindowsTextValues.Effect(value));
    }
    public FormatEffect Superscript
    {
        get { int value; Pointer->GetSuperscript(&value); return WindowsTextValues.Effect(value); }
        set => Pointer->SetSuperscript(WindowsTextValues.Effect(value));
    }
    public float Kerning
    {
        get { float value; Pointer->GetKerning(&value); return value; }
        set => Pointer->SetKerning(value);
    }
    public float Position
    {
        get { float value; Pointer->GetPosition(&value); return value; }
        set => Pointer->SetPosition(value);
    }
    public float Size
    {
        get { float value; Pointer->GetSize(&value); return value; }
        set => Pointer->SetSize(value);
    }
    public float Spacing
    {
        get { float value; Pointer->GetSpacing(&value); return value; }
        set => Pointer->SetSpacing(value);
    }
    public global::Windows.UI.Color BackgroundColor
    {
        get { int value; Pointer->GetBackColor(&value); return WindowsTextValues.Color(value); }
        set => Pointer->SetBackColor(WindowsTextValues.Color(value));
    }
    public global::Windows.UI.Color ForegroundColor
    {
        get { int value; Pointer->GetForeColor(&value); return WindowsTextValues.Color(value); }
        set => Pointer->SetForeColor(WindowsTextValues.Color(value));
    }
    public global::Windows.UI.Text.FontStretch FontStretch
    {
        get { int value; Pointer->GetProperty((int)tomConstants.tomFontStretch, &value); return (global::Windows.UI.Text.FontStretch)(value < 0 ? 0 : value); }
        set => Pointer->SetProperty((int)tomConstants.tomFontStretch, (int)value);
    }
    public global::Windows.UI.Text.FontStyle FontStyle
    {
        get { int value; Pointer->GetProperty((int)tomConstants.tomFontStyle, &value); return (global::Windows.UI.Text.FontStyle)(value < 0 ? 0 : value); }
        set => Pointer->SetProperty((int)tomConstants.tomFontStyle, (int)value);
    }
    public string Name
    {
        get { BSTR value; Pointer->GetName(&value); return WindowsTextValues.TakeString(value); }
        set { using var text = new WindowsTextValues.Text(value); Pointer->SetName(text.Value); }
    }
    public string LanguageTag
    {
        get
        {
            int value; Pointer->GetLanguageID(&value);
            return value <= 0 ? "" : System.Globalization.CultureInfo.GetCultureInfo(value).IetfLanguageTag;
        }
        set => Pointer->SetLanguageID(string.IsNullOrEmpty(value) || value == "und" ? 0 : System.Globalization.CultureInfo.GetCultureInfo(value).LCID);
    }
    public LinkType LinkType { get { int value; Pointer->GetLinkType(&value); return (LinkType)WindowsTextValues.Option(value); } }
    public TextScript TextScript
    {
        get { int value; Pointer->GetCharRep(&value); return (TextScript)WindowsTextValues.Option(value); }
        set => Pointer->SetCharRep(WindowsTextValues.NativeOption((int)value));
    }
    public UnderlineType Underline
    {
        get { int value; Pointer->GetUnderline(&value); return (UnderlineType)WindowsTextValues.Option(value); }
        set => Pointer->SetUnderline(WindowsTextValues.NativeOption((int)value));
    }
    public int Weight { get { int value; Pointer->GetWeight(&value); return value; } set => Pointer->SetWeight(value); }
    public ITextCharacterFormat GetClone()
    {
        ITextFont2* value; Pointer->GetDuplicate2(&value); return new WindowsTextFont(Owner, (nint)value);
    }
    public void SetClone(ITextCharacterFormat value)
    {
        var source = RequireNative(value);
        int style;
        source.Pointer->GetProperty((int)tomConstants.tomFontStyle, &style);
        Pointer->SetDuplicate((ITextFont*)source.Pointer);
        // TOM1 cloning applies the italic effect and can reset TOM2's separate
        // oblique style. Preserve that defined property from the same clone.
        if (style != (int)tomConstants.tomUndefined)
            Pointer->SetProperty((int)tomConstants.tomFontStyle, style);
    }
    public bool IsEqual(ITextCharacterFormat value)
    {
        if (value is not WindowsTextFont native) return false;
        int equal; Pointer->IsEqual((ITextFont*)native.Pointer, &equal); return equal != 0;
    }
    internal static WindowsTextFont RequireNative(ITextCharacterFormat value) => value as WindowsTextFont
        ?? throw new ArgumentException("The format must come from the same Windows text backend.", nameof(value));
}
