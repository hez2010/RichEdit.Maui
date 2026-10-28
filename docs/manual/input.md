---
title: Input, commands, and clipboard
description: Native input, composition, checked cut, and transactional source rules.
---

# Input, commands, and clipboard

The native control owns typing, marked text, candidate interaction, primary selection, and touch handles. Hardware key bindings route commands; they do not replace the platform input connection.

On Android, keyboards receive up to 8,192 UTF-16 units of surrounding text. Autofill is unavailable for document values longer than 65,536 units. These limits do not truncate documents, saved files, or clipboard content.

Use the control's `Commands`, `KeyBindings`, and `ContextMenuOpening` APIs for application UI. Rich-editor methods include `CopyAsync`, `CutAsync`, `PasteAsync`, `PastePlainTextAsync`, `Undo`, `Redo`, and `SelectAll`. `IsReadOnly` is a session editing policy. Direct document edits are independent of one view's read-only state.

## Commands and failures

Both editors expose stable `ICommand` instances for `Undo`, `Redo`, `SelectAll`, `Copy`, `Cut`, and `Paste`. Bind toolbar buttons to `Commands.Undo`, for example, so command enablement follows editor state. Rich formatting commands such as `ToggleBold` are methods; wrap them in application commands when building an MVVM toolbar.

Subscribe to `Commands.Failed` to surface errors from native commands and asynchronous clipboard commands. Its event argument contains `Exception`. Direct editing API calls propagate exceptions to their caller; an `ICommand` failure notification does not replace exception handling around direct calls.

## Keyboard bindings

Use `EditorKeyBinding` for exact key/modifier combinations. Apple Command is `Meta`; Windows and Android shortcuts normally use `Control`. This example adds a rich formatting shortcut:

```csharp
using Microsoft.Maui.Controls;
using RichEdit.Maui;

var primary = OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst()
    ? EditorKeyModifiers.Meta : EditorKeyModifiers.Control;
var binding = new EditorKeyBinding(EditorKey.B, primary,
    new Command(() => { editor.ToggleBold(); }, () => !editor.IsReadOnly));
editor.KeyBindings.Add(binding);
```

The last matching entry takes precedence. Remove that same binding from `KeyBindings` when its feature ends. A command whose `CanExecute` returns false leaves native handling available; use an enabled no-op command if a gesture must be consumed. `KeyDown` runs before binding lookup and lets a handler set `Handled = true`. Native composition keeps ownership of text input, so hardware-key interception is not a replacement for IME or software-keyboard handling.

`CodeEditor` installs these editable defaults:

| Gesture | Behavior |
| --- | --- |
| Tab / Shift+Tab | Indent / outdent all selected source lines. |
| Enter | Insert `NewLine` and apply the configured indentation policy. |
| Ctrl+/ or Command+/ | Toggle `LineCommentPrefix` on selected lines. |
| Ctrl+B/I/U or Command+B/I/U | Consume rich-formatting shortcuts in the plain-source editor. |

Use the corresponding `Indent`, `Outdent`, `InsertNewLine`, and `ToggleLineComment` methods for menus and toolbars. Platform-native editing gestures and accessibility navigation remain available.

## Context and selection menus

`ContextMenuOpening` lets the application add MAUI `MenuFlyoutItem`, `MenuFlyoutSubItem`, and `MenuFlyoutSeparator` entries for one opening:

```csharp
editor.ContextMenuOpening += (_, args) =>
{
    args.Items.Add(new MenuFlyoutItem
    {
        Text = "Select all",
        Command = editor.Commands.SelectAll,
    });
    args.IncludeDefaultItems = true;
};
```

Set `IncludeDefaultItems = false` when supplying a complete application menu. Native menu customization requires iOS or Mac Catalyst 16 or later; this is newer than the editors' minimum Apple deployment target. Keep callbacks short and unsubscribe when the owning feature ends.

## Composition

Marked text lives in a provisional session snapshot. The shared committed document does not change until native composition completes. A document permits one composition lease; unrelated mutation is rejected with `CompositionActive`. Other panes may still inspect and display committed content.

