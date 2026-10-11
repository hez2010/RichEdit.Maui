---
title: Input, commands, and clipboard
description: Native input, composition, checked cut, and transactional source rules.
---

# Input, commands, and clipboard

The native control owns typing, marked text, candidate interaction, primary selection, and touch handles. Hardware key bindings route commands; they do not replace the platform input connection.

Android text-service queries return bounded canonical context. Extracted/surrounding windows contain at most 8,192 UTF-16 units and carry their source offset. Oversized selected-text/extracted requests return no payload, and autofill does not publish document values above 65,536 units. These bounds prevent oversized Binder transactions; they do not truncate authored content, saved files, or clipboard commands.

Apple accessibility line queries use indexed source ranges, including exact line endings and provisional composition content. Reading one line does not materialize the complete source or its paragraph metadata. Full accessible-document requests remain proportional to document size.

Windows accessibility peers retain persistent source snapshots for change notifications. Bounded text queries materialize only the requested range and preserve surrogate pairs at the requested limit. Full value strings are produced when a subscribed value-change event or an explicit full-value query requires them.

Source readback compares persistent text slices when a native edit includes unchanged surrounding text. Only the changed replacement is materialized, and unchanged CRLF, CR, and LF endings retain their exact source representation.

Use the control's `Commands`, `KeyBindings`, and `ContextMenuOpening` APIs for application UI. Rich-editor methods include `CopyAsync`, `CutAsync`, `PasteAsync`, `PastePlainTextAsync`, `Undo`, `Redo`, and `SelectAll`. `IsReadOnly` is a session editing policy. Direct document edits are independent of one view's read-only state.

## Composition

Marked text lives in a provisional session snapshot. The shared committed document does not change until native composition completes. A document permits one composition lease; unrelated mutation is rejected with `CompositionActive`. Other panes may still inspect and display committed content.

With multiple carets, native composition previews at the primary caret. A simple committed replacement is replicated to the other carets in one transaction. Reconversion extending outside the primary range leaves secondary-selection mode. Cancellation restores the pre-composition selection set.

Request completion before saving, changing documents, or commands requiring committed input. An unresolved request must remain pending rather than saving text that omits the marked input.

## Source input rules

`CodeEditor.InputRules.Register` installs synchronous transaction-planning callbacks in explicit priority and registration order. A `TextInputContext` exposes the before/input snapshots, selections, and input changes. Its replacements address the input candidate; overlapping rule edits are rejected.

Use rules for paired delimiters, completion commit characters, indentation, and linked placeholders. Rules cannot await or mutate the live document. A completion plus commit character belongs to one plan and one undo unit. Async language analysis supplies cached decisions or later revision-checked edits.

## Clipboard

Rich clipboard exchange is self-contained RTF plus ordinary plain-text fallback. Android uses an RTF transport that cooperating applications can read. Unsupported external rich payloads use available plain text; no HTML codec is provided.

Cut freezes the selection and snapshot, publishes the clipboard, then checks document revision and selection generation before deleting. An asynchronous stale cut cannot delete newly selected or edited content.

In-process multiple-selection metadata can preserve fragment boundaries while clipboard identity matches. Matching fragment/caret counts distribute fragments; otherwise the combined payload is inserted at each selection. External rich interchange remains one RTF payload.
