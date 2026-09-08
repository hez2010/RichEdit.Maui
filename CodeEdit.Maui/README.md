# CodeEdit.Maui

A native code editor for .NET MAUI, built in C# on `RichEdit.Maui`. It uses the same native text surfaces on Windows, Android, iOS, and Mac Catalyst.

The control provides LSP semantic coloring, logical line numbers, and horizontal scrolling or word wrapping. Applications supply an initialized `CodeEdit.Lsp.LspClient`, choose how it connects to a server, and provide a token-to-color delegate. It composes a `RichEditor` through public document, selection, clipboard, and history APIs. The sample app supplies toolbar actions and literal find/replace.

Syntax colors are view-owned decorations. Highlighting and theme changes do not change document content, versions, saved state, content events, or undo history. Persist source through `Document.Text`; clipboard copies contain source without syntax formatting.

## Register and create an editor

Reference `CodeEdit.Maui` and register its handler during MAUI startup. `UseCodeEditor()` also registers `RichEdit.Maui`:

```csharp
using CodeEdit.Maui;
using RichEdit.Maui;

builder.UseMauiApp<App>().UseCodeEditor();

var editor = new CodeEditor
{
    Document = CodeDocument.FromPlainText("public class Example\n{\n}\n", languageId: "csharp"),
    Theme = static token => token.Type == "keyword" ? Colors.Blue : null,
    FontSize = 14,
    IndentSize = 4,
    ShowLineNumbers = true,
    WordWrap = false,
};
```

The package uses the matching `RichEdit.Maui` version, including its general decoration, directional-selection, and document-history APIs.

XAML and MVVM work with the same control:

```xml
<code:CodeEditor
    xmlns:code="clr-namespace:CodeEdit.Maui;assembly=CodeEdit.Maui"
    x:Name="Editor"
    Document="{Binding Document}"
    SelectedRange="{Binding Selection, Mode=TwoWay}"
    IsReadOnly="{Binding IsReadOnly}"
    ShowLineNumbers="True"
    WordWrap="False" />
```

Give the editor a bounded area, such as a `Grid` row with `Height="*"`. It owns native scrolling. The sample app's **Code editor** tab demonstrates the control with editing, search, wrapping, and theme controls; its page is written entirely in C#.

## Work with source

Load plain source with `CodeDocument.FromPlainText`. Line endings are normalized to `\n`, and a newly created document starts with empty undo history. Persist source using `Document.Text`:

```csharp
var source = editor.Document.Text;

editor.Document.Edit(edit =>
    edit.InsertText(editor.Document.Length, "// Added by the application\n"));

editor.SelectedRange = new RichTextRange(0, 6);
editor.Selection.ReplaceText("internal");

editor.Undo();
editor.Redo();
```

`Document` is a `CodeDocument`, and `Selection` is a `CodeTextSelection`. Their public APIs expose plain source operations. A document can belong to one editor at a time. `Pasting` allows cancellation or replacement before the accepted fragment is converted to plain text.

A `CodeDocument` has immutable source snapshots, atomic text edits, public undo/redo, undo descriptions, and `IsModified`. `CreateSavePoint()` captures the state associated with a snapshot; after an asynchronous save succeeds, `MarkSaved(savePoint)` marks that captured state without marking later edits as saved. Undoing back to the saved state clears `IsModified`.

## Editing and navigation

`Commands` exposes the core `ICommand` instances: `Undo`, `Redo`, `SelectAll`, `Copy`, `Cut`, and `Paste`. The corresponding methods are also available directly. Toolbar actions such as indentation, newline insertion, line comments, find/replace, and go-to-line belong to the application. The sample implements them in [CodeEditorActions.cs](../RichEdit.Maui.TestApp/CodeEditorActions.cs) using public document, selection, line, and scrolling APIs.

