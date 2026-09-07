namespace RichEdit.Maui;

public sealed partial class RichEditor
{
    /// <summary>Gets the hardware-key command bindings. Mutate the collection on the editor's UI thread.</summary>
    public EditorKeyBindingCollection KeyBindings { get; }

    /// <summary>Occurs before built-in hardware-key handling, except while native text composition is active.</summary>
    public event EventHandler<EditorKeyEventArgs>? KeyDown;

    /// <summary>Customizes the native context or selection menu. Apple platforms require iOS or Mac Catalyst 16 or later.</summary>
    public event EventHandler<EditorContextMenuEventArgs>? ContextMenuOpening;

    internal bool SendKeyDown(EditorKey key, EditorKeyModifiers modifiers)
    {
        if (key == EditorKey.None) return false;
        var args = new EditorKeyEventArgs(key, modifiers);
        KeyDown?.Invoke(this, args);
        if (args.Handled) return true;
        if (KeyBindings.Find(key, modifiers) is not { } binding || !binding.Command.CanExecute(binding.CommandParameter)) return false;
        binding.Command.Execute(binding.CommandParameter);
        return true;
    }

    internal EditorContextMenuEventArgs CreateContextMenu()
    {
        var args = new EditorContextMenuEventArgs(FlyoutBase.GetContextFlyout(this) as MenuFlyout);
        ContextMenuOpening?.Invoke(this, args);
        EditorMenu.Validate(args.Items);
        return args;
    }
}

internal static class EditorMenu
{
    internal static void Validate(IEnumerable<IMenuElement> items)
    {
        foreach (var item in items)
        {
            switch (item)
            {
                case IMenuFlyoutSubItem submenu:
                    Validate(submenu);
                    break;
                case IMenuFlyoutItem or IMenuFlyoutSeparator:
                    break;
                default:
                    throw new ArgumentException("Editor menus support MenuFlyoutItem, MenuFlyoutSubItem, and MenuFlyoutSeparator.");
            }
        }
    }

    internal static bool CanExecute(IMenuElement item) => item.IsEnabled &&
        (item is not MenuItem { Command: { } command } menuItem || command.CanExecute(menuItem.CommandParameter));

    internal static void Execute(IMenuElement item)
    {
        if (CanExecute(item)) item.Clicked();
    }
}
