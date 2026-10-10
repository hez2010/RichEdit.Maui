using RichEdit.Maui;
using TextEdit.Core;

namespace CodeEdit.Maui;

public sealed partial class CodeEditor
{
    /// <summary>Gets synchronous rules that extend native source input before one atomic commit.</summary>
    public TextInputRules InputRules => TextView.InputRules;

    /// <summary>Occurs when rule failure or a conflicting plan rejects native input.</summary>
    public event EventHandler<RichTextInputRejectedEventArgs>? InputRejected
    {
        add => TextView.InputRejected += value;
        remove => TextView.InputRejected -= value;
    }
}
