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
        Assert.Equal(new TextSpan(4, 0), editor.SelectedRange);
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
        var editor = new NativeEditorView();
        var invoked = 0;
        var enabled = false;
        var parameter = new object();
        editor.KeyBindings.Add(new(EditorKey.S, EditorKeyModifiers.Control, new Command(() => invoked += 100)));
        editor.KeyBindings.Add(new(EditorKey.S, EditorKeyModifiers.Control,
            new Command<object>(value =>
            {
                Assert.Same(parameter, value);
                invoked++;
            }, value => enabled && ReferenceEquals(value, parameter)), parameter));
        Assert.False(editor.SendKeyDown(EditorKey.S, EditorKeyModifiers.Control));
        Assert.Equal(0, invoked);
        enabled = true;
        Assert.True(editor.SendKeyDown(EditorKey.S, EditorKeyModifiers.Control));
        Assert.Equal(1, invoked);
    });

    [Fact]
    public Task ModifiersMatchExactlyAndAltGrDoesNotInvokeControlAltBinding() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new NativeEditorView();
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
        var editor = new NativeEditorView();
        await Task.Run(() => Assert.Throws<InvalidOperationException>(() =>
            editor.KeyBindings.Add(new(EditorKey.S, EditorKeyModifiers.Control, new Command(static () => { })))));
        Assert.Empty(editor.KeyBindings);
    });

    [Fact]
    public Task ContextMenuCustomizationDoesNotMutateTheConfiguredMenu() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new NativeEditorView();
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
            Command = new Command<object>(value =>
            {
                Assert.Same(parameter, value);
                invoked++;
            }, _ => enabled),
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
    public Task KeyCommandFailuresAreReportedAndConsumeTheGesture() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new NativeEditorView();
        var failure = new InvalidOperationException("Application command failure");
        var command = new Command(() => throw failure);
        Exception? observed = null;
        editor.Commands.Failed += (_, args) => observed = args.Exception;
        editor.KeyBindings.Add(new(EditorKey.F1, EditorKeyModifiers.None, command));
        Assert.True(editor.SendKeyDown(EditorKey.F1, EditorKeyModifiers.None));
        Assert.Same(failure, observed);
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => command.Execute(null)));
    });

    [Theory]
    [InlineData("Undo")]
    [InlineData("Redo")]
    [InlineData("SelectAll")]
    public Task BuiltInSynchronousCommandsReportFailures(string name) => WindowsTestHost.RunAsync(() =>
    {
        var editor = new NativeEditorView { Document = RichTextDocument.FromPlainText("before") };
        editor.SelectAll();
        editor.Selection.ReplaceText("after");
        if (name == "Redo") editor.Undo();
        editor.SelectedRange = new(0, 0);
        var failure = new InvalidOperationException("Application notification failure");
        Exception? observed = null;
        editor.Commands.Failed += (_, args) => observed = args.Exception;
        if (name == "SelectAll") editor.SelectionChanged += (_, _) => throw failure;
        else editor.ContentChanged += (_, _) => throw failure;
        var command = name switch { "Undo" => editor.Commands.Undo, "Redo" => editor.Commands.Redo, _ => editor.Commands.SelectAll };
        Assert.True(command.CanExecute(null));
        command.Execute(null);
        Assert.Same(failure, observed);
    });

    [Theory]
    [InlineData("CanExecute")]
    [InlineData("Execute")]
    [InlineData("Clicked")]
    public Task NativeMenuFailuresAreReportedAndLaterActionsStillWork(string stage) => WindowsTestHost.RunAsync(() =>
    {
        var editor = new NativeEditorView();
        var failure = new InvalidOperationException("Menu failure");
        Exception? observed = null;
        editor.Commands.Failed += (_, args) => observed = args.Exception;
        var ready = false;
        var item = new MenuFlyoutItem
        {
            Command = new Command(() => { if (stage == "Execute") throw failure; },
                () => ready && stage == "CanExecute" ? throw failure : true),
        };
        ready = true;
        item.Clicked += (_, _) => { if (stage == "Clicked") throw failure; };
        if (stage == "CanExecute") Assert.False(editor.CanExecuteMenuItem(item));
        else editor.ExecuteMenuItem(item);
        Assert.Same(failure, observed);
        var recovered = false;
        editor.ExecuteMenuItem(new MenuFlyoutItem { Command = new Command(() => recovered = true) });
        Assert.True(recovered);
    });

    [Fact]
    public Task NativeInputCallbacksReportFailures() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new NativeEditorView();
        var failure = new InvalidOperationException("Native callback failure");
        var failures = new List<Exception>();
        editor.Commands.Failed += (_, args) => failures.Add(args.Exception);
        editor.KeyBindings.Add(new(EditorKey.F1, EditorKeyModifiers.None, new Command(() => { }, () => throw failure)));
        Assert.True(editor.SendKeyDown(EditorKey.F1, EditorKeyModifiers.None));
        editor.KeyDown += (_, _) => throw failure;
        Assert.True(editor.SendKeyDown(EditorKey.F2, EditorKeyModifiers.None));
        editor.ContextMenuOpening += (_, _) => throw failure;
        var menu = editor.CreateContextMenu();
        Assert.Empty(menu.Items);
        Assert.False(menu.IncludeDefaultItems);
        editor.ReturnCommand = new Command(() => throw failure);
        editor.RaiseCompleted();
        Assert.Equal(4, failures.Count);
        Assert.All(failures, actual => Assert.Same(failure, actual));
    });

    [Fact]
    public Task CellCommandFailuresReachThePublicEditorOnce() => WindowsTestHost.RunAsync(async () =>
    {
        var document = new RichEdit.Core.RichDocument();
        RichEdit.Core.RichNodeId cell = default;
        document.Edit(edit =>
        {
            var table = edit.InsertTable(edit.Body, 1, 1, (RichEdit.Core.RichLength[])[new(1440)]);
            cell = edit.Snapshot.GetTableGrid(table)[0, 0];
        });
        var editor = new RichEditor { Document = document };
        var surface = new NativeEditorView();
        surface.BindStructuredDocument(document, cell, editor.NativeView);
        var failure = new InvalidOperationException("Cell command failure");
        var failures = new List<Exception>();
        editor.Commands.Failed += (sender, args) =>
        {
            Assert.Same(editor.Commands, sender);
            failures.Add(args.Exception);
        };
        surface.ExecuteMenuItem(new MenuFlyoutItem { Command = new Command(() => throw failure) });
        await surface.Commands.ExecuteClipboardAsync(() => Task.FromException(failure));
        Assert.Equal(2, failures.Count);
        Assert.All(failures, actual => Assert.Same(failure, actual));

        editor.Document = new RichEdit.Core.RichDocument();
        surface.Commands.TryExecute(() => throw failure);
        Assert.Equal(2, failures.Count); // A stale cell must not report on the replacement document.
    });
}
