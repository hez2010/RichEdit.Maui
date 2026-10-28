---
title: Documents and snapshots
description: Content ownership, source coordinates, and RTF persistence.
---

# Documents and snapshots

`RichDocument` owns an immutable graph of stories, paragraphs, inline content, lists, tables, resources, styles, annotations, and revisions. The supported editable story is the body. Nodes have stable document-local IDs; structural moves retain their identity. RTF import regenerates IDs while preserving relationships.

`CodeDocument` owns plain source through `TextDocument`. Source CRLF, LF, and CR terminators, including mixed endings, are preserved. `GetLine` returns content and terminator lengths. Offsets and line/column coordinates are zero-based UTF-16; visual X and tab-display width are separate concepts.

## Load RTF

RTF is the only rich persistence and interchange format.

```csharp
using RichEdit.Core;
using RichEdit.Core.Rtf;

var imported = await RtfCodec.ReadAsync(inputStream,
    new RtfReadOptions { RequireLossless = true });
if (!editor.TryCompleteComposition())
    return;
editor.Document = new RichDocument(imported.Snapshot);
```

Streams belong to the caller. Import validates syntax, references, nesting, text, assets, and retained-revision work. This example rejects approximated or omitted content with `RequireLossless = true`. For tolerant import, leave that option false and apply your policy to `imported.Diagnostics` before replacing the open document. The application also decides how to handle unsaved edits in the document being replaced.

Async stream APIs support cancellation but do not promise that CPU work runs off the caller thread. For large files, run codec work on a worker and adopt the completed immutable snapshot on the document dispatcher. `new RichDocument(snapshot)` creates a fresh live document identity with clean saved state and empty undo history.

## Inspect content

Rich text is an explicit projection:

```csharp
var snapshot = editor.Document.CurrentSnapshot;
var projection = snapshot.ProjectText();
string plain = projection.Text.ToString();
var firstPoint = projection.GetPoint(0);
```

A projection maps paragraph-local addresses to its own offsets and represents structural separators and inline atoms. Pass rich points to rich editing APIs. Projection offsets from a different snapshot are not interchangeable.

## Inspect source text

`CodeDocument.CurrentSnapshot` exposes a `CodeDocumentSnapshot`. Its `TextBuffer` is the immutable `TextSnapshot` used for bounded `GetText`, indexed line lookup, chunk enumeration, and streaming output. Reading `snapshot.Text` or calling `snapshot.TextBuffer.ToString()` materializes the entire source. Prefer bounded reads for language tools and viewport work.