With multiple carets, native composition previews at the primary caret, including its virtual column. Virtual padding becomes document text only when input commits. A simple committed replacement is replicated to the other carets in one transaction. Reconversion extending outside the primary range leaves secondary-selection mode. Cancellation restores the pre-composition selection set.

Request completion before saving, changing documents, or commands requiring committed input. An unresolved request must remain pending rather than saving text that omits the marked input.

## Source input rules

`CodeEditor.InputRules.Register` installs synchronous transaction-planning callbacks in explicit priority and registration order. A `TextInputContext` exposes the before/input snapshots, selections, and input changes. Its replacements address the input candidate; overlapping rule edits are rejected.

Use rules for paired delimiters, completion commit characters, indentation, and linked placeholders. Rules cannot await or mutate the live document. A completion plus commit character belongs to one plan and one undo unit. Async language analysis supplies cached decisions or later revision-checked edits.

This minimal rule pairs an opening parenthesis for a single caret and leaves the caret inside the pair:

```csharp
using TextEdit.Core;

using var pairing = code.InputRules.Register(context =>
{
    if (context.IsCompositionCommit || context.Selections.Count != 1 ||
        context.InputChanges.Length != 1 || context.InputChanges[0].Text != "(")
        return;

    var caret = context.Selections[0];
    if (!caret.IsEmpty || caret.AnchorVirtualSpaces != 0 || caret.ActiveVirtualSpaces != 0)
        return;

    context.Replace(new TextSpan(caret.Active, 0), ")");
    context.SetSelections(context.Selections);
}, priority: 100);
```

Keep the registration alive while pairing is enabled. This example deliberately handles one simple input; a language-aware rule should also check strings/comments and existing delimiters. Every rule sees the same `Input` snapshot, not the preceding rule's proposals. Priorities run in ascending order, then registration order. Only one rule may set resulting selections; conflicting replacements, duplicate insertion positions, or callback failures reject the whole plan. Observe `code.InputRejected` to diagnose those failures.

## Clipboard

Rich clipboard exchange is self-contained RTF plus ordinary plain-text fallback. Android uses an RTF transport that cooperating applications can read. Unsupported external rich payloads use available plain text; no HTML codec is provided.

Cut freezes the selection and snapshot, publishes the clipboard, then checks document revision and selection generation before deleting. An asynchronous stale cut cannot delete newly selected or edited content.

In-process multiple-selection metadata can preserve fragment boundaries while clipboard identity matches. Matching fragment/caret counts distribute fragments; otherwise the combined payload is inserted at each selection. External rich interchange remains one RTF payload.

### Intercept paste

`RichEditor.Pasting` supplies a `RichPastingEventArgs` with `Fragment`, RTF `Diagnostics`, `UsePlainText`, and `Cancel`. For a plain-text-only paste policy:

```csharp
editor.Pasting += (_, args) => args.UsePlainText = true;
```

Use `PastePlainTextAsync()` for an explicit “Paste without formatting” action. To preserve rich paste, inspect diagnostics and optionally replace `Fragment` with application-approved content. Set `Cancel = true` to decline the paste.

`CodeEditor.Pasting` supplies `TextPastingEventArgs`. It exposes the exact source `Text` to replace or a `Cancel` flag; it has no rich fragment:

```csharp
code.Pasting += (_, args) =>
{
    if (args.Text.Contains('\0')) args.Cancel = true;
};
```

Paste callbacks are synchronous. If a policy needs asynchronous UI or analysis, cancel this paste, capture the destination revision and selection generation, and later apply a checked edit against that capture. Do not use an `async void` handler to modify paste arguments after the commit has continued.

## Drag and drop

On Windows, dragging selected text copies it to a drop destination. Rich selections carry RTF and ordinary text; source selections carry exact text. Dropping into an editor uses its paste event and editing limits. A drop is canceled if its destination document, selection, or attached view changes while the data is being read. Dragging does not replace the system clipboard.
