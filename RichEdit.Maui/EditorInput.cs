using System.Collections.ObjectModel;
using System.Windows.Input;

namespace RichEdit.Maui;

/// <summary>Keys used by editor hardware-key events. Text insertion remains the native input system's responsibility.</summary>
public enum EditorKey
{
    /// <summary>An unmapped key.</summary>
    None = 0,
    /// <summary>The Backspace key.</summary>
    Backspace = 8,
    /// <summary>The Tab key.</summary>
    Tab = 9,
    /// <summary>The Enter key.</summary>
    Enter = 13,
    /// <summary>The Escape key.</summary>
    Escape = 27,
    /// <summary>The Space key.</summary>
    Space = 32,
    /// <summary>The Page Up key.</summary>
    PageUp = 33,
    /// <summary>The Page Down key.</summary>
    PageDown = 34,
    /// <summary>The End key.</summary>
    End = 35,
    /// <summary>The Home key.</summary>
    Home = 36,
    /// <summary>The Left Arrow key.</summary>
    Left = 37,
    /// <summary>The Up Arrow key.</summary>
    Up = 38,
    /// <summary>The Right Arrow key.</summary>
    Right = 39,
    /// <summary>The Down Arrow key.</summary>
    Down = 40,
    /// <summary>The Insert key.</summary>
    Insert = 45,
    /// <summary>The Delete key.</summary>
    Delete = 46,
    /// <summary>The 0 key.</summary>
    D0 = 48,
    /// <summary>The 1 key.</summary>
    D1 = 49,
    /// <summary>The 2 key.</summary>
    D2 = 50,
    /// <summary>The 3 key.</summary>
    D3 = 51,
    /// <summary>The 4 key.</summary>
    D4 = 52,
    /// <summary>The 5 key.</summary>
    D5 = 53,
    /// <summary>The 6 key.</summary>
    D6 = 54,
    /// <summary>The 7 key.</summary>
    D7 = 55,
    /// <summary>The 8 key.</summary>
    D8 = 56,
    /// <summary>The 9 key.</summary>
    D9 = 57,
    /// <summary>The A key.</summary>
    A = 65,
    /// <summary>The B key.</summary>
    B = 66,
    /// <summary>The C key.</summary>
    C = 67,
    /// <summary>The D key.</summary>
    D = 68,
    /// <summary>The E key.</summary>
    E = 69,
    /// <summary>The F key.</summary>
    F = 70,
    /// <summary>The G key.</summary>
    G = 71,
    /// <summary>The H key.</summary>
    H = 72,
    /// <summary>The I key.</summary>
    I = 73,
    /// <summary>The J key.</summary>
    J = 74,
    /// <summary>The K key.</summary>
    K = 75,
    /// <summary>The L key.</summary>
    L = 76,
    /// <summary>The M key.</summary>
    M = 77,
    /// <summary>The N key.</summary>
    N = 78,
    /// <summary>The O key.</summary>
    O = 79,
    /// <summary>The P key.</summary>
    P = 80,
    /// <summary>The Q key.</summary>
    Q = 81,
    /// <summary>The R key.</summary>
    R = 82,
    /// <summary>The S key.</summary>
    S = 83,
    /// <summary>The T key.</summary>
    T = 84,
    /// <summary>The U key.</summary>
    U = 85,
    /// <summary>The V key.</summary>
    V = 86,
    /// <summary>The W key.</summary>
    W = 87,
    /// <summary>The X key.</summary>
    X = 88,
    /// <summary>The Y key.</summary>
    Y = 89,
    /// <summary>The Z key.</summary>
    Z = 90,
    /// <summary>The numeric keypad 0 key.</summary>
    NumPad0 = 96,
    /// <summary>The numeric keypad 1 key.</summary>
    NumPad1 = 97,
    /// <summary>The numeric keypad 2 key.</summary>
    NumPad2 = 98,
    /// <summary>The numeric keypad 3 key.</summary>
    NumPad3 = 99,
    /// <summary>The numeric keypad 4 key.</summary>
    NumPad4 = 100,
    /// <summary>The numeric keypad 5 key.</summary>
    NumPad5 = 101,
    /// <summary>The numeric keypad 6 key.</summary>
    NumPad6 = 102,
    /// <summary>The numeric keypad 7 key.</summary>
    NumPad7 = 103,
    /// <summary>The numeric keypad 8 key.</summary>
    NumPad8 = 104,
    /// <summary>The numeric keypad 9 key.</summary>
    NumPad9 = 105,
    /// <summary>The numeric keypad multiply key.</summary>
    Multiply = 106,
    /// <summary>The numeric keypad add key.</summary>
    Add = 107,
    /// <summary>The numeric keypad subtract key.</summary>
    Subtract = 109,
    /// <summary>The numeric keypad decimal key.</summary>
    Decimal = 110,
    /// <summary>The numeric keypad divide key.</summary>
    Divide = 111,
    /// <summary>The F1 key.</summary>
    F1 = 112,
    /// <summary>The F2 key.</summary>
    F2 = 113,
    /// <summary>The F3 key.</summary>
    F3 = 114,
    /// <summary>The F4 key.</summary>
    F4 = 115,
    /// <summary>The F5 key.</summary>
    F5 = 116,
    /// <summary>The F6 key.</summary>
    F6 = 117,
    /// <summary>The F7 key.</summary>
    F7 = 118,
    /// <summary>The F8 key.</summary>
    F8 = 119,
    /// <summary>The F9 key.</summary>
    F9 = 120,
    /// <summary>The F10 key.</summary>
    F10 = 121,
    /// <summary>The F11 key.</summary>
    F11 = 122,
    /// <summary>The F12 key.</summary>
    F12 = 123,
    /// <summary>The F13 key.</summary>
    F13 = 124,
    /// <summary>The F14 key.</summary>
    F14 = 125,
    /// <summary>The F15 key.</summary>
    F15 = 126,
    /// <summary>The F16 key.</summary>
    F16 = 127,
    /// <summary>The F17 key.</summary>
    F17 = 128,
    /// <summary>The F18 key.</summary>
    F18 = 129,
    /// <summary>The F19 key.</summary>
    F19 = 130,
    /// <summary>The F20 key.</summary>
    F20 = 131,
    /// <summary>The F21 key.</summary>
    F21 = 132,
    /// <summary>The F22 key.</summary>
    F22 = 133,
    /// <summary>The F23 key.</summary>
    F23 = 134,
    /// <summary>The F24 key.</summary>
    F24 = 135,
    /// <summary>The semicolon key.</summary>
    Semicolon = 186,
    /// <summary>The plus or equals key.</summary>
    Plus = 187,
    /// <summary>The comma key.</summary>
    Comma = 188,
    /// <summary>The minus key.</summary>
    Minus = 189,
    /// <summary>The period key.</summary>
    Period = 190,
    /// <summary>The slash key.</summary>
    Slash = 191,
    /// <summary>The backquote key.</summary>
    Backquote = 192,
    /// <summary>The opening bracket key.</summary>
    OpenBracket = 219,
    /// <summary>The backslash key.</summary>
    Backslash = 220,
    /// <summary>The closing bracket key.</summary>
    CloseBracket = 221,
    /// <summary>The quote key.</summary>
    Quote = 222,
}

