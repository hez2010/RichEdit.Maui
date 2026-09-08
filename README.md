# RichEdit.Maui

`RichEdit.Maui` is a native-handler rich-text editor for .NET MAUI. It combines a bindable editor control, a stable live document, immutable versioned snapshots, and atomic range edits.

For source-code editing, [CodeEdit.Maui](CodeEdit.Maui/README.md) provides LSP semantic coloring, line numbers, and native keyboard editing. [CodeEdit.Lsp](CodeEdit.Lsp/README.md) supplies the client API with application-owned connections. Register it with `builder.UseCodeEditor()`. The sample app's **Code editor** tab demonstrates application toolbar actions and find/replace.

The editor uses each platform's native text stack:

- Android 26+: `AppCompatEditText` and editable spans
- iOS and Mac Catalyst: `UITextView`, `NSTextStorage`, and attributed strings
- Windows: WinUI 3 `RichEditBox` and the Text Object Model

Formatting that cannot be rendered exactly on a platform remains in the document and its RTF representation. See the [portable RTF support matrix](RTF_SUPPORT.md) for the exact native, adapted, degraded, preserved, and unsupported behavior.

## Register the handler

```csharp
using RichEdit.Maui;

builder
    .UseMauiApp<App>()
    .UseRichEdit();
```

## Own editor content

Bind a stable `RichTextDocument` to the control. Native input and application
edits update that document:

```xml
<ContentPage
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    xmlns:rich="clr-namespace:RichEdit.Maui;assembly=RichEdit.Maui">
    <rich:RichEditor x:Name="Editor"
                     Document="{Binding Document}"
                     SelectedRange="{Binding Selection, Mode=TwoWay}"
                     IsReadOnly="{Binding IsReadOnly}"
                     IsSpellCheckEnabled="True"
                     Placeholder="Start writing…"
                     FontSize="17"
                     MinimumHeightRequest="240" />
</ContentPage>
```

Create an initial document from RTF and assign it to the editor. Read its `Text`
and `RtfText` projections for inspection and persistence:

```csharp
Editor.Document = RichTextDocument.FromRtf(rtfText);

var plainText = Editor.Document.Text;
var canonicalRtf = Editor.Document.RtfText;
```

After construction, change content only through `Document.Edit(...)` or selection
operations. RTF serialization is lazy and cached by document version.

Control appearance properties such as `FontFamily`, `FontSize`, and `TextColor` are rendering fallbacks. They are not authored document formatting and are never serialized into `RtfText`.

## Selection and formatting

`Selection` is a stable live facade. Formatting a nonempty selection updates only that range; formatting a caret changes the typing format:

```csharp
Editor.Selection.ToggleBold();
Editor.Selection.ToggleItalic();
Editor.Selection.ToggleUnderline(RichTextUnderlineStyle.Single);
Editor.Selection.ToggleStrikethrough(RichTextStrikethroughStyle.Single);
Editor.Selection.ToggleScript(RichTextScript.Superscript);

Editor.Selection.CharacterFormat.FontFamily = "Georgia";
Editor.Selection.CharacterFormat.FontSize = 20;
Editor.Selection.CharacterFormat.ForegroundColor = Color.FromArgb("#E06C75");
Editor.Selection.CharacterFormat.BackgroundColor = Color.FromArgb("#FFF3A3");
Editor.Selection.ParagraphFormat.Alignment = RichTextAlignment.Center;
```

Each selection-format property has a corresponding mixed-state property, such as `IsFontFamilyMixed`. Authored values and effective rendering values are reported separately:

```csharp
var authoredFont = Editor.Selection.CharacterFormat.FontFamily;
var visibleFont = Editor.Selection.CharacterFormat.EffectiveFontFamily;
var isInherited = Editor.Selection.CharacterFormat.IsFontFamilyInherited;

// Remove explicit run formatting and resume document/view/native inheritance.
Editor.Selection.CharacterFormat.FontFamily = null;
```

The same selection facade provides range replacement, links, fields, images, and list operations.

## Caller-defined lists

The library does not choose a bullet glyph, number style, prefix, suffix, start value, or indentation. Define the complete list in application code:

