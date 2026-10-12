---
title: RichEdit Studio sample
description: Explore rich documents and source editing in the sample app.
---

# RichEdit Studio sample

RichEdit Studio has separate rich-text and code-editor tabs. The tools act on the main view. Turn on **Split view** to edit the same document in a second pane with its own selection and scroll position. Each pane has Undo and Redo; both use the document's shared history.

## Rich document

The sample opens the bundled **RTF reference** document. **RTF reference**, **Studio notes**, and **Feature tour** load the three bundled examples. **New** opens an empty document. **Theme** changes the app's light or dark appearance.

Choose a group in **Editor tools**. Tool rows scroll horizontally on narrow screens.

| Group | Available actions |
| --- | --- |
| Formatting | Font family and size, bold, italic, underline styles, strike-through, superscript/subscript, text color, highlighting, and clearing direct formatting. More character options include case, outline/shadow, visibility, baseline offset, spacing, width, language, and direction. |
| Paragraphs | Alignment, leading and first-line indentation, hanging indentation, line spacing, paragraph spacing, tab stops, direction, and paragraph keep options. |
| Lists & styles | Reusable Body/Heading styles, bullet/numbered lists, number styles, restart, paragraph indentation, and inserting a nested list. |
| Insert | Hyperlinks, unlinking, an image, date/custom fields, and soft breaks. Field instructions are stored as data; the displayed result is supplied separately. |
| Tables | Insert, select, navigate, merge/split, add/delete rows or columns, clear cells, change widths and cell appearance, and delete the table. |
| Annotations & review | Comments, bookmarks, Original/Final review, and accepting or rejecting tracked changes. |
| Selections | Select all or a paragraph, add carets above/below, keep the primary caret, cut/copy/paste, and paste plain text. |
| Checked edits | Capture a selection and revision, apply a checked replacement, or add a transient highlight. Changing the document after capture demonstrates stale-edit rejection. |
| Tracking | Track a text range or caret bookmark through edits and undo, navigate to it, and clear tracking. These transient anchors are separate from saved annotations. |
| Widgets | Add overlay, margin, inline, above-line, or below-line widgets, then clear them. Widgets and presentation highlights do not enter RTF or undo history. |
| Layout & input | Capture and inspect layout, inspect the selection, show native selection/caret bounds, observe pointer addresses and composition, and request composition completion. |
| Document options | Read-only mode, spell checking, prediction, Tab input, saving, and the supported RTF profile. |

Formatting actions apply to the selected content. With an empty text selection, character formatting sets the appearance of newly typed text. The formatting controls show the current selection's values where they agree; a mixed font or size has no selected picker item.

**Styles & lists** applies Body or Heading to selected paragraphs, changes the reusable Heading style, creates bullet/numbered lists, and removes list structure. **Restyle headings** affects every paragraph using that style and can be undone.

Use **Insert table** in Tables, or choose **Insert table** from **Table operations**. Place the caret in a cell to add rows or columns, select a row or column, or move between cells. Select cells to merge, split, clear, format, or delete their rows/columns. **Equal column widths** makes every column 120 points wide. **Cell appearance** changes padding, vertical alignment, shading, and borders.

**Annotations** adds comments and named bookmarks at the text selection. Browse annotations to return to their text or delete them. An annotation whose text was deleted remains available as an orphaned annotation.

Turn on **Track changes** before editing. **Review** opens selectable Original and Final views and lets you accept or reject a change group. The confirmation names how many related groups will also resolve. Use Undo in an editing pane to reverse a resolution.

**Open RTF** imports an RTF file. **Save RTF** saves the current document and opens the system share interface. **Copy RTF** copies its RTF source as clipboard text; the ordinary Copy command copies selected content for pasting into an editor. Comments, styles, tables, bookmarks, and pending change groups are retained by the library's RTF format. Other RTF applications can support a smaller set of features. Import notes identify approximations in an opened document.

## Code editor

The sample includes syntax colors, completion suggestions, word information, snippets, folding indicators, hints, and code actions. Use **Complete** or Ctrl/Command+Space for suggestions. **Insert snippet** inserts linked fields; **Next field** or Tab advances between them.

Open **Tools** to indent, outdent, toggle comments, fold selected lines, change tab width, and control wrapping or read-only mode. Indentation and comment commands apply to all selections. **Caret below** adds a caret on the next visual row. **Select occurrences** selects all exact matches of the selected text, or the Find term when the selection is empty. Type to replace them together, then use **Single caret** to keep only the primary selection. Alt+drag creates a rectangular selection on pointer devices.

**Go to line** uses one-based line numbers. **Line endings** sets the terminator inserted by Enter without rewriting existing text. Its Convert commands explicitly change the document's terminators in one undoable edit. The status bar shows the insertion policy, caret location, and selection count.

Use **Find** for literal search and replacement. The second pane shares the text while retaining independent wrapping, folding, and selection. **Theme** switches the app's light and dark appearance.

**Virtual carets** moves each selection to one tab width beyond its line ending. Type or compose text there to insert padding and text together; Undo restores the virtual carets.
