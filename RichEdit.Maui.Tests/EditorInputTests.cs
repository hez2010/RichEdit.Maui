using CodeEdit.Maui;
using Microsoft.Maui;
using Microsoft.Maui.Controls;

namespace RichEdit.Maui.Tests;

[Collection("Native editor")]
public sealed class EditorInputTests
{
    [Fact]
    public Task ApplicationBindingsOverrideCodeDefaults() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new CodeEditor { Document = new("text"), SelectedRange = new(4, 0) };
        editor.KeyBindings.Add(new(EditorKey.Tab, EditorKeyModifiers.None, new Command(() => editor.Selection.ReplaceText("!"))));
        Assert.True(editor.TextView.SendKeyDown(EditorKey.Tab, EditorKeyModifiers.None));
        Assert.Equal("text!", editor.Document.Text);
        editor.Undo();
        Assert.Equal("text", editor.Document.Text);
        Assert.Equal(new RichTextRange(4, 0), editor.SelectedRange);
        editor.KeyBindings.RemoveAt(editor.KeyBindings.Count - 1);
        Assert.True(editor.TextView.SendKeyDown(EditorKey.Tab, EditorKeyModifiers.None));
        Assert.Equal("text    ", editor.Document.Text);
    });

    [Fact]
    public Task HandledKeyEventSuppressesBindings() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new CodeEditor();
        var invoked = 0;
        editor.KeyBindings.Add(new(EditorKey.K, EditorKeyModifiers.Control, new Command(() => invoked++)));
        editor.KeyDown += (sender, args) =>
        {
            Assert.Same(editor, sender);
            Assert.Equal(EditorKey.K, args.Key);
            Assert.Equal(EditorKeyModifiers.Control, args.Modifiers);
            args.Handled = true;
        };
        Assert.True(editor.TextView.SendKeyDown(EditorKey.K, EditorKeyModifiers.Control));
        Assert.Equal(0, invoked);
    });

    [Fact]
    public Task LastMatchingBindingOwnsTheGestureAndChecksItsParameter() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new RichEditor();
        var invoked = 0;
        var enabled = false;
        var parameter = new object();
        editor.KeyBindings.Add(new(EditorKey.S, EditorKeyModifiers.Control, new Command(() => invoked += 100)));
        editor.KeyBindings.Add(new(EditorKey.S, EditorKeyModifiers.Control,
            new Command<object>(value => { Assert.Same(parameter, value); invoked++; }, value => enabled && ReferenceEquals(value, parameter)), parameter));
        Assert.False(editor.SendKeyDown(EditorKey.S, EditorKeyModifiers.Control));
        Assert.Equal(0, invoked);
        enabled = true;
        Assert.True(editor.SendKeyDown(EditorKey.S, EditorKeyModifiers.Control));
        Assert.Equal(1, invoked);
    });

    [Fact]
    public Task ModifiersMatchExactlyAndAltGrDoesNotInvokeControlAltBinding() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new RichEditor();
        var invoked = 0;
        var modifiers = EditorKeyModifiers.Control | EditorKeyModifiers.Alt;
        editor.KeyBindings.Add(new(EditorKey.Q, modifiers, new Command(() => invoked++)));
        Assert.False(editor.SendKeyDown(EditorKey.Q, modifiers | EditorKeyModifiers.AltGraph));
        Assert.False(editor.SendKeyDown(EditorKey.Q, modifiers | EditorKeyModifiers.Shift));
        Assert.True(editor.SendKeyDown(EditorKey.Q, modifiers));
        Assert.Equal(1, invoked);
    });

    [Fact]
    public Task KeyBindingChangesRejectBackgroundMutation() => WindowsTestHost.RunAsync(async () =>
    {
        var editor = new RichEditor();
        await Task.Run(() => Assert.Throws<InvalidOperationException>(() =>
            editor.KeyBindings.Add(new(EditorKey.S, EditorKeyModifiers.Control, new Command(static () => { })))));
        Assert.Empty(editor.KeyBindings);
    });

    [Fact]
    public Task ContextMenuCustomizationDoesNotMutateTheConfiguredMenu() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new RichEditor();
        var configured = new MenuFlyout { new MenuFlyoutItem { Text = "Configured" } };
        FlyoutBase.SetContextFlyout(editor, configured);
        editor.ContextMenuOpening += (_, args) => args.Items.Add(new MenuFlyoutItem { Text = "Dynamic" });
        var first = editor.CreateContextMenu();
        var second = editor.CreateContextMenu();
        Assert.False(first.IncludeDefaultItems);
        Assert.Equal(2, first.Items.Count);
        Assert.Equal(2, second.Items.Count);
        Assert.Single(configured);
        Assert.NotSame(first.Items, second.Items);
    });

    [Fact]
    public Task CodeContextMenuTargetsTheTextSurfaceAndCanKeepNativeItems() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new CodeEditor();
        var configured = new MenuFlyout { new MenuFlyoutItem { Text = "Code action" } };
        FlyoutBase.SetContextFlyout(editor, configured);
        editor.ContextMenuOpening += (sender, args) =>
        {
            Assert.Same(editor, sender);
            Assert.Single(args.Items);
            args.IncludeDefaultItems = true;
            args.Items.Add(new MenuFlyoutSeparator());
            var submenu = new MenuFlyoutSubItem { Text = "More" };
            submenu.Add(new MenuFlyoutItem { Text = "Nested" });
            args.Items.Add(submenu);
        };
        var menu = editor.TextView.CreateContextMenu();
        Assert.True(menu.IncludeDefaultItems);
        Assert.Equal(3, menu.Items.Count);
        Assert.IsType<MenuFlyoutSubItem>(menu.Items[2]);
    });

    [Fact]
    public Task MenuActionsRecheckCanExecuteAndPreserveClickedHandlers() => WindowsTestHost.RunAsync(() =>
    {
        var enabled = true;
        var invoked = 0;
        var clicked = 0;
        var parameter = new object();
        var item = new MenuFlyoutItem
        {
            Text = "Action",
            Command = new Command<object>(value => { Assert.Same(parameter, value); invoked++; }, _ => enabled),
            CommandParameter = parameter,
        };
        item.Clicked += (_, _) => clicked++;
        enabled = false;
        EditorMenu.Execute(item);
        Assert.Equal(0, invoked);
        Assert.Equal(0, clicked);
        enabled = true;
        EditorMenu.Execute(item);
        Assert.Equal(1, invoked);
        Assert.Equal(1, clicked);
    });

    [Fact]
    public Task KeyCommandExceptionsPropagate() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new RichEditor();
        var failure = new InvalidOperationException("Application command failure");
        editor.KeyBindings.Add(new(EditorKey.F1, EditorKeyModifiers.None, new Command(() => throw failure)));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => editor.SendKeyDown(EditorKey.F1, EditorKeyModifiers.None)));
    });
}