```csharp
var checklist = new RichTextListDefinition(
[
    new RichTextListLevelDefinition
    {
        Marker = new RichTextListMarker.Bullet("✓"),
        Prefix = string.Empty,
        Suffix = string.Empty,
        LeadingIndent = 24,
        FirstLineIndent = -18,
        MarkerTab = 24,
    },
]);

var outline = new RichTextListDefinition(
[
    new RichTextListLevelDefinition
    {
        Marker = new RichTextListMarker.Number(RichTextListNumberStyle.UpperRoman, 4),
        Prefix = "(",
        Suffix = ")",
        LeadingIndent = 36,
        FirstLineIndent = -24,
        MarkerTab = 36,
    },
]);

Editor.Selection.ToggleList(checklist);
Editor.Selection.ToggleList(outline);
```

Document transactions can create one list identity and apply it to several ranges without duplicating its definition:

```csharp
Editor.Document.Edit(edit =>
{
    var listId = edit.CreateList(outline);
    edit.ApplyList(new RichTextRange(0, 10), listId);
    edit.ApplyList(new RichTextRange(30, 15), listId);
});
```

Lists support up to nine independently defined levels, caller-selected bullet text or pictures, numbering style and start value, prefixes and suffixes, nesting, continuation, and explicit restarts.

## Atomic incremental document edits

`RichTextDocument` keeps a stable identity and emits one `Changed` event for each committed transaction:

```csharp
Editor.Document.Edit(
    edit =>
    {
        edit.ReplaceText(new RichTextRange(20, 5), "replacement");
        edit.UpdateCharacterFormat(
            new RichTextRange(20, 11),
            format => format with { FontWeight = 700 });
        edit.SetLink(new RichTextRange(20, 11), "https://example.com");
    },
    new RichTextEditOptions(
        undoBehavior: RichTextUndoBehavior.CreateUnit,
        undoDescription: "Replace title"));
```

The transaction produces one version change, native batch, undo unit, and change notification. The edit builder supports:

- insert, delete, and replace text or rich fragments
- set, update, or clear character and paragraph formats
- document default formats
- list definitions, application, nesting, restart, and picture markers
- links, fields, images, and metadata

`CurrentSnapshot` exposes an immutable view of the current version for safe enumeration. Range-bearing values use `RichTextRange`, whose offsets and lengths are UTF-16 code units.

## Grouping edits and authored formatting

`Document.Edit` is an atomic transaction. To combine several editing operations into one undo unit, open an undo group:

```csharp
using (Editor.Document.BeginUndoGroup("Insert heading and body"))
{
    Editor.Selection.ReplaceText("Heading\n");
    Editor.Selection.ReplaceText("Body text\n");
}
```

Groups can nest and must close in reverse order. Edits commit and notify independently; an undo group does not roll back committed edits on an exception. Undo/redo are unavailable until the outermost group closes, and history cannot be cleared while a group is open.

Application-generated document styles can preserve undo and redo history:

```csharp
Editor.Document.Edit(
    edit => edit.UpdateCharacterFormat(match, format => format with { BackgroundColor = Colors.Yellow }),
    new RichTextEditOptions(RichTextUndoBehavior.PreserveHistory));
```

`PreserveHistory` accepts character, paragraph, and default formatting. Text and semantic edits are rejected atomically. These formats are document content and are included in RTF. Undo/redo restore formatting from recorded snapshots, so applications that derive styles from text should reapply them after history transitions. Use `Decorations` for view-only syntax colors and search highlights. `ClearHistory` commits the edit and clears undo/redo.

## MVVM commands

`Commands` exposes `Undo`, `Redo`, `SelectAll`, `Copy`, `Cut`, and `Paste` with editor-aware `CanExecute` state. Native menus and clipboard shortcuts use these same commands.

```xml
<Button Text="Undo"
        Command="{Binding Source={x:Reference Editor}, Path=Commands.Undo}" />
```

Formatting toolbar commands belong to the application. The sample's [RichEditorFormattingCommands.cs](RichEdit.Maui.TestApp/RichEditorFormattingCommands.cs) wraps `Selection` operations and owns the list, link, and field command request types. Applications can also use those operations directly or bind to `Selection.CharacterFormat` and `Selection.ParagraphFormat`.

```csharp
var bold = new Command(Editor.Selection.ToggleBold, () => !Editor.IsReadOnly);
```

Refresh an application's command state when its editor state changes, as the sample does.

## Key bindings

