#if WINDOWS
using Microsoft.UI.Xaml.Controls;
using NativeMenuFlyout = Microsoft.UI.Xaml.Controls.MenuFlyout;
using NativeMenuFlyoutItem = Microsoft.UI.Xaml.Controls.MenuFlyoutItem;
using NativeMenuFlyoutSubItem = Microsoft.UI.Xaml.Controls.MenuFlyoutSubItem;
using NativeMenuFlyoutSeparator = Microsoft.UI.Xaml.Controls.MenuFlyoutSeparator;
using WinRT;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private readonly Dictionary<TextCommandBarFlyout, (ICommandBarElement[] Primary, ICommandBarElement[] Secondary)> _textFlyoutContents = [];

    private static EditorKey GetEditorKey(Windows.System.VirtualKey key) =>
        Enum.IsDefined((EditorKey)key) ? (EditorKey)key : EditorKey.None;

    private static EditorKeyModifiers GetEditorModifiers()
    {
        var modifiers = EditorKeyModifiers.None;
        if (IsShiftKeyDown()) modifiers |= EditorKeyModifiers.Shift;
        if (IsControlKeyDown()) modifiers |= EditorKeyModifiers.Control;
        if (IsAltKeyDown()) modifiers |= EditorKeyModifiers.Alt;
        if ((GetNativeKeyState(0x5B) & KeyPressedMask) != 0 || (GetNativeKeyState(0x5C) & KeyPressedMask) != 0)
            modifiers |= EditorKeyModifiers.Meta;
        if (IsControlKeyDown() && (GetNativeKeyState(0xA5) & KeyPressedMask) != 0)
            modifiers |= EditorKeyModifiers.AltGraph;
        return modifiers;
    }

    private void CustomizeTextFlyout(TextCommandBarFlyout flyout)
    {
        var menu = VirtualView.CreateContextMenu();
        if (menu.IncludeDefaultItems && menu.Items.Count == 0) return;
        _textFlyoutContents[flyout] = ([.. flyout.PrimaryCommands], [.. flyout.SecondaryCommands]);
        try
        {
            if (!menu.IncludeDefaultItems)
            {
                flyout.PrimaryCommands.Clear();
                flyout.SecondaryCommands.Clear();
            }
            foreach (var item in menu.Items)
            {
                if (item is IMenuFlyoutSeparator)
                {
                    flyout.SecondaryCommands.Add(new AppBarSeparator());
                    continue;
                }
                var button = new AppBarButton { Label = item.Text, IsEnabled = EditorMenu.CanExecute(item) };
                if (item is IMenuFlyoutSubItem submenu) button.Flyout = CreateMenuFlyout(submenu, flyout.Hide);
                else button.Click += (_, _) => { EditorMenu.Execute(item); flyout.Hide(); };
                flyout.SecondaryCommands.Add(button);
            }
        }
        catch
        {
            RestoreTextFlyout(flyout);
            throw;
        }
    }

    private static NativeMenuFlyout CreateMenuFlyout(IEnumerable<IMenuElement> items, Action close)
    {
        var flyout = new NativeMenuFlyout();
        foreach (var item in items) flyout.Items.Add(CreateMenuItem(item, close));
        return flyout;
    }

    private static MenuFlyoutItemBase CreateMenuItem(IMenuElement item, Action close)
    {
        if (item is IMenuFlyoutSeparator) return new NativeMenuFlyoutSeparator();
        if (item is IMenuFlyoutSubItem submenu)
        {
            var parent = new NativeMenuFlyoutSubItem { Text = item.Text, IsEnabled = EditorMenu.CanExecute(item) };
            foreach (var child in submenu) parent.Items.Add(CreateMenuItem(child, close));
            return parent;
        }
        var button = new NativeMenuFlyoutItem { Text = item.Text, IsEnabled = EditorMenu.CanExecute(item) };
        button.Click += (_, _) => { EditorMenu.Execute(item); close(); };
        return button;
    }

    [DynamicWindowsRuntimeCast(typeof(TextCommandBarFlyout))]
    private void OnTextFlyoutClosed(object? sender, object args)
    {
        if (sender is TextCommandBarFlyout flyout) RestoreTextFlyout(flyout);
    }

    private void RestoreTextFlyout(TextCommandBarFlyout flyout)
    {
        if (!_textFlyoutContents.Remove(flyout, out var contents)) return;
        flyout.PrimaryCommands.Clear();
        flyout.SecondaryCommands.Clear();
        foreach (var item in contents.Primary) flyout.PrimaryCommands.Add(item);
        foreach (var item in contents.Secondary) flyout.SecondaryCommands.Add(item);
    }
}
#endif