| Setting | Default | Behavior |
|---|---|---|
| `IndentSize` | 4 | Width for space indentation; valid values are 1–16 |
| `UseTabs` | false | Insert a tab instead of spaces; display tab stops follow the native editor |
| `AutoIndent` | true | Copy leading whitespace when inserting a newline |
| `LineCommentPrefix` | `//` | Prefix used by the line-comment toggle |
| `WordWrap` | false | Wrap long lines when enabled; otherwise scroll horizontally |
| `ShowLineNumbers` | true | Show logical line numbers aligned with native layout |
| `MaxLength` | -1 | Maximum UTF-16 source length, or unlimited |

Default hardware bindings use Tab and Shift+Tab to indent and outdent, and Control+/ (Command+/ on Apple platforms) to toggle line comments. The input settings above configure keyboard behavior and the sample toolbar. Enter inserts a newline with optional indentation; automatic indentation after native newline input is merged with that input's undo unit.

The sample applies block actions and replace-all through one `Document.Edit` transaction, which remaps the selection and records one undo unit. Its actions check read-only state and the length limit before editing. Its line-selection policy excludes a final line when the selection ends at that line's start.

`LineCount` includes a trailing empty line. `CaretPosition` reports the active endpoint as a one-based logical line and UTF-16 column. `SelectionState` holds the anchor and active offsets and binds two ways. `SelectedRange` is a normalized range; assigning it selects forwards. Undo/redo restore the recorded selection. `GetPosition` and `GetOffset` convert coordinates, and `ScrollIntoView` reveals a range without selecting it. Tabs occupy one UTF-16 column; visual wrapping does not add logical lines.

```csharp
RichTextRange line = editor.GetLineRange(12); // Excludes the line terminator.
int offset = editor.GetOffset(new CodePosition(12, 5));
editor.Selection.Select(new RichTextSelectionState(offset, offset));
editor.ScrollIntoView(editor.SelectedRange);
```

The sample's `CodeEditorActions` supplies `GoToLine`, `FindAll`, `FindNext`, `FindPrevious`, `ReplaceAll`, and `CodeSearchOptions`. Its searches are ordinal and literal, with application-defined word boundaries and optional wraparound.

## Range folding

`Folding` provides explicit, view-owned range folding. Applications choose the ranges and when to collapse or expand them:

```csharp
var range = editor.SelectedRange;
if (!range.IsEmpty)
    editor.Folding.Collapse(range);

editor.Folding.Expand(range); // Removes this exact collapsed range.
editor.Folding.ExpandAll();
```

`SetCollapsedRanges(ranges)` replaces the collapsed set atomically. `CollapsedRanges` exposes its current source ranges, and `Changed` reports changes. Nested ranges retain independent state: expanding a parent leaves its collapsed children in place. Overlapping ranges are hidden as a union; `GetCollapsedRange(offset)` returns the union containing a character, or null. Use an entry from `CollapsedRanges` with `Expand` to remove an individual fold.

Ranges use the document's UTF-16 offsets and can include line terminators or text within one line. Folding removes the range's layout space while retaining the complete source in `Document.Text`, selection, clipboard operations, and language-server synchronization. Source versions, saved state, and undo history are unaffected. Line numbers continue to refer to source lines.

Ranges track text edits. Insertions at either boundary remain visible; insertions inside a fold stay inside it. Replacing or deleting an entire folded range removes that fold. Document replacement clears the collapsed set. Undo and redo operate on source and do not restore discarded fold state. After edits, use the remapped ranges in `CollapsedRanges` when calling `Expand`.

The control does not discover folds, request LSP folding ranges, add folding shortcuts or gutter actions, or expand folds during navigation. Applications can request `LspMethods.FoldingRanges` through `GetLanguageDocumentAsync()` and convert the response to source ranges themselves. Folding mutations require the UI thread and throw `InvalidOperationException` during IME composition; they are not queued for later.

Expand a fold explicitly when its text needs a visible native caret or selection. Source selection and document-edit APIs can address collapsed text directly, including a completely collapsed document.

## Key bindings and context menus