/// <summary>Modifiers held during an editor hardware-key event.</summary>
[Flags]
public enum EditorKeyModifiers
{
    /// <summary>No modifier keys.</summary>
    None = 0,
    /// <summary>The Shift key.</summary>
    Shift = 1,
    /// <summary>The Control key.</summary>
    Control = 2,
    /// <summary>The Alt or Option key.</summary>
    Alt = 4,
    /// <summary>The Command key on Apple platforms or the Windows/Meta key elsewhere.</summary>
    Meta = 8,
    /// <summary>AltGr, in addition to any Control and Alt flags reported by the platform.</summary>
    AltGraph = 16,
}

/// <summary>A hardware-key press delivered before built-in editor shortcuts.</summary>
public sealed class EditorKeyEventArgs : EventArgs
{
    internal EditorKeyEventArgs(EditorKey key, EditorKeyModifiers modifiers)
    {
        Key = key;
        Modifiers = modifiers;
    }

    /// <summary>Gets the pressed key.</summary>
    public EditorKey Key { get; }
    /// <summary>Gets the held modifiers.</summary>
    public EditorKeyModifiers Modifiers { get; }
    /// <summary>Gets or sets whether the application consumed the key, suppressing default handling.</summary>
    public bool Handled { get; set; }
}

/// <summary>Customizes a native editor context or selection menu for one opening.</summary>
public sealed class EditorContextMenuEventArgs : EventArgs
{
    internal EditorContextMenuEventArgs(MenuFlyout? menu)
    {
        IncludeDefaultItems = menu is null;
        Items = menu is null ? [] : [.. menu.Cast<IMenuElement>()];
    }

