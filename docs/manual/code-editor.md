---
title: Code editor
description: Native source editing, selections, and application language tools.
---

# Code editor

`CodeEditor` is a native source editor over `CodeDocument`, backed by the platform-independent `TextDocument` core. It shares native integration with the rich control and owns plain source semantics.

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

`GetPosition`, `GetOffset`, `GetLineRange`, and snapshot `GetLine` use zero-based UTF-16 coordinates. `CaretPosition` describes the primary caret. Add one for user-facing line/column displays.

## Multiple selections

`Selections` contains stable directional selections with one primary. `AddCaretAbove`, `AddCaretBelow`, `AddCaret`, and `ReplaceSelections` operate through native geometry and shared history. Virtual space is view-owned and materializes as spaces only when an edit commits.

Alt-pointer gestures add carets and derive rectangular selections from visible native visual rows. `SelectRectangle` supports native wrapping, tabs, and variable-width endpoints. The current rectangle API operates on the visible viewport; it does not retain a drag rectangle across scrolling or produce disjoint bidi intervals within one row. Logical columns must not be treated as pixel X positions.

## Language features

Applications own parsing, language servers, completion UI, diagnostics, project systems, and debugging. Use immutable snapshots for background analysis, checked edits for completion/refactoring, input rules for dependent typing, decoration layers for colors, and lazy widget sources for hints and actions. See [background work](background-work.md).

Native controls retain their own buffers and layout costs. The managed persistent tree does not make native editing cost independent of file size. Large-file and long-line benchmarks validate correctness and record latency; this preview does not guarantee the proposed desktop/mobile latency budgets. Use a bounded viewport and disable wrapping when it is unsuitable for very long logical lines.
