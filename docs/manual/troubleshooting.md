---
title: Troubleshooting
description: Diagnose identity, composition, persistence, and native layout issues.
---

# Troubleshooting

| Symptom | Check |
| --- | --- |
| A checked edit returns false or a rejection | Captured document revision, read-only session, active composition, and optional selection generation |
| A decoration/widget result is rejected without a text change | A newer producer request, clear, disposal, or document replacement invalidated its generation |
| A rich range addresses the wrong content | Use typed points or the exact snapshot-bound projection; never reuse flattened offsets across structural edits |
| A save stays modified | The saved capture is older than the current content; acknowledge only the successfully written capture |
| Undo changes another pane's content | Shared views intentionally have one document-owned history |
| A native format differs after RTF load | Review import diagnostics and native rendering fidelity separately |
| Geometry is null | Capture after layout, check composition state, and use a fresh capture for the same view |
| A widget is not created | Its anchor is outside the native viewport, its paragraph was removed, or its estimate/placement is invalid |
| Large documents stall | Measure native readback, layout, and reservation publication separately from core storage |

For RTF import, inspect every `RtfDiagnostic`. Set `RequireLossless` when approximation is unacceptable. Do not silently discard diagnostics to make an unsupported file appear lossless.

For rich selections, inspect the selection's concrete type and validate node IDs against its document snapshot. Model IDs are document-local and can change on import. For source text, preserve exact CRLF/LF/CR coordinates and distinguish logical columns from visual positions.

Do not mutate a document from its public change callback. Dispatch a separate edit or use a source input rule when the dependent operation must be atomic with typing. Complete or cancel native composition before operations that require committed state.
