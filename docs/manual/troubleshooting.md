---
title: Troubleshooting
description: Diagnose handler registration, document ownership, input, persistence, stale results, and layout problems.
---

# Troubleshooting

| Symptom | Check and fix |
| --- | --- |
| The editor has no registered handler | Call `UseRichEdit()` or `UseCodeEditor()` before building the MAUI app. `UseCodeEditor()` registers both. |
| The editor has little height or does not scroll as expected | Give it a bounded Grid row or explicit height. Avoid measuring it with an unbounded parent ScrollView. |
| Assigning a document fails | A live document can attach to one editor at a time. Create a separate document for another editor. |
| A binding does not update after loading | Raise `PropertyChanged` for the view-model `Document` property when replacing it. Native typing updates the current document in place. |
| Offsets select the wrong text | Use normalized LF text and UTF-16 offsets. Code line/column APIs are one-based. |
| Formatting changes new typing but not nearby text | An empty selection changes typing format. Select the intended text or use an explicit document range. |
| Syntax highlighting marks the document modified | Use decorations instead of authored formats, including `PreserveHistory` formatting. |
| Undo is disabled | Close all undo groups, confirm a changed edit was recorded, and check read-only command state. |
| The document appears saved after a later edit | Capture a save point with the snapshot and mark that point only after its write succeeds. Serialize overlapping saves. |
| Custom metadata disappears after an RTF reload | Application metadata is model-only. Save it alongside the RTF. |
| `TryApplyEdits` returns false | The revision may be stale, the editor read-only or composing, or the result too long for `MaxLength`. |
| Decoration `TrySet` returns false | The revision is stale or belongs to another document. Recompute from a current snapshot. |
| Decoration `TrySet` throws | Ranges must be ordered, nonempty, in bounds, and non-overlapping within one layer. The layer must not be disposed. |
| A fold will not expand | Expand an entry from `CollapsedRanges`. A hidden union returned by `GetCollapsedRange` can combine several entries. |
| Layout queries return null or no bounds | Wait for native layout, recapture after `TextLayout.Changed`, and account for offscreen or folded source. |
| Geometry becomes wrong after scrolling | Layout has its own version. A source revision alone does not keep geometry current. |
| An adornment disappears after an edit | Deleting or replacing its anchor removes it. Undo does not recreate a removed view; regenerate it from feature state. |
| A tracked handle returns null after undo | Invalidated or disposed handles are not revived by undo. Choose a preservation policy before deletion if required. |
| A keyboard handler misses software input | Hardware keys do not cover software keyboards or IME. Observe committed text and composition instead. |
| An edit throws from a change event | Do not mutate the same document recursively from its notifications. Schedule work after notification returns. |
| Formatting looks different on another platform | Check rendering and RTF persistence separately in the [support matrix](../../RTF_SUPPORT.md). |

## Inspecting content

Compare `CurrentSnapshot.Text`, runs, paragraphs, links, fields, and images before and after the operation. This separates a document change from a rendering problem. `RichTextChangeSet` identifies the origin and affected ranges.

For import/export issues, compare the content represented by the original and exported RTF. The codec normalizes RTF, so byte-for-byte differences are expected.

## Background results

Record the captured and current revisions, request generation, and document replacement events. A request can be outdated even when its source revision still matches, and layout can change without a text edit.
