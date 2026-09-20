---
title: Code editor
description: Configure source editing, line numbers, indentation, wrapping, and protocol-independent language integrations.
---

# Code editor

<xref:CodeEdit.Maui.CodeEditor> combines plain-text editing with line numbers, indentation, and wrapping. It uses RichEditor's native text surface and extension APIs.

## Configure editing

```csharp
var editor = new CodeEditor
{
    Document = CodeDocument.FromPlainText("class Example\n{\n    int value;\n}\n"),
    FontSize = 14,
    ShowLineNumbers = true,
    WordWrap = false,
    IndentSize = 4,
    UseTabs = false,
    AutoIndent = true,
    LineCommentPrefix = "//",
};
```

| Option | Default and behavior |
| --- | --- |
| `ShowLineNumbers` | `true`; numbers logical source lines |
| `WordWrap` | `false`; long lines scroll horizontally |
| `IndentSize` | `4`; valid values are 1 through 16 |
| `UseTabs` | `false`; indentation inserts spaces |
| `AutoIndent` | `true`; Enter copies the current line's leading whitespace |
| `LineCommentPrefix` | `//`; must be nonempty with no whitespace |
| `FontFamily` | Consolas on Windows, monospace on Android, Menlo on Apple |

`IndentSize` controls inserted indentation, not native tab width. Wrapped fragments retain the same logical line number.

## Editing shortcuts

| Hardware shortcut | Action |
| --- | --- |
| Tab | Insert indentation at the caret or indent selected logical lines |
| Shift+Tab | Remove one indentation level from selected lines |
| Enter | Insert a newline and, if enabled, copy leading whitespace |
| Control+/ on Windows/Android, Command+/ on Apple | Toggle the configured line-comment prefix |
| Primary modifier + B, I, U | Suppress rich-text formatting shortcuts |

Indenting selected lines and toggling comments each form one undo unit. These shortcuts are entries in `KeyBindings`; a later binding can override one for a completion popup or snippet session.

## Read and edit source

```csharp
editor.Document.Edit(edit =>
    edit.InsertText(editor.Document.Length, "// End of file\n"));

editor.SelectedRange = new RichTextRange(0, 5);
editor.Selection.ReplaceText("struct");
string source = editor.Document.Text;
```

<xref:CodeEdit.Maui.CodeDocument> supports atomic text edits, snapshots, history, undo groups, and save points. `CodeTextSelection` exposes selection and plain-text replacement.

## Convert source positions

```csharp
var snapshot = editor.Document.CurrentSnapshot;
var line = snapshot.GetLineRange(2);
var location = snapshot.GetPosition(line.Start); // Line 2, Column 1.
int offset = snapshot.GetOffset(new CodePosition(2, 1));
editor.SelectionState = new RichTextSelectionState(offset, offset);
editor.ScrollIntoView(new RichTextRange(offset, 0));
```

Line and column numbers are one-based, and columns count UTF-16 code units. `GetLineRange` excludes the line terminator. A trailing newline contributes an empty final line to `LineCount`. The live editor exposes these conversions too, plus `CaretPosition` for the active selection endpoint.

A language service may use zero-based positions or a different encoding. Convert its positions against the snapshot used for that request.

## Add language features

Language features can use a parser or language server of your choice. The editor supplies these building blocks:

| Feature | Building blocks |
| --- | --- |
| Syntax colors and diagnostic underlines | Snapshot analysis and [decoration layers](decorations.md) |
| Formatting, rename, completion insertion | [Revision-checked edits](background-work.md) |
| Snippet placeholders and bookmarks | [Tracked ranges and positions](tracking.md) |
| Fold regions and navigation | [Folding](folding.md), selection, `ScrollIntoView` |
| Inline hints and line actions | [Adornments](adornments.md) |
| Hover information or completion popup placement | [Layout and pointer observation](layout.md) |

LSP clients and document sessions live outside the control. The sample's completion UI, snippet navigation, and syntax analysis use these APIs; they are not built-in CodeEditor services.