    /// <summary>Gets the application menu items for this opening. Supports MAUI flyout items, submenus, and separators.</summary>
    public IList<IMenuElement> Items { get; }
    /// <summary>Gets or sets whether native editing, formatting, and proofing items are included.</summary>
    public bool IncludeDefaultItems { get; set; }
}

/// <summary>Maps one exact hardware-key gesture to an application command.</summary>
public sealed record EditorKeyBinding
{
    /// <summary>Creates a key binding. A disabled command leaves native key handling available.</summary>
    /// <param name="key">The hardware key.</param>
    /// <param name="modifiers">The exact held modifiers.</param>
    /// <param name="command">The command to invoke.</param>
    /// <param name="commandParameter">The value passed to the command.</param>
    public EditorKeyBinding(EditorKey key, EditorKeyModifiers modifiers, ICommand command, object? commandParameter = null)
    {
        if (key == EditorKey.None || !Enum.IsDefined(key)) throw new ArgumentOutOfRangeException(nameof(key));
        if ((modifiers & ~(EditorKeyModifiers.Shift | EditorKeyModifiers.Control | EditorKeyModifiers.Alt | EditorKeyModifiers.Meta | EditorKeyModifiers.AltGraph)) != 0)
            throw new ArgumentOutOfRangeException(nameof(modifiers));
        Key = key;
        Modifiers = modifiers;
        Command = command ?? throw new ArgumentNullException(nameof(command));
        CommandParameter = commandParameter;
    }

    /// <summary>Gets the hardware key.</summary>
    public EditorKey Key { get; }
    /// <summary>Gets the exact held modifiers.</summary>
    public EditorKeyModifiers Modifiers { get; }
    /// <summary>Gets the application command.</summary>
    public ICommand Command { get; }
    /// <summary>Gets the command parameter.</summary>
    public object? CommandParameter { get; }
}

/// <summary>An editor's UI-thread-owned key bindings. The last matching entry takes precedence.</summary>
public sealed partial class EditorKeyBindingCollection : Collection<EditorKeyBinding>
{
    private readonly RichEditor _editor;
    internal EditorKeyBindingCollection(RichEditor editor) => _editor = editor;
    internal long Version { get; private set; }

    /// <inheritdoc />
    protected override void InsertItem(int index, EditorKeyBinding item)
    {
        _editor.VerifyAccess();
        ArgumentNullException.ThrowIfNull(item);
        base.InsertItem(index, item);
        Version++;
    }
    /// <inheritdoc />
    protected override void SetItem(int index, EditorKeyBinding item)
    {
        _editor.VerifyAccess();
        ArgumentNullException.ThrowIfNull(item);
        base.SetItem(index, item);
        Version++;
    }
    /// <inheritdoc />
    protected override void RemoveItem(int index)
    {
        _editor.VerifyAccess();
        base.RemoveItem(index);
        Version++;
    }
    /// <inheritdoc />
    protected override void ClearItems()
    {
        _editor.VerifyAccess();
        base.ClearItems();
        Version++;
    }

    internal EditorKeyBinding? Find(EditorKey key, EditorKeyModifiers modifiers)
    {
        for (var index = Count - 1; index >= 0; index--)
        {
            var binding = this[index];
            if (binding.Key == key && binding.Modifiers == modifiers) return binding;
        }
        return null;
    }
}
