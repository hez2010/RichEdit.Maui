---
title: Migrate to 0.5
description: Upgrade applications to 0.5.0-preview.3.
---

# Migrate to 0.5.0-preview.3

This is a breaking release. Upgrade the MAUI packages together and restore their `TextEdit.Core` and `RichEdit.Core` dependencies. The libraries target .NET 10.

The rich control now takes a structured `RichEdit.Core.RichDocument`. Source editing uses `CodeDocument` over the separate text core. Both controls share native infrastructure; CodeEditor does not wrap the public RichEditor. A document can serve multiple views on one dispatcher.

## API replacements

| Existing application code | 0.5 API |
| --- | --- |
| `RichTextDocument.FromPlainText(text)` assigned to RichEditor | `RichDocument.FromPlainText(text)` |
| `RichTextDocument.FromRtf(rtf)`, `Document.RtfText` | `RtfCodec.ReadAsync` / `WriteAsync` over streams |
| Flat rich `RichTextRange` for every operation | `RichTextPoint` and typed `RichSelection`; use snapshot `ProjectText` only as an explicit mapping |
| Mutable `editor.Selection` formatting facade | `editor.ApplyCharacterFormat`, toggle methods, `editor.Session`, and `editor.Edit` |
| `RichTextSelectionState` | Source `TextSelectionState` or `TextSelectionSet`; rich `RichTextSelection` or `RichTextSelectionSet` |
| Source `RichTextRange` | `TextEdit.Core.TextSpan` |
| Source `RichTextDocumentRevision` | `TextEdit.Core.DocumentRevision` |
| Rich formatting records with MAUI colors/point doubles | `RichCharacterStyle`, `RichParagraphStyle`, RGB `RichColor`, twip `RichLength`, half-point `RichFontSize` |
| Null/default as a general formatting reset | Explicit `StyleValue<T>.Inherit`, `Initial`, or value, as appropriate |
| `CreateSavePoint` plus a separately captured snapshot | One `CaptureSave()` result, acknowledged by `MarkSaved(capture)` |
| `MergeWithPrevious` | Session-controlled typing coalescing or an explicit `BeginUndoGroup` |
| Authored `PreserveHistory` formatting | Ordinary authored transaction, or a transient decoration layer |
| Rich `Decorations` / `Adornments` with flat offsets | `editor.Presentation` producers with typed rich ranges/anchors |
| Source `Decorations` / `Adornments` | Retained source presentation concepts, now using text-core coordinates and generation-checked requests |
| Generic persisted metadata | Supported document semantics such as comments/bookmarks; application metadata remains application-owned |

Use `RichDocument` as the document shared by rich-editor controls.

## Load existing RTF with diagnostics

```csharp
using RichEdit.Core;
using RichEdit.Core.Rtf;

var result = await RtfCodec.ReadAsync(inputStream);
if (!result.Diagnostics.IsEmpty)
{
    // Apply the application's import policy before replacing the open document.
}
editor.Document = new RichDocument(result.Snapshot);
```

There is no format-conversion step to HTML, JSON, or a package file. RTF remains the rich format. Set `RequireLossless = true` when any diagnosed approximation must reject import.

Review [RTF support](rtf-support.md) against your documents. The structured authored profile includes semantic tables and revisions but does not represent every flat formatting/list option: arbitrary numeric font weights, custom list prefix/suffix and marker fonts/picture bullets, custom metadata, and paragraph min/max-height pairs need an application policy. Do not silently translate an unsupported feature and then mark the file as losslessly saved. The core uses bold/normal, standard list counters, and explicit line-spacing modes.

Standard Word tracked-change controls are not converted into the retained revision model. The codec's own revision metadata round-trips supported pending groups, while independent RTF readers receive the current body.

## Convert a flat rich selection

Obtain the mapping from the exact rich snapshot being edited:

```csharp
using RichEdit.Core;

var snapshot = editor.Document.CurrentSnapshot;
var projection = snapshot.ProjectText();
var start = projection.GetPoint(startOffset);
var end = projection.GetPoint(endOffset);
if (start is not null && end is not null)
{
    editor.Selection = new RichEdit.Core.RichTextSelection(start.Value, end.Value);
    editor.ApplyCharacterFormat(new RichCharacterStyle { Bold = true });
}
```

An offset can address a structural separator with no text point. A cell selection or block command must handle that intent explicitly. Do not force the separator into a neighboring text range. Store long-lived locations as rich IDs/anchors or semantic annotations, not offsets in an obsolete projection.

## Move formatting to transactions

```csharp
editor.Edit(edit =>
{
    edit.SetCharacterFormat(point, length,
        new RichCharacterStyle { Bold = true, Italic = false });
}, new RichEditOptions(Description: "Apply emphasis"));
```

Set-format APIs replace the direct record for the requested range. Preserve other direct properties when needed. Selection-format commands handle selected content or typing state. Compute mixed toolbar values from resolved selected formats instead of binding to the removed mutable selection-format facade.

Table editing uses `RichCellSelection`, `GetTableGrid`, and explicit row/column/merge/split commands. Lists use `RichList`/`RichListItem` and wrap/unwrap/split/join commands.

## Update source extensions

Import `TextEdit.Core` for spans, revisions, tracking, and selection sets. Source line endings now remain exact: remove application code that assumes every newline occupies one UTF-16 unit. `GetLine` provides the terminator length. Normalizing EOLs is an explicit undoable edit.

Use `TryApplyEdits` with the captured revision and optional selection generation for completion/refactoring. Move dependent typing reactions into `InputRules`; a public change callback cannot recursively edit the live document. Batch edit coordinates refer to one captured snapshot.

For async colors/widgets, call `BeginUpdate` before background work and `TryPublish` on return. A revision-only check cannot reject an older result computed with different settings against unchanged text.

## Shared views, composition, and saving

Create separate controls with the same document. Each control owns its session, selection, presentation, and viewport. Undo is global to the document and navigates the invoking view; applications expecting pane-local undo must adjust their UI.

For linked source panes, use `CaptureScrollAnchor` and `RestoreScrollAnchorAsync` instead of copying native pixel offsets. Anchors follow edits and dispose independently of the control. Use `RevealAsync` for explicit reveal alignment, and forward `ViewportChanged.CauseToken` to suppress linked-scroll feedback. Keep `ScrollIntoView` for simple range visibility. Rectangle gestures use visual glyph intervals, so a bidi row can contribute more than one source selection.

During IME, the document snapshot is committed content and the session composition is provisional. Resolve native composition before saving or switching documents. Respect `CompositionActive` rejection instead of bypassing the session.

```csharp
if (!editor.TryCompleteComposition()) return;
var document = editor.Document;
var capture = document.CaptureSave();
await RtfCodec.WriteAsync(capture.Snapshot, outputStream);
// Flush and atomically replace the file before acknowledging.
document.MarkSaved(capture);
```

## Validation checklist

- Compile all document, selection, formatting, and extension call sites.
- Import representative RTF with diagnostics and compare semantic re-export/reload.
- Exercise multiple views, undo, save during editing, and document replacement.
- Exercise native composition, clipboard, table navigation, and accessibility on target devices.
- Check responsiveness with your application's file sizes, hints, and longest lines.
