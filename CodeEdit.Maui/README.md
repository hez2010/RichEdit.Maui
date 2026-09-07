# CodeEdit.Maui

A native code editor for .NET MAUI, built in C# on `RichEdit.Maui`. It uses the same native text surfaces on Windows, Android, iOS, and Mac Catalyst.

The control provides C# syntax coloring, logical line numbers, horizontal scrolling or word wrapping, and light/dark palettes. It composes a `RichEditor` through public document, selection, clipboard, and history APIs. The sample app supplies toolbar actions and literal find/replace.

Syntax colors are view-owned decorations. Highlighting and theme changes do not change document content, versions, saved state, content events, or undo history. Persist source through `Document.Text`; clipboard copies contain source without syntax formatting.

## Register and create an editor

Reference `CodeEdit.Maui` and register its handler during MAUI startup. `UseCodeEditor()` also registers `RichEdit.Maui`:

```csharp
using CodeEdit.Maui;
using RichEdit.Maui;

builder.UseMauiApp<App>().UseCodeEditor();

var editor = new CodeEditor
{
    Document = CodeDocument.FromPlainText("public class Example\n{\n}\n"),
    Theme = CodeEditorTheme.Dark,
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

Give the editor a bounded area, such as a `Grid` row with `Height="*"`. It owns native scrolling. The test app's **Code editor** tab demonstrates the control with editing, search, wrapping, and theme controls; its page is written entirely in C#.

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

On Windows and Android, hardware Tab and Shift+Tab indent and outdent, and Ctrl+/ toggles line comments. The input settings above configure keyboard behavior and the sample toolbar. Enter inserts a newline with optional indentation; automatic indentation after native newline input is merged with that input's undo unit.

The sample applies block actions and replace-all through one `Document.Edit` transaction, which remaps the selection and records one undo unit. Its actions check read-only state and the length limit before editing. Its line-selection policy excludes a final line when the selection ends at that line's start.

`LineCount` includes a trailing empty line. `CaretPosition` reports the active endpoint as a one-based logical line and UTF-16 column. `SelectionState` holds the anchor and active offsets and binds two ways. `SelectedRange` is a normalized range; assigning it selects forwards. Undo/redo restore the recorded selection. `GetPosition` and `GetOffset` convert coordinates, and `ScrollIntoView` reveals a range without selecting it. Tabs occupy one UTF-16 column; visual wrapping does not add logical lines.

```csharp
RichTextRange line = editor.GetLineRange(12); // Excludes the line terminator.
int offset = editor.GetOffset(new CodePosition(12, 5));
editor.Selection.Select(new RichTextSelectionState(offset, offset));
editor.ScrollIntoView(editor.SelectedRange);
```

The sample's `CodeEditorActions` supplies `GoToLine`, `FindAll`, `FindNext`, `FindPrevious`, `ReplaceAll`, and `CodeSearchOptions`. Its searches are ordinal and literal, with application-defined word boundaries and optional wraparound.

## Syntax and themes

`CSharpSyntaxHighlighter` is the default. It classifies keywords, strings and character literals, comments, numbers, and preprocessor directives, including verbatim and raw strings. It performs lexical coloring: contextual keywords are always colored, and interpolation expressions are not parsed separately. Compiler diagnostics, completion, semantic classification, and folding are outside this version's scope.

Implement `ICodeSyntaxHighlighter` to supply another language or compiler-based classification. Return ordered, nonempty, non-overlapping `CodeToken` ranges using UTF-16 offsets. Unclassified text uses the theme's `TextColor`.

Highlighting is debounced by 120 ms and runs on a worker thread over an immutable source string. Highlighters must be thread-safe and should observe cancellation. Results are validated and discarded when superseded, when the document changes, or when the handler disconnects. Presentation waits until IME composition has ended and replaces the syntax decoration layer. CodeEditor reclassifies text after undo/redo. Application layers created through `Decorations.CreateLayer()` compose above syntax colors, so diagnostics or search highlights can be managed independently.

```csharp
editor.Highlighter = new CSharpSyntaxHighlighter();
await editor.RefreshHighlightingAsync(); // Call on the UI thread; also useful in tests.

editor.Theme = CodeEditorTheme.Dark with
{
    CommentColor = Color.FromArgb("#85B98C"),
};

editor.Highlighter = null; // Disable syntax coloring.
```

Automatic failures raise `HighlightingFailed`; an awaited explicit refresh propagates errors to its caller. Highlighting scans the complete source and updates changed presentation ranges. Native readback removes presentation overrides before committing authored content. This version is intended for ordinary source documents, not a virtualized multi-gigabyte file viewer.

## Validation

The library targets .NET 10 on Windows, Android, iOS, and Mac Catalyst. Windows integration tests use a real WinUI `RichEditBox` and cover native input, focus changes, undo/redo, clipboard conversion, scrolling, asynchronous cancellation, and the sample editing helpers. Native adapters in the code package use public platform APIs for keyboard shortcuts, IME state, wrapping, and visible-line geometry; they do not read or change RichEditor internals.

Build and run the focused tests on Windows with the installed MAUI workload and Windows App SDK runtime:

```powershell
dotnet build RichEdit.Maui.Tests/RichEdit.Maui.Tests.csproj -p:Platform=x64 -p:WindowsPackageType=None
& ./RichEdit.Maui.Tests/bin/x64/Debug/net10.0-windows10.0.19041.0/RichEdit.Maui.Tests.exe -class RichEdit.Maui.Tests.CodeEditorTests
```

The executable uses xUnit's in-process runner. Add `-p:SkipMauiWorkloadManifest=true` to the build when validating the .NET 10 targets with the locally installed workload. Mobile native behavior requires device or simulator validation.

## Threading and notifications

Create and mutate controls on their owning UI thread. An attached source document requires that thread for edits, history operations, and save-state changes; violations throw before committing. Detached documents require callers to serialize mutations. Capture an immutable `CurrentSnapshot` on the owning thread for background inspection.

Internal document/native/selection synchronization completes before public notifications. The order is document `Changed`, control `ContentChanged`/`TextChanged`, document property notifications, then selection and selection-format notifications. Mutating the same document recursively from these notifications throws.

Clipboard operation exceptions propagate from awaited `CopyAsync`, `CutAsync`, and `PasteAsync`. Clipboard `ICommand` and native-menu entry points propagate asynchronous failures to the application's UI exception handling; the library does not log and suppress them.
