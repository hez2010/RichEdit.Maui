# CodeEdit.Maui

A native code editor for .NET MAUI, built in C# on `RichEdit.Maui`. It uses the same native text surfaces on Windows, Android, iOS, and Mac Catalyst.

The control includes C# syntax coloring, logical line numbers, horizontal scrolling or word wrapping, indentation, line comments, literal find/replace, and light/dark palettes. It contains an ordinary `RichEditor` and uses public document, selection, clipboard, and history APIs. It does not subclass RichEditor or its handlers.

Syntax colors are derived character formatting in the document. They preserve undo/redo and do not change source text. Persist code through `Document.Text`; RTF and rich clipboard copies include the current syntax colors.

## Register and create an editor

Reference `CodeEdit.Maui` and register its handler during MAUI startup. `UseCodeEditor()` also registers `RichEdit.Maui`:

```csharp
using CodeEdit.Maui;
using RichEdit.Maui;

builder.UseMauiApp<App>().UseCodeEditor();

var editor = new CodeEditor
{
    Document = RichTextDocument.FromPlainText("public class Example\n{\n}\n"),
    Theme = CodeEditorTheme.Dark,
    FontSize = 14,
    IndentSize = 4,
    ShowLineNumbers = true,
    WordWrap = false,
};
```

The package requires `RichEdit.Maui` 0.1.0-preview.4 or later, which provides general history-preserving formatting and undo grouping APIs.

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

Load plain source with `RichTextDocument.FromPlainText`. Line endings are normalized to `\n`, and a newly created document starts with empty undo history. Persist source using `Document.Text`:

```csharp
var source = editor.Document.Text;

editor.Document.Edit(edit =>
    edit.InsertText(editor.Document.Length, "// Added by the application\n"));

editor.SelectedRange = new RichTextRange(0, 6);
editor.Selection.ReplaceText("internal");

editor.Undo();
editor.Redo();
```

`Document`, `SelectedRange`, `Selection`, clipboard operations, and history use RichEdit's existing contracts. A document can belong to only one editor at a time. `Pasting` allows cancellation or replacement before the accepted fragment is converted to plain text.

Use plain source documents for this control. Rich content such as images, fields, links, and lists belongs in `RichEditor`.

## Editing and navigation

`Commands` exposes `ICommand` instances for `Indent`, `Outdent`, `InsertNewLine`, `ToggleLineComment`, `Undo`, `Redo`, `SelectAll`, `Copy`, `Cut`, and `Paste`. The corresponding methods are also available directly.

| Setting | Default | Behavior |
|---|---|---|
| `IndentSize` | 4 | Width for space indentation; valid values are 1–16 |
| `UseTabs` | false | Insert a tab instead of spaces; display tab stops follow the native editor |
| `AutoIndent` | true | Copy leading whitespace when inserting a newline |
| `LineCommentPrefix` | `//` | Prefix used by the line-comment toggle |
| `WordWrap` | false | Wrap long lines when enabled; otherwise scroll horizontally |
| `ShowLineNumbers` | true | Show logical line numbers aligned with native layout |
| `MaxLength` | -1 | Maximum UTF-16 source length, or unlimited |

On Windows and Android, hardware Tab and Shift+Tab indent and outdent, and Ctrl+/ toggles line comments. The same editing commands are available to application toolbars on every platform. Enter inserts a newline with optional indentation; automatic indentation after native newline input is merged with that input's undo unit.

Block commands and replace-all each use one transaction and undo unit. A selection ending at the beginning of the next line excludes that line. Read-only state and the length limit also apply to code-editing commands.

`LineCount` includes a trailing empty line. `CaretPosition` reports the selection start as a one-based logical line and UTF-16 column. Tabs occupy one UTF-16 column; visual wrapping does not add logical lines.

```csharp
editor.GoToLine(12, column: 5);
RichTextRange line = editor.GetLineRange(12); // Excludes the line terminator.

var options = new CodeSearchOptions(MatchCase: true, WholeWord: true);
editor.FindNext("count", options);
editor.FindPrevious("count", options);
var matches = editor.FindAll("count", options);
int replaced = editor.ReplaceAll("count", "total", options);
```

Search is ordinal and literal. Next/previous searches wrap by default; pass `wrap: false` to stop at the document boundary. Empty queries have no matches.

## Syntax and themes

`CSharpSyntaxHighlighter` is the default. It classifies keywords, strings and character literals, comments, numbers, and preprocessor directives, including verbatim and raw strings. It performs lexical coloring: contextual keywords are always colored, and interpolation expressions are not parsed separately. Compiler diagnostics, completion, semantic classification, and folding are outside this version's scope.

Implement `ICodeSyntaxHighlighter` to supply another language or compiler-based classification. Return ordered, nonempty, non-overlapping `CodeToken` ranges using UTF-16 offsets. Unclassified text uses the theme's `TextColor`.

Highlighting is debounced by 120 ms and runs on a worker thread over an immutable source string. Highlighters must be thread-safe and should observe cancellation. Results are validated and discarded when superseded, when the document changes, or when the handler disconnects. Formatting waits until IME composition has ended, then uses a tagged `Document.Edit` with `RichTextUndoBehavior.PreserveHistory`. The tag prevents recursive highlighting. Undo/redo can restore older formatting; CodeEditor reclassifies the restored text.

```csharp
editor.Highlighter = new CSharpSyntaxHighlighter();
await editor.RefreshHighlightingAsync(); // Call on the UI thread; also useful in tests.

editor.Theme = CodeEditorTheme.Dark with
{
    CommentColor = Color.FromArgb("#85B98C"),
};

editor.Highlighter = null; // Disable syntax coloring.
```

Automatic failures raise `HighlightingFailed`; an awaited explicit refresh propagates errors to its caller. Highlighting scans the complete source, then compares tokens with existing document runs and changes only the foreground ranges that differ. This version is intended for ordinary source documents, not a virtualized multi-gigabyte file viewer.

## Validation

The library targets .NET 10 on Windows, Android, iOS, and Mac Catalyst. Windows integration tests use a real WinUI `RichEditBox` and cover native input, focus changes, undo/redo, clipboard conversion, scrolling, asynchronous cancellation, and editing commands. Native adapters in the code package use public platform APIs for keyboard shortcuts, IME state, wrapping, and visible-line geometry; they do not read or change RichEditor internals.

Build and run the focused tests on Windows with the installed MAUI workload and Windows App SDK runtime:

```powershell
dotnet build RichEdit.Maui.Tests/RichEdit.Maui.Tests.csproj -p:Platform=x64 -p:WindowsPackageType=None
& ./RichEdit.Maui.Tests/bin/x64/Debug/net10.0-windows10.0.19041.0/RichEdit.Maui.Tests.exe -class RichEdit.Maui.Tests.CodeEditorTests
```

The executable uses xUnit's in-process runner. Add `-p:SkipMauiWorkloadManifest=true` to the build when validating the .NET 10 targets with the locally installed workload. Mobile native behavior still requires device or simulator validation.
