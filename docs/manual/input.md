---
title: Input, commands, and clipboard
description: Connect editing commands, customize paste and context menus, and register hardware-key shortcuts.
---

# Input, commands, and clipboard

## Use built-in commands

Both controls expose `Commands.Undo`, `Redo`, `SelectAll`, `Copy`, `Cut`, and `Paste` as `ICommand` properties. Their `CanExecute` state reflects the editor's current selection, history, and editability.

```csharp
var copy = new Button { Text = "Copy", Command = editor.Commands.Copy };
var paste = new Button { Text = "Paste", Command = editor.Commands.Paste };
```

Use `CopyAsync()`, `CutAsync()`, and `PasteAsync()` when your code needs to await completion and observe errors directly. Command and native-menu execution reports operational clipboard failures through `Commands.Failed`; other exceptions still propagate through UI exception handling.

`IsReadOnly` disables editing through the control while leaving reading, selection, and copying available. `MaxLength` is a UTF-16 input limit, with `-1` meaning unlimited. It does not truncate an already loaded document or constrain direct model edits.

## Intercept paste

`Pasting` receives a portable fragment before insertion. Set `Cancel` to reject it or replace `Fragment` to transform it:

```csharp
editor.Pasting += (_, args) =>
{
    // Paste plain text with tabs replaced by spaces.
    string text = args.Fragment.Text.Replace("\t", "    ", StringComparison.Ordinal);
    args.Fragment = RichTextDocumentFragment.FromPlainText(text);
};
```

RichEditor retains rich fragments for copies within the running application while the clipboard still identifies that copy. Windows and Apple also publish RTF and plain text for other applications. Android imports plain text from other applications. Plain-text paste inherits the destination typing format and respects the editor's input length policy.

CodeEditor copies and pastes plain source. Its `Pasting` event uses the shared fragment type, but accepted content is converted to text.

## Bind hardware shortcuts

```csharp
var primary = OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst()
    ? EditorKeyModifiers.Meta
    : EditorKeyModifiers.Control;

editor.KeyBindings.Add(new EditorKeyBinding(
    EditorKey.A, primary, editor.Commands.SelectAll));
```

Bindings match exact key and modifier combinations. The last matching binding takes precedence, receives its command parameter, and runs only if `CanExecute` returns true. A disabled binding lets native handling continue. `Meta` represents Command on Apple platforms; `AltGraph` distinguishes AltGr from Control+Alt.

`KeyDown` runs before bindings and default shortcut handling. Set `Handled` to consume an event. Prefer key bindings for shortcut overrides on Apple, where the bindings are registered as native key commands.

`KeyDown` and bindings handle hardware keys. Observe `TextChanged` for input from software keyboards or IMEs. Native composition takes precedence over custom shortcuts.

## Customize context menus

```csharp
editor.ContextMenuOpening += (_, args) =>
{
    args.Items.Add(new MenuFlyoutSeparator());
    args.Items.Add(new MenuFlyoutItem
    {
        Text = "Select all",
        Command = editor.Commands.SelectAll,
    });
};
```

Each opening gets a new top-level item list. Set `IncludeDefaultItems = false` to replace native actions, or attach a `MenuFlyout` with `FlyoutBase.SetContextFlyout`.

Adapters support text, submenus, separators, enabled state, click handlers, commands, and command parameters. Menu icon sources and menu-item keyboard accelerators are not rendered. Customization is available on Windows, Android, and iOS/Mac Catalyst 16 or later; older Apple versions use their standard menus.

## Configure text entry

RichEditor exposes `Keyboard`, `IsSpellCheckEnabled`, and `IsTextPredictionEnabled` for native text input. `AcceptsTab` enables tab insertion. `ReturnCommand` and `Completed` respond to native editing completion, whose trigger varies by platform.
