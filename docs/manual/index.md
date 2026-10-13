---
title: User manual
description: Structured rich documents and native source editing.
---

# User manual

RichEdit.Maui and CodeEdit.Maui provide native editing controls over library-owned documents. The document owns authored content, revision identity, saved state, and shared undo history. Each view owns its selection, composition, presentation, and native resources.

| Use case | Control and document |
| --- | --- |
| Formatted paragraphs, lists, tables, images, comments, and tracked changes | `RichEditor` with `RichEdit.Core.RichDocument` |
| Source text, multiple carets, syntax colors, folding, and application language tools | `CodeEditor` with `CodeDocument` |
| Read-only original or final tracked-change preview | `RichReviewView` with a shared `RichDocument` |

Both editable controls use native text engines on Windows, Android, iOS, and Mac Catalyst. No WebView is used. Multiple controls may share a document on one dispatcher, with independent selections and one undo sequence.

Start with [installation](getting-started.md), [documents](documents.md), and [editing](editing.md). See [platform behavior](platforms.md) for rendering and performance boundaries, [RTF support](rtf-support.md) for persistence fidelity and the Windows, iOS, Mac Catalyst, and Android feature matrices, and the [migration guide](migration-0.5.md) for upgrading an application.

Examples use `RichEdit.Core` for rich semantics, `TextEdit.Core` for source coordinates, and the appropriate MAUI control namespace. A rich text projection is a snapshot-bound mapping, not an editable flat document.
