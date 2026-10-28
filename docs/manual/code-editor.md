---
title: Code editor
description: Native source editing, selections, and application language tools.
---

# Code editor

`CodeEditor` edits plain source in a `CodeDocument`. Formatting and language tools are view presentation; they do not alter the source text.

```csharp
var editor = new CodeEditor
{
    Document = new CodeDocument("first\r\nsecond\n"),
    ShowLineNumbers = true,
    WordWrap = false,
    IndentSize = 4,
    TabSize = 4,
    UseTabs = false,
    AutoIndent = true,
    NewLine = "\r\n",
};
```

`IndentSize` controls inserted indentation; `TabSize` controls native tab display. `NewLine` is an insertion policy. Use an explicit document edit with `NormalizeLineEndings` to change existing terminators.

| Option | Default and behavior |
| --- | --- |
| `ShowLineNumbers` | `true`; displays logical line numbers, including a trailing empty line. |
| `WordWrap` | `false`; enables wrapping when set to true. |
| `IndentSize` | `4`; accepts 1–16 spaces per indentation level. |
| `TabSize` | `4`; accepts 1–32 space advances per displayed tab. Existing tab characters stay unchanged. |
| `UseTabs` | `false`; set to true to insert tab characters for indentation. |
| `AutoIndent` | `true`; Enter copies the current line's leading whitespace. Language-specific indentation belongs in an input rule. |
| `LineCommentPrefix` | `"//"`; set a nonempty prefix without whitespace, such as `"#"`, for the document's language. |
| `NewLine` | Controls newly inserted line endings; set it explicitly to LF, CRLF, or CR for your file policy. |

For appearance, placeholders, and editing limits, see [Control configuration](configuration.md). `NewLine` is a CLR property rather than a bindable property; set it from application code when opening or changing a file's insertion policy.

`Indent()`, `Outdent()`, `InsertNewLine()`, and `ToggleLineComment()` apply the same editing policies as their keyboard bindings to all current selections. Use these methods for toolbar and menu commands. Each operation is one undoable edit and respects read-only mode and the length limit.

`GetPosition`, `GetOffset`, `GetLineRange`, and snapshot `GetLine` use zero-based UTF-16 coordinates. `CaretPosition` describes the primary caret. Add one for user-facing line/column displays.

## Multiple selections

`Selections` contains stable directional selections with one primary. `AddCaretAbove`, `AddCaretBelow`, `AddCaret`, and `ReplaceSelections` operate through native geometry and shared history. Virtual space is view-owned and materializes as spaces only when an edit commits.

Alt-pointer gestures add carets and derive rectangular selections from native visual rows. A drag retains its source anchor and scrolls when the pointer leaves the text viewport; rows already traversed remain selected. Native glyph bounds determine disjoint selections within bidi rows, including tabs, wrapping, and variable-width text. `SelectRectangle` applies the same geometry rules to the currently visible rows. Virtual space begins at logical line ends, never at a soft wrap. Logical columns must not be treated as pixel X positions.

`RevealAsync` supports nearest-edge, top, center, and bottom alignment without moving selection or focus. `CaptureScrollAnchor` creates a disposable position that follows document edits and can be restored in another view of the same document. `ViewportChanged` includes source coordinates and a caller-supplied cause token for linked scrolling. See [layout](layout.md).

## Language features

Applications own parsing, language servers, completion UI, diagnostics, project systems, and debugging. Use immutable snapshots for background analysis, checked edits for completion/refactoring, input rules for dependent typing, decoration layers for colors, and lazy widget sources for hints and actions. See [background work](background-work.md).

Start with [source editing recipes](source-workflows.md) for find/replace, go-to-line, explicit multiple selections, virtual carets, line-ending conversion, and saving. The library does not select a language service or provide a built-in search or completion panel.

For large files, give the editor finite viewport constraints and fill alignment. Set `WordWrap = false` when wrapping very long logical lines is unsuitable. Large files and long lines can still reduce responsiveness; see [platform behavior](platforms.md).
