---
title: Events and threading
description: Observe committed changes, handle document replacement, and keep editor integrations on the correct thread.
---

# Events and threading

Controls and attached documents require their owning UI thread, including for history, tracking, and saved-state changes. Access to a detached document must be serialized.

Capture a snapshot for background analysis, then return to the UI thread to apply its results. Native layout queries also run on the UI thread.

## Change notifications

| Event | Use it for |
| --- | --- |
| `Document.Changed` | One committed transaction and its change set |
| `ContentChanged` | Text or rich-content changes observed through the editor |
| `TextChanged` | Logical text changes |
| `DocumentChanged` | A different live document attached to the control |
| `SelectionChanged` | Caret and selection updates |
| `SelectionFormatChanged` | Rich-text toolbar state |
| `EffectiveAppearanceChanged` | UI that depends on resolved rich-editor appearance |
| `CompositionChanged` | Start, update, and end of native marked-text input |
| `TextLayout.Changed` | Geometry that needs recapturing |
| `Commands.Failed` | Operational clipboard failures from command execution |

`SelectionFormatChanged` and `EffectiveAppearanceChanged` are specific to RichEditor. The other events in the table are available on both editors.

## Change sets

```csharp
editor.ContentChanged += (_, args) =>
{
    var change = args.ChangeSet;
    System.Diagnostics.Debug.WriteLine(
        $"{change.Origin}: {change.VersionBefore} -> {change.VersionAfter}");
};
```

The change set contains the origin, ordered changes, and optional tag supplied in the edit options. `IsTextChanged` distinguishes text changes from formatting or metadata changes.

Document, native-view, selection, and history synchronization completes before public content notifications. The content notification sequence is document `Changed`, editor `ContentChanged` and any `TextChanged`, document property changes, then pending selection and selection-format changes.

Editing the same document from a transaction callback or change notification throws. Queue follow-up edits to run after the notification returns.

Exceptions from event subscribers propagate after commit; they do not roll back the edit.

## Document replacement

`DocumentChanged` carries `OldDocument` and `NewDocument`. Use it to cancel requests tied to the old source, release its tracked handles, and move any document event subscriptions.

Replacing the document clears decoration contents, folds, and adornments. Existing decoration layers remain reusable. Tracking handles stay attached to the original document.