`KeyBindings`, `KeyDown`, and `ContextMenuOpening` operate on the inner text surface. The bindings collection includes CodeEditor's Tab, Shift+Tab, Enter, line-comment, and rich-format-shortcut suppression defaults. Applications can replace or remove any binding; the last matching entry takes precedence.

```csharp
var primary = OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst()
    ? EditorKeyModifiers.Meta
    : EditorKeyModifiers.Control;
editor.KeyBindings.Add(new(EditorKey.F, primary, findCommand));

editor.ContextMenuOpening += (_, args) =>
{
    args.Items.Add(new MenuFlyoutSeparator());
    args.Items.Add(new MenuFlyoutItem { Text = "Find…", Command = findCommand });
};
```

`KeyDown.Handled` suppresses further key handling. Bindings check `CanExecute`, pass their command parameter, and preserve native IME composition. These APIs handle hardware keyboards; soft-keyboard edits continue through native text input.

Set `IncludeDefaultItems = false` in `ContextMenuOpening` to supply a replacement menu, or assign a `MenuFlyout` through `FlyoutBase.SetContextFlyout(editor, menu)`. The attached menu also supplies the inner text surface's editing menu. Menu commands can call application-owned helpers such as the sample's search and indentation actions.

The [shared input and menu API](../README.md#key-bindings) documents modifier semantics and supported menu fields. Native menu customization is available on Windows, Android, and iOS/Mac Catalyst 16 or later; earlier Apple versions use their standard menus.

## Language servers and themes

Language integration uses [CodeEdit.Lsp](../CodeEdit.Lsp/README.md). The application implements its `LspConnection` contract and initializes a client. Connections may dispatch to an in-process server directly or use an application's chosen transport to an existing server. The library supplies the client APIs and source-generated payload metadata; connection implementations belong to the application.

```csharp
using CodeEdit.Lsp;

// applicationConnection is an application-owned implementation of LspConnection.
var client = await LspClient.ConnectAsync(applicationConnection, new()
{
    RootUri = new Uri("file:///workspace/"),
});

editor.LanguageServer = client;
editor.Document = new CodeDocument(source, new Uri("file:///workspace/Example.cs"), "csharp");
await editor.RefreshHighlightingAsync();
```

`CodeDocument.Uri` is a stable absolute URI, and `LanguageId` is a standard LSP language identifier. Omitted values create a unique untitled URI and use `plaintext`. Set the language explicitly for language-specific features. A client can be shared by multiple editors. Each editor opens and synchronizes its own document; replacing the source or client, or disconnecting the native handler, closes that session. The application disposes the shared client when finished with it.

Semantic-token requests are debounced by 120 ms. Full tokens are requested when supported, with a whole-document range fallback. Tokens are decoded against the negotiated server legend and retain the server's type and modifier names. Results are discarded when superseded, when the source changes, or when the handler disconnects. Presentation waits until IME composition has ended and replaces the syntax decoration layer. Source notifications remain ordered across rapid edits and undo/redo.

```csharp
editor.Theme = token => token.Type switch
{
    "comment" => Colors.ForestGreen,
    "variable" when token.Modifiers.Contains("readonly") => Colors.Teal,
    _ => null,
};

editor.TextColor = Colors.Black;
editor.BackgroundColor = Colors.White;

editor.Theme = null; // Clear syntax colors while keeping language services attached.
```

`CodeEditorTheme` is a `Color? (SemanticToken token)` delegate. It receives the complete token, including its source range, type, and modifiers. Returning null leaves that token's normal appearance unchanged. The callback runs on the UI thread and should be fast and avoid modifying the editor. Assigning a new delegate recolors cached tokens without another language-server request. Color choices and token mapping belong to the application.

`TextColor` sets the normal source and line-number color; null uses the native default. `BackgroundColor` styles the text surface and gutter. Without a theme delegate or language server, source remains uncolored. The former `ICodeSyntaxHighlighter`, `CSharpSyntaxHighlighter`, and five-category token API have been removed.

Use `GetLanguageDocumentAsync()` on the UI thread to obtain the synchronized, editor-owned LSP session:

```csharp
var language = await editor.GetLanguageDocumentAsync();
if (language is not null)
{
    var position = editor.GetLspPosition(editor.SelectionState.Active);
    var completions = await language.GetCompletionsAsync(position);
    var hover = await language.GetHoverAsync(position);
}
```

The session provides completion, resolve, hover, definition, reference, formatting, rename, and generic LSP requests. A source edit cancels requests for an older revision. `LspMethods` supplies additional typed contracts, and source-generated metadata supports custom typed methods without reflection. Applications own completion UI, hover presentation, navigation, and workspace edit application. `CodeDocument` is the source of truth; change it through its editing API and let the editor synchronize the session.

`Diagnostics` and `DiagnosticsChanged` expose server diagnostics on the UI thread. Versioned diagnostics are checked against the current LSP revision, and edits clear the collection. Servers may omit diagnostic versions; such notifications are associated with the currently attached document, and the client cannot establish their original revision. Application layers created through `Decorations.CreateLayer()` compose above syntax colors and can present diagnostics or search results independently.

Apply returned text edits only against the exact snapshot used for the request:

```csharp
var language = await editor.GetLanguageDocumentAsync();
if (language is not null)
{
    var snapshot = editor.Document.CurrentSnapshot;
    var edits = await language.FormatAsync(editor.IndentSize, !editor.UseTabs);
    if (edits is not null)
        editor.ApplyLanguageServerEdits(edits, snapshot, "Format document");
}
```

`ApplyLanguageServerEdits` validates ranges and overlaps before applying one undoable transaction. It returns false for stale snapshots, read-only or composing editors, and changes that exceed `MaxLength`. LSP coordinates are zero-based UTF-16 positions; use `GetLspPosition` and the `GetOffset(LspPosition)` overload to convert them. Existing `CodePosition` APIs remain one-based.

Automatic failures raise `LanguageServerFailed`; explicitly awaited requests and refreshes propagate errors to their caller. Semantic coloring and diagnostic notifications do not change source versions, saved state, selection, RTF, or undo history. The Code editor sample accepts a shared client through its `CodeEditorPage(LspClient?)` constructor.

## Validation

The library targets .NET 10 on Windows, Android, iOS, and Mac Catalyst. Native adapters in the code package use public platform APIs for keyboard shortcuts, IME state, wrapping, and visible-line geometry; they do not read or change RichEditor internals.

Build and run the focused tests on Windows with the installed MAUI workload and Windows App SDK runtime:

```powershell
dotnet build RichEdit.Maui.Tests/RichEdit.Maui.Tests.csproj -p:Platform=x64 -p:WindowsPackageType=None
& ./RichEdit.Maui.Tests/bin/x64/Debug/net10.0-windows10.0.19041.0/RichEdit.Maui.Tests.exe -class RichEdit.Maui.Tests.CodeEditorTests
```

The executable uses xUnit's in-process runner. Add `-p:SkipMauiWorkloadManifest=true` to the build when validating the .NET 10 targets with the locally installed workload.

## Threading and notifications

Create and mutate controls on their owning UI thread. An attached source document requires that thread for edits, history operations, and save-state changes; violations throw before committing. Detached documents require callers to serialize mutations. Capture an immutable `CurrentSnapshot` on the owning thread for background inspection.

Internal document/native/selection synchronization completes before public notifications. The order is document `Changed`, control `ContentChanged`/`TextChanged`, document property notifications, then selection and selection-format notifications. Mutating the same document recursively from these notifications throws.

Clipboard operation exceptions propagate from awaited `CopyAsync`, `CutAsync`, and `PasteAsync`. Clipboard `ICommand` and native-menu entry points propagate asynchronous failures to the application's UI exception handling; the library does not log and suppress them.
