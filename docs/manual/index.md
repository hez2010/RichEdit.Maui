---
title: User manual
description: A practical guide to rich-text editing, code editing, document persistence, and editor extensions in .NET MAUI.
---

# User manual

RichEdit.Maui edits rich text and RTF. CodeEdit.Maui edits plain source text, with line numbers, indentation, and hooks for language tools. Both use native text controls on Windows, Android, iOS, and Mac Catalyst.

## Choose a control

| | RichEdit.Maui | CodeEdit.Maui |
| --- | --- | --- |
| Control | <xref:RichEdit.Maui.RichEditor> | <xref:CodeEdit.Maui.CodeEditor> |
| Live document | <xref:RichEdit.Maui.RichTextDocument> | <xref:CodeEdit.Maui.CodeDocument> |
| Persistence | RTF or plain text | Plain source text |
| Built-in editing | Rich character and paragraph formats, lists, links, fields, images | Logical line numbers, indentation, comments, wrapping |
| Extensions | Decorations, folding, adornments, tracking, layout and input observation | The same shared extension APIs |

## Documents and views

Text, formatting, and undo history belong to the document. Selection, scrolling, decorations, folds, and anchored views belong to the editor. These display features are not saved in the document or added to its undo history.

A live document can attach to one editor at a time. Its immutable snapshots can be read in the background; changes to an attached document run on the editor's UI thread.

## Example conventions

Examples assume the usual MAUI namespaces plus `RichEdit.Maui`. Code-editor examples also use `CodeEdit.Maui`. Unless a snippet creates its own control, `editor` refers to an existing editor on the UI thread.