`KeyBindings` maps an exact `EditorKey` and `EditorKeyModifiers` combination to an `ICommand`. Add bindings on the editor's UI thread. The last matching binding wins; its command receives `CommandParameter` and runs only when `CanExecute` is true. A disabled binding allows native handling to continue without invoking an earlier binding for that gesture.

```csharp
var primary = OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst()
    ? EditorKeyModifiers.Meta
    : EditorKeyModifiers.Control;
Editor.KeyBindings.Add(new(EditorKey.S, primary, saveCommand));
```

`Meta` represents Command on Apple platforms and the Windows/Meta key elsewhere. `AltGraph` distinguishes AltGr input from an ordinary Control+Alt shortcut. Available keys depend on the hardware and OS; OS-reserved shortcuts remain outside the editor's control.

`KeyDown` receives hardware-key events before the editor dispatches its bindings and default shortcuts. Set `Handled` to consume an event:

```csharp
Editor.KeyDown += (_, args) =>
{
    if (args.Key == EditorKey.Escape && args.Modifiers == EditorKeyModifiers.None)
    {
        CloseApplicationPopup();
        args.Handled = true;
    }
};
```

Use `KeyBindings` for shortcut overrides: Apple registers these as native key commands with priority over text and focus handling. `KeyDown` also handles hardware events delivered to the text view. Neither API translates soft-keyboard input into synthetic key presses. Native IME composition takes precedence over custom shortcuts. Command exceptions propagate to the application.

## Context menus

`ContextMenuOpening` customizes the native context and text-selection menus. Its `Items` collection accepts MAUI `MenuFlyoutItem`, `MenuFlyoutSubItem`, and `MenuFlyoutSeparator` objects. Set `IncludeDefaultItems` to false to replace the native editing, formatting, and proofing items:

```csharp
Editor.ContextMenuOpening += (_, args) =>
{
    args.Items.Add(new MenuFlyoutSeparator());
    args.Items.Add(new MenuFlyoutItem { Text = "Insert date", Command = insertDateCommand });
};
```

For a reusable replacement menu, assign the standard MAUI attached property:

```csharp
FlyoutBase.SetContextFlyout(Editor, new MenuFlyout
{
    new MenuFlyoutItem { Text = "Copy", Command = Editor.Commands.Copy },
    new MenuFlyoutItem { Text = "Application action", Command = applicationCommand },
});
```

The attached menu supplies the initial custom items and disables native defaults. `ContextMenuOpening` then receives a separate top-level item list for that opening, so adding or removing entries does not modify the configured menu. Menu text, submenus, native separators, `IsEnabled`, `Clicked`, `Command`, and `CommandParameter` are supported. Commands recheck `CanExecute` when invoked. Use `KeyBindings` for shortcuts; menu-item keyboard accelerators and icon sources are not rendered by the native editing-menu adapters.

Menu customization supports Windows and Android, and iOS/Mac Catalyst 16 or later. Earlier Apple versions use their standard native editing menu. Android uses the selection and insertion action modes; separators follow the platform's native menu presentation.

## Events and editor operations

The control exposes `ContentChanged`, `TextChanged`, `SelectionChanged`, `SelectionFormatChanged`, `EffectiveAppearanceChanged`, `LinkInvoked`, `InlineObjectInvoked`, `Pasting`, and `Completed` events. `ContentChanged` includes the atomic `RichTextChangeSet`, including its origin, old and new versions, and bounded changes.

Undo, redo, selection, and portable clipboard operations are available directly:

```csharp
Editor.Undo();
Editor.Redo();
Editor.SelectAll();
await Editor.CopyAsync();
await Editor.CutAsync();
await Editor.PasteAsync();
```

Copy and cut from native menus and keyboard shortcuts use the same portable fragments as these methods. Copies within the running application retain formatting that RTF cannot represent; Windows and Apple also publish RTF and plain text for other applications. Paste raises `Pasting`, respects `MaxLength`, and preserves destination typing formatting for plain text. Android's **Paste as plain text** discards the copied formatting.

All platforms use document snapshots for undo and redo, including field instructions, image payloads, and metadata. Undo entries also retain the before/after directional selection. `SelectionState` exposes its anchor and active UTF-16 offsets, while `SelectedRange` is its normalized range. Apple's native undo manager forwards system undo/redo commands to that history, so native typing and programmatic edits remain in one consistent sequence. Undo and redo are disabled while the editor is read-only.

