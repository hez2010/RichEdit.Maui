# RichEdit.Maui

[![RichEdit.Maui NuGet version](https://img.shields.io/nuget/vpre/RichEdit.Maui?label=RichEdit.Maui)](https://www.nuget.org/packages/RichEdit.Maui)
[![CodeEdit.Maui NuGet version](https://img.shields.io/nuget/vpre/CodeEdit.Maui?label=CodeEdit.Maui)](https://www.nuget.org/packages/CodeEdit.Maui)

A native rich-text editor for .NET MAUI, with a bindable document model, RTF support, and undo/redo.

- Character and paragraph formatting, lists, links, fields, and inline images.
- Atomic document edits, immutable snapshots, selection tracking, and save points.
- Text decorations, range folding, and anchored MAUI views.
- Keyboard bindings, context menus, clipboard commands, and native text input.

[CodeEdit.Maui](#code-editor) provides a code editor built on the same APIs, with line numbers, configurable indentation, and word wrapping.

## Requirements

The libraries target .NET 10 and use each platform's native text controls.

| Platform | Minimum target | Native text control |
| --- | --- | --- |
| Android | API 26 | `AppCompatEditText` with editable spans |
| iOS | 15.0 | `UITextView` with attributed text |
| Mac Catalyst | 15.0 | `UITextView` with attributed text |
| Windows | 10.0.17763.0 | WinUI 3 `RichEditBox` |

Rendering support varies by platform. See the [RTF support matrix](RTF_SUPPORT.md) for formatting and serialization details.

## Installation

Add the NuGet package to a .NET 10 MAUI project:

```sh
dotnet add package RichEdit.Maui --prerelease
```

Register the native handler on the `MauiAppBuilder`:

```csharp
using RichEdit.Maui;

builder.UseRichEdit();
```

## Usage

Create an editor with a document:

```csharp
using RichEdit.Maui;

var editor = new RichEditor
{
    Document = RichTextDocument.FromPlainText("Hello, world!\n"),
    Placeholder = "Start writing…",
    FontSize = 17,
    IsSpellCheckEnabled = true,
    MinimumHeightRequest = 240,
};
```

The control also supports XAML and data binding:

```xml
<ContentPage
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    xmlns:rich="clr-namespace:RichEdit.Maui;assembly=RichEdit.Maui">
    <rich:RichEditor x:Name="Editor"
                     Document="{Binding Document}"
                     SelectedRange="{Binding Selection, Mode=TwoWay}"
                     IsReadOnly="{Binding IsReadOnly}"
                     Placeholder="Start writing…"
                     MinimumHeightRequest="240" />
</ContentPage>
```

`Document` is a stable, live object. Native input and document edits update it in place. A document can be attached to one editor at a time.

### Loading and reading content

Use `FromPlainText` or `FromRtf` to load content. Read `Text` or `RtfText` to export it:

```csharp
editor.Document = RichTextDocument.FromRtf(@"{\rtf1\ansi Hello, \b world\b0 !}");

string plainText = editor.Document.Text;
string rtf = editor.Document.RtfText;
```

Document text uses LF (`\n`) line endings. All positions and `RichTextRange` values use UTF-16 code units. `CurrentSnapshot` provides immutable text, formatting, and metadata for inspection. RTF serialization is lazy and cached.

### Selection and formatting

Set `SelectedRange` to select text. `Selection` exposes the selected content and its formatting:

```csharp
editor.SelectedRange = new RichTextRange(0, 5);
editor.Selection.ToggleBold();
editor.Selection.ToggleItalic();
editor.Selection.ToggleUnderline(RichTextUnderlineStyle.Single);

editor.Selection.CharacterFormat.FontFamily = "Georgia";
editor.Selection.CharacterFormat.FontSize = 20;
editor.Selection.CharacterFormat.ForegroundColor = Colors.DarkBlue;
editor.Selection.ParagraphFormat.Alignment = RichTextAlignment.Center;
```

Character formatting at an empty selection changes the typing format. For directional selections, use `SelectionState`, which retains both the anchor and active position. `ScrollIntoView(range)` scrolls to a source range.

Selection format properties expose mixed, inherited, and effective values, such as `IsFontFamilyMixed`, `IsFontFamilyInherited`, and `EffectiveFontFamily`. Assign `null` to a nullable format property to restore inheritance. Control properties such as `FontFamily`, `FontSize`, and `TextColor` provide display defaults and are not saved as document formatting.

`Selection` also provides `SetLink`, `RemoveLinks`, `InsertField`, `InsertImage`, and `ReplaceFragment` for richer content.

### Editing a document

Use `Document.Edit` to group changes into an atomic transaction:

```csharp
editor.Document.Edit(edit =>
{
    edit.ReplaceText(new RichTextRange(0, 5), "Welcome");
    edit.UpdateCharacterFormat(new RichTextRange(0, 7),
        format => format with { FontWeight = 700 });
    edit.SetLink(new RichTextRange(0, 7), "https://example.com");
}, new RichTextEditOptions(undoDescription: "Update greeting"));
```

Operations run in order, so each range addresses the result of preceding operations in the callback. A changed transaction produces one document version, one `Changed` event, and one undo unit by default. A failure inside the callback leaves the document unchanged.

The edit builder supports text and fragment replacement, character and paragraph formatting, document defaults, lists, links, fields, images, and metadata. `SetCharacterFormats` accepts an ordered, non-overlapping batch of formatted runs.

### Lists

Define the marker and layout for each list level, then apply the definition to the selection:

```csharp
var numberedList = new RichTextListDefinition(
[
    new RichTextListLevelDefinition
    {
        Marker = new RichTextListMarker.Number(RichTextListNumberStyle.Arabic, 1),
        Prefix = string.Empty,
        Suffix = ".",
        LeadingIndent = 24,
        FirstLineIndent = -18,
        MarkerTab = 24,
    },
]);

editor.Selection.ToggleList(numberedList);
```

Lists support up to nine levels, text and picture bullets, numbering styles, prefixes, suffixes, continuation, and restarts. Use `ChangeListLevel`, `RestartList`, or `ClearList` to update selected paragraphs. Within a document transaction, `CreateList` returns an identity that `ApplyList` can reuse across ranges.

### Undo, redo, and saving

Call `editor.Undo()` and `editor.Redo()`, or bind to `Commands.Undo` and `Commands.Redo`. `CanUndo`, `CanRedo`, `UndoDescription`, and `RedoDescription` are also available on the document. History retains rich content and directional selection.

Use an undo group to combine several editing operations:

```csharp
using (editor.Document.BeginUndoGroup("Insert section"))
{
    editor.Selection.ReplaceText("Heading\n");
    editor.Selection.ReplaceText("Body text\n");
}
```

Edits still commit independently inside a group. Groups can nest, must close in reverse order, and disable undo/redo until the outermost group closes. They do not roll back already committed edits if a later operation fails.

`RichTextEditOptions` also supports `MergeWithPrevious`, `ClearHistory`, and `PreserveHistory`. `PreserveHistory` accepts character, paragraph, and default formatting only; those changes remain part of the document and its RTF. Undo/redo can restore earlier formatting.

For asynchronous saves, capture the document, snapshot, and save point together on the UI thread:

```csharp
var document = editor.Document;
var snapshot = document.CurrentSnapshot;
var savePoint = document.CreateSavePoint();

await File.WriteAllTextAsync("document.rtf", snapshot.RtfText);
document.MarkSaved(savePoint);
```

`IsModified` compares the current state with the saved state, including after undo/redo. Marking a captured save point leaves later edits modified. `MarkSaved()` marks the current state directly.

### Decorations

Use decorations for temporary colors, diagnostics, or search highlights:

```csharp
var highlights = editor.Decorations.CreateLayer();
highlights.TrySet(editor.Document.Revision,
[
    new RichTextDecoration(new RichTextRange(0, 5),
        new RichTextDecorationStyle { BackgroundColor = Colors.Yellow }),
]);
```

Keep the layer alive while it is needed; call `Clear()` to remove its contents or `Dispose()` to remove it. Layers compose in creation order, with later layers overriding the properties they specify. Ranges within a layer must be nonempty, ordered, and non-overlapping.

Decorations leave document content, RTF, saved state, and undo history unchanged. Ranges track text edits, and document replacement clears their contents. `TrySet` rejects stale or foreign revisions. Native painting is deferred during IME composition; a deferred publication is discarded if its revision becomes stale before painting.

### Range folding

Collapse and expand explicit source ranges:

```csharp
var range = new RichTextRange(0, 5);
editor.Folding.Collapse(range);
editor.Folding.Expand(range);
editor.Folding.ExpandAll();
```

`SetCollapsedRanges` replaces the set, `CollapsedRanges` exposes its entries, and `Changed` reports updates. Nested and overlapping folds retain independent state. Use an entry from `CollapsedRanges` with `Expand`; `GetCollapsedRange(offset)` returns the hidden union at an offset.

Folding affects display only. Text, source selection, clipboard content, and history retain the complete document. Ranges track edits; boundary insertions stay outside a fold. Replacing an entire folded range removes it, and undo does not recreate discarded folds. Document replacement clears the set.

Expand ranges explicitly when navigating to hidden text. Folding changes require the UI thread and throw during native IME composition.

### Anchored views

Add MAUI views at source positions with `Adornments`:

```csharp
editor.Adornments.MarginWidth = 32;
var button = new Button
{
    Text = "+",
    WidthRequest = 24,
    HeightRequest = 20,
    Padding = 0,
};
var adornment = editor.Adornments.Add(0, button,
    new RichTextAdornmentOptions { Placement = RichTextAdornmentPlacement.LeftMargin });
```

`Overlay` draws over text, and `LeftMargin` uses the reserved margin. Both accept an `Offset`. `Inline` reserves the view's measured space in text flow; `AboveLine` and `BelowLine` reserve a row around the logical line. Inline `Baseline` is measured from the view's top; `null` aligns its bottom. Inline content wider than the viewport occupies its own row.

Views retain their normal input and accessibility behavior. Reserved space adds no source characters. Anchors follow edits with `AfterInsertion` affinity by default; set `Affinity` to `BeforeInsertion` to stay before inserted text. Deleting or replacing text containing an anchor removes its view. Folded anchors are hidden, and views are clipped to the viewport.

Add unparented views and update the collection on the UI thread. Dispose an adornment to remove it, or call `Adornments.Clear()`. Document replacement clears the collection; undo does not recreate removed views. `TryAdd(revision, position, view, out adornment, options)` checks a captured revision before adding a view.

### Revisions and tracked ranges

A `Revision` identifies both a document and its version. Capture `CurrentSnapshot` before background work, then use the captured revision when publishing results on the UI thread.

`TryApplyEdits(revision, edits, selectionAfter, options)` applies a batch of `RichTextEdit` replacements in the original snapshot's coordinates. It validates ranges and overlaps before mutation, retains caller order for insertions at the same offset, and creates one undo unit by default. `selectionAfter` uses the resulting document's coordinates; when omitted, the current selection is mapped through the edits.

The method returns `false` for a stale revision, read-only state, active composition, or a length increase rejected by `MaxLength`. Invalid ranges and overlaps throw.

Use `Document.Tracking.TrackPosition` and `TrackRange` for positions that must follow edits and undo/redo. For example, an editable placeholder can include insertions at both edges and survive replacement:

```csharp
using var placeholder = editor.Document.Tracking.TrackRange(
    new RichTextRange(0, 5),
    startAffinity: RichTextTrackingAffinity.BeforeInsertion,
    endAffinity: RichTextTrackingAffinity.AfterInsertion,
    deletion: RichTextTrackingDeletionBehavior.Preserve);
```

Default range affinities exclude boundary insertions, and the default deletion behavior invalidates a handle when its source is removed. Invalidated or disposed handles return `null` and are not restored by undo. Handles remain attached to their original document when the editor's document changes.

### Text layout and input observation

`TextLayout.Capture()` returns visible lines, source ranges, viewport rectangles, and the document and layout revisions. Use `GetCaretBounds`, `GetRangeBounds`, and `HitTest` with that capture to query geometry in device-independent editor coordinates. These queries leave selection and scrolling unchanged.

Wrapped or bidirectional text can produce multiple rectangles. Hidden, offscreen, stale, or unavailable geometry returns no result. Recapture after `TextLayout.Changed`; use `CreateRelativeTo(ancestor)` for coordinates relative to a surrounding view.

`Composition` and `CompositionChanged` expose native marked-text state. `PointerMoved`, `PointerPressed`, and `PointerExited` observe pointer input without suppressing native selection or focus behavior.

### Commands and clipboard

`Commands` exposes `Undo`, `Redo`, `SelectAll`, `Copy`, `Cut`, and `Paste` as `ICommand` properties with editor-aware `CanExecute` state:

```xml
<Button Text="Undo"
        Command="{Binding Source={x:Reference Editor}, Path=Commands.Undo}" />
```

The corresponding methods are also available directly, including `CopyAsync`, `CutAsync`, and `PasteAsync`. Native menus and clipboard shortcuts use the same portable rich fragments. Windows and Apple publish RTF and plain text to the system clipboard. Paste raises `Pasting`, respects `MaxLength`, and uses the destination typing format for plain text. Android also supports paste as plain text.

Use selection operations for formatting buttons and custom commands. Refresh custom command state when `IsReadOnly`, selection, or formatting changes.

### Key bindings

`KeyBindings` maps exact key and modifier combinations to commands:

```csharp
var primary = OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst()
    ? EditorKeyModifiers.Meta
    : EditorKeyModifiers.Control;
editor.KeyBindings.Add(new(EditorKey.A, primary, editor.Commands.SelectAll));
```

The last matching binding takes precedence, receives its `CommandParameter`, and executes only if `CanExecute` returns `true`. A disabled binding allows native handling to continue. `Meta` means Command on Apple platforms; `AltGraph` distinguishes AltGr from Control+Alt.

`KeyDown` runs before bindings and default shortcuts. Set `Handled` to consume the event. Use key bindings for shortcut overrides on Apple, where they register as native key commands. These APIs handle hardware keys; soft keyboards and IME composition use native text input. Native composition takes precedence over custom shortcuts.

### Context menus

Add items through `ContextMenuOpening`:

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

Set `IncludeDefaultItems = false` to replace native items, or assign a `MenuFlyout` with `FlyoutBase.SetContextFlyout(editor, menu)`. Each opening receives a separate top-level item list. Items support text, submenus, separators, enabled state, click handlers, commands, and command parameters. Icon sources and menu-item keyboard accelerators are not rendered by the native menu adapters.

Menu customization supports Windows, Android, and iOS/Mac Catalyst 16 or later. Earlier Apple versions use their standard native menus.

## Code editor

`CodeEdit.Maui` provides a native code editor with plain-text documents, logical line numbers, and horizontal scrolling or word wrapping. It composes `RichEditor` through its public APIs and uses the same native text surfaces.

### Installation and setup

Install the package, which depends on the matching version of `RichEdit.Maui`:

```sh
dotnet add package CodeEdit.Maui --prerelease
```

Register the handler with `UseCodeEditor()`. This also registers the rich-text handler:

```csharp
using CodeEdit.Maui;
using RichEdit.Maui;

builder.UseCodeEditor();

var codeEditor = new CodeEditor
{
    Document = CodeDocument.FromPlainText("public class Example\n{\n}\n"),
    FontSize = 14,
    ShowLineNumbers = true,
    WordWrap = false,
    IndentSize = 4,
    UseTabs = false,
};
```

For XAML and data binding, give the editor a bounded area, such as a `Grid` row with `Height="*"`. The editor manages its own scrolling:

```xml
<Grid
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    xmlns:code="clr-namespace:CodeEdit.Maui;assembly=CodeEdit.Maui"
    RowDefinitions="*">
    <code:CodeEditor x:Name="CodeEditor"
                     Document="{Binding Document}"
                     SelectedRange="{Binding Selection, Mode=TwoWay}"
                     IsReadOnly="{Binding IsReadOnly}"
                     ShowLineNumbers="True"
                     WordWrap="False" />
</Grid>
```

### Source documents

`CodeDocument.FromPlainText` normalizes line endings to LF and starts with empty undo history and a clean saved state. `Document` is a `CodeDocument`, and `Selection` is a `CodeTextSelection`; both expose plain-text operations:

```csharp
codeEditor.Document.Edit(edit =>
    edit.InsertText(codeEditor.Document.Length, "// End of file\n"));

codeEditor.SelectedRange = new RichTextRange(0, 6);
codeEditor.Selection.ReplaceText("internal");

string source = codeEditor.Document.Text;
codeEditor.Undo();
codeEditor.Redo();
```

A source document can be attached to one editor at a time. Use `Document.Text` for persistence. `CurrentSnapshot` provides immutable source, a `Revision`, and line/column mapping for background inspection. Document edits, history, undo groups, and save points follow the same [undo and saving](#undo-redo-and-saving) behavior as rich-text documents.

`Commands` exposes `Undo`, `Redo`, `SelectAll`, `Copy`, `Cut`, and `Paste`, with corresponding methods on the control. Clipboard copies contain plain source. `Pasting` supports cancellation or replacement before the accepted fragment is converted to plain text.

### Editing settings

| Property | Default | Behavior |
| --- | --- | --- |
| `IndentSize` | `4` | Space indentation width, from 1 to 16 |
| `UseTabs` | `false` | Insert tabs for indentation; native tab stops determine their display width |
| `AutoIndent` | `true` | Copy leading whitespace when inserting a newline |
| `LineCommentPrefix` | `"//"` | Prefix used by the line-comment shortcut |
| `WordWrap` | `false` | Wrap long lines when enabled; otherwise scroll horizontally |
| `ShowLineNumbers` | `true` | Show logical line numbers aligned with native layout |
| `MaxLength` | `-1` | Maximum UTF-16 source length; `-1` means unlimited |

Tab and Shift+Tab indent and outdent. Control+/ toggles line comments, with Command+/ on Apple platforms. Enter inserts a newline with optional indentation. Automatic indentation after native newline input is merged into that input's undo unit.

`KeyBindings` includes these defaults and bindings that suppress rich-text formatting shortcuts. Add, replace, or remove bindings to customize them. `KeyDown`, `ContextMenuOpening`, and an attached `MenuFlyout` operate on the inner text surface, following the shared [key binding](#key-bindings) and [context menu](#context-menus) behavior.

### Lines and navigation

`LineCount` includes a trailing empty line. `CaretPosition` reports the active selection endpoint as a one-based logical line and UTF-16 column. Tabs occupy one column, and word wrapping does not add logical lines.

Use `GetPosition` and `GetOffset` to convert between offsets and line/column positions. `GetLineRange` excludes the line terminator:

```csharp
RichTextRange line = codeEditor.GetLineRange(2);
int offset = codeEditor.GetOffset(new CodePosition(2, 1));
codeEditor.Selection.Select(new RichTextSelectionState(offset, offset));
codeEditor.ScrollIntoView(codeEditor.SelectedRange);
```

`SelectionState` retains direction and binds two ways. Assigning `SelectedRange` selects forwards. Undo/redo restore the recorded selection; `ScrollIntoView` scrolls without changing it. `CodeDocumentSnapshot` also exposes `LineCount`, `GetLineRange`, `GetPosition`, and `GetOffset`.

### Syntax highlighting and extensions

Publish syntax colors through decoration layers:

```csharp
var colors = codeEditor.Decorations.CreateLayer();
var snapshot = codeEditor.Document.CurrentSnapshot;
int keywordStart = snapshot.Text.IndexOf("class", StringComparison.Ordinal);
if (keywordStart >= 0)
{
    colors.TrySet(snapshot.Revision,
    [
        new RichTextDecoration(new RichTextRange(keywordStart, 5),
            new RichTextDecorationStyle { ForegroundColor = Colors.Blue }),
    ]);
}
```

Colors remain outside source text, document versions, saved state, and undo history. Each component can keep its own layer and palette. `TrySet` copies and validates the supplied ranges and rejects stale revisions. For asynchronous analysis, capture a snapshot on the UI thread, inspect it in the background, and publish on the UI thread. Track request ordering separately when multiple requests use the same revision.

Use `TryApplyEdits` for [revision-checked replacements](#revisions-and-tracked-ranges), and `Document.Tracking` for positions or editable placeholders that follow source changes. `TextLayout` reports geometry in `CodeEditor` coordinates. Composition, pointer, selection, and keyboard events provide input observation.

`Folding` uses the shared [range folding](#range-folding) API, with line numbers continuing to refer to source lines. Expand hidden ranges explicitly before placing a visible caret. `Adornments` supports the same [anchored views](#anchored-views), including inline content and rows above or below a line.

Handle `DocumentChanged` to cancel pending analysis and release state tied to the old document. Detach event handlers and dispose decoration, tracking, and adornment handles when they are no longer needed.

## Development

The library and test sources are organized as follows:

| Directory | Contents |
| --- | --- |
| [`RichEdit.Maui`](RichEdit.Maui) | Document model, rich-text control, RTF codec, and native handlers |
| [`CodeEdit.Maui`](CodeEdit.Maui) | Code editor composed over the public RichEdit APIs |
| [`RichEdit.Maui.Tests`](RichEdit.Maui.Tests) | Document, control, and native editing tests |

Shared editor behavior lives alongside the document model. Android and Windows implementations are under `Platforms`; shared Apple implementations use `.Apple.cs` files. Public APIs include XML documentation, and missing documentation is treated as a build error.

### Building

Install the .NET 10 SDK and MAUI workloads for the platforms you want to build. From the repository root on Windows:

```powershell
dotnet workload restore RichEdit.Maui.slnx
dotnet build RichEdit.Maui.slnx -c Release
```

To build a library for one target:

```powershell
dotnet build RichEdit.Maui/RichEdit.Maui.csproj -c Release -f net10.0-windows10.0.19041.0
```

The other library targets are `net10.0-android`, `net10.0-ios`, and `net10.0-maccatalyst`. Linux includes the Android target; Windows also includes the Windows target. Apple native builds require the corresponding Apple toolchain.

Create NuGet packages with:

```powershell
dotnet pack RichEdit.Maui/RichEdit.Maui.csproj -c Release -o artifacts
dotnet pack CodeEdit.Maui/CodeEdit.Maui.csproj -c Release -o artifacts
```

### Testing

The Windows test project uses xUnit's in-process runner and requires the matching Windows App SDK runtime:

```powershell
dotnet build RichEdit.Maui.Tests/RichEdit.Maui.Tests.csproj -p:Platform=x64 -p:WindowsPackageType=None
& ./RichEdit.Maui.Tests/bin/x64/Debug/net10.0-windows10.0.19041.0/RichEdit.Maui.Tests.exe -parallelMode none -nocolor
```

Use `-class RichEdit.Maui.Tests.CodeEditorTests` to run the code editor tests only. Run the relevant document and native editing tests when changing shared behavior or platform handlers.

### Threading and events

Create and mutate controls on their owning UI thread. Attached document edits, history operations, and save-state changes require that thread. Serialize mutations to detached documents; capture immutable snapshots for background inspection.

Document, native view, selection, and history synchronization completes before public change notifications. Notifications arrive in this order: document `Changed`, editor `ContentChanged`/`TextChanged`, document property changes, then selection and selection-format changes. Mutating the same document recursively from these notifications throws. Subscriber exceptions propagate after the transaction has committed.

`ContentChanged` includes the `RichTextChangeSet`, with its origin, versions, and bounded changes. `DocumentChanged` reports document replacement. Other events include `SelectionChanged`, `SelectionFormatChanged`, `EffectiveAppearanceChanged`, `LinkInvoked`, `InlineObjectInvoked`, `Pasting`, and `Completed`.

Await clipboard methods to observe exceptions. Clipboard commands and native menu actions propagate asynchronous failures through UI exception handling.

### RTF behavior

The codec preserves paragraph breaks as `\n` and soft line breaks as U+2028. It handles Unicode fallback, code pages, group scoping, and ignorable destinations. Table cells are flattened to tab/newline text.

RTF stores opaque 8-bit RGB colors and half-point font sizes. Serialization rounds values to those units; nonzero alpha becomes opaque RGB, while fully transparent formatting colors mean unset/reset. See the [RTF support matrix](RTF_SUPPORT.md) before changing serialization or native formatting behavior.

## License

[MIT](LICENSE.txt)
