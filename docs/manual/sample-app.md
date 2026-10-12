---
title: RichEdit Studio sample
description: Explore rich documents and source editing in the sample app.
---

# RichEdit Studio sample

RichEdit Studio has separate rich-text and code-editor tabs. The tools act on the main view. Turn on **Split view** to edit the same document in a second pane with its own selection and scroll position. Each pane has Undo and Redo; both use the document's shared history.

## Rich document

Select text and use **Bold**, **Italic**, or **Underline**. With an empty selection, these commands set the appearance of newly typed text. **Caret below** adds another insertion point.

**Styles & lists** applies Body or Heading to selected paragraphs, changes the reusable Heading style, creates bullet/numbered lists, and removes list structure. **Restyle headings** affects every paragraph using that style and can be undone.

Use **Table** to insert a table. Place the caret in a cell to add rows or columns, select the first row, or move to the next cell. Select cells to merge or split them. **Equal column widths** makes every column 120 points wide.

**Annotations** adds comments and named bookmarks at the text selection. Browse annotations to return to their text or delete them. An annotation whose text was deleted remains available as an orphaned annotation.

Turn on **Track changes** before editing. **Review** opens selectable Original and Final views and lets you accept or reject a change group. The confirmation names how many related groups will also resolve. Use Undo in an editing pane to reverse a resolution.

**Open RTF** imports an RTF file. **Save RTF** saves the current document and opens the system share interface. Comments, styles, tables, bookmarks, and pending change groups are retained by the library's RTF format. Other RTF applications can support a smaller set of features.

## Code editor

The sample includes syntax colors, completion suggestions, word information, snippets, folding indicators, hints, and code actions. Use **Complete** or Ctrl/Command+Space for suggestions. **Insert snippet** inserts linked fields; **Next field** or Tab advances between them.

Open **Tools** to indent, outdent, toggle comments, fold selected lines, change tab width, and control wrapping or read-only mode. Indentation and comment commands apply to all selections. **Caret below** adds a caret on the next visual row. **Select occurrences** selects all exact matches of the selected text, or the Find term when the selection is empty. Type to replace them together, then use **Single caret** to keep only the primary selection. Alt+drag creates a rectangular selection on pointer devices.

**Go to line** uses one-based line numbers. **Line endings** sets the terminator inserted by Enter without rewriting existing text. Its Convert commands explicitly change the document's terminators in one undoable edit. The status bar shows the insertion policy, caret location, and selection count.

Use **Find** for literal search and replacement. The second pane shares the text while retaining independent wrapping, folding, and selection. **Theme** switches the app's light and dark appearance.

**Virtual carets** moves each selection to one tab width beyond its line ending. Type or compose text there to insert padding and text together; Undo restores the virtual carets.
