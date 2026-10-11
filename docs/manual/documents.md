---
title: Documents and snapshots
description: Content ownership, source coordinates, and RTF persistence.
---

# Documents and snapshots

`RichDocument` owns an immutable graph of stories, paragraphs, inline content, lists, tables, resources, styles, annotations, and revisions. The supported editable story is the body. Nodes have stable document-local IDs; structural moves retain their identity. RTF import regenerates IDs while preserving relationships.

`CodeDocument` owns plain source through `TextDocument`. Source CRLF, LF, and CR terminators, including mixed endings, are preserved. `GetLine` returns content and terminator lengths. Offsets and line/column coordinates are zero-based UTF-16; visual X and tab-display width are separate concepts.

## Read and write RTF

RTF is the only rich persistence and interchange format.

```csharp
using RichEdit.Core;
using RichEdit.Core.Rtf;

var imported = await RtfCodec.ReadAsync(inputStream);
var document = new RichDocument(imported.Snapshot);
editor.Document = document;
// Present imported.Diagnostics to the application's import policy.

var capture = document.CaptureSave();
await RtfCodec.WriteAsync(capture.Snapshot, outputStream);
```

Streams belong to the caller. Import validates syntax, references, nesting, text, assets, and retained-revision work. Set `RequireLossless = true` to reject approximated or omitted content. Export assesses fidelity before writing and throws `RtfFidelityException` when the supported model cannot be written losslessly.

Async stream APIs support cancellation but do not promise that CPU work runs off the caller thread. For large files, run codec work on a worker and adopt the completed immutable snapshot on the document dispatcher. A canceled write can leave a partial destination; the host owns atomic file replacement.

## Inspect content

Rich text is an explicit projection:

```csharp
var snapshot = editor.Document.CurrentSnapshot;
var projection = snapshot.ProjectText();
string plain = projection.Text.ToString();
var firstPoint = projection.GetPoint(0);
```

A projection maps paragraph-local addresses to its own offsets and represents structural separators and inline atoms. Pass rich points to rich editing APIs. Projection offsets from a different snapshot are not interchangeable.

Source snapshots support bounded `GetText`, indexed line lookup, chunk enumeration, and streaming text output through `TextSnapshot`. Reading `Text` or calling `ToString()` materializes the entire source. Prefer bounded reads for language tools and viewport work.