## Tests

Run the Windows tests with the matching Windows App SDK runtime installed:

```powershell
dotnet build RichEdit.Maui.Tests/RichEdit.Maui.Tests.csproj -p:Platform=x64 -p:WindowsPackageType=None
& ./RichEdit.Maui.Tests/bin/x64/Debug/net10.0-windows10.0.19041.0/RichEdit.Maui.Tests.exe -parallelMode none -nocolor
```

The Android test app also includes a debug-only native test mode. Build and install its Debug APK, then launch the main activity with the boolean intent extra `run-editor-tests=true`. Results are written to `cache/editor-tests.txt` in the app's private storage and to the `RichEditTests` logcat tag. Keep fast-deployment overrides consistent with the installed APK when switching SDKs.

For the Apple debug test mode, launch the test app with `RICHEDIT_RUN_TESTS=1` in its environment. With `simctl`, use `SIMCTL_CHILD_RICHEDIT_RUN_TESTS=1 xcrun simctl launch <device> <bundle-id>`. Results are written to `Library/Caches/apple-editor-tests.txt` inside the app's data container.

## RTF behavior

The reader follows RTF group scoping, Unicode fallback, `\upr`/`\ud` Unicode-alternate, code-page, paragraph-default, and ignorable-destination rules. It accepts ANSI (`\ansicpg`), Mac, PC 437, PC 850, and font-specific `\fcharset`/`\cpg` text; `\ansi` without `\ansicpg` decodes as Windows-1252. Table cells are flattened to tab/newline text. `\line` is represented as U+2028 and `\par` as `\n`, so soft and paragraph breaks remain distinct.

RTF uses opaque 8-bit RGB colors and half-point font sizes. Serialization rounds model values to those wire-format units. Nonzero alpha is degraded to opaque RGB; a fully transparent formatting color means unset/reset instead of an alpha-zero native color.

## Presentation layers

Use `Editor.Decorations` for syntax colors, diagnostics, or search highlights. These overrides never enter the document, its RTF, content events, saved state, or undo history. Layers compose in creation order; properties specified by a later layer override earlier layers. Ranges within each layer must be nonempty, ordered, and non-overlapping. Native painting is deferred during IME composition.

```csharp
using var matches = Editor.Decorations.CreateLayer();
matches.Set([new(new RichTextRange(0, 5),
    new RichTextDecorationStyle { BackgroundColor = Colors.Yellow })]);
```

Replacing the document clears the layers. Text edits map their ranges, and disposing a layer restores the remaining appearance. `Decorations.Changed` and `Version` describe presentation independently of document changes.

## Selection, history, and saves

```csharp
Editor.SelectionState = new RichTextSelectionState(anchor: 20, active: 5);
Editor.ScrollIntoView(Editor.SelectedRange);
Editor.Document.Undo(); // Restores content and the recorded selection.

var snapshot = Editor.Document.CurrentSnapshot;
var savePoint = Editor.Document.CreateSavePoint();
await SaveRtfAsync(snapshot.RtfText); // Application-owned persistence.
Editor.Document.MarkSaved(savePoint); // Later edits remain modified.
```

The document exposes `CanUndo`, `CanRedo`, `UndoDescription`, `RedoDescription`, `Undo`, `Redo`, `ClearUndoHistory`, `BeginUndoGroup`, and `IsModified`, including while detached. `MarkSaved()` marks the current state; a captured save point supports asynchronous saves. Undo/redo recognize a saved state independently of the monotonically increasing document version. Editor commands additionally respect `IsReadOnly`.

## Threading and notifications

Create controls on their owning UI thread. Attached document mutations, history operations, and save-state changes require that thread and throw before committing when called elsewhere. Detached document mutations must be serialized by the caller. Immutable snapshots may be inspected on background threads.

Before public notifications, the committed document, native view, selection, and history availability are synchronized. Notification order is document `Changed`, editor `ContentChanged`/`TextChanged`, document projection/history property changes, and finally selection and selection-format notifications. Recursive document mutation from those notifications throws. An exception from an application notification propagates after the transaction has committed.

Awaited clipboard methods propagate their exceptions. Clipboard commands and native menu actions propagate asynchronous exceptions to the application's UI exception handling without logging and suppressing them.
