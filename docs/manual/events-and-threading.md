---
title: Events and threading
description: Dispatch, immutable reads, and coherent change publication.
---

# Events and threading

## Thread ownership

Live mutation is serialized by the document owner. All attached views of a shared document use the same dispatcher. Construct detached immutable snapshots on a worker, then adopt them on the UI thread. Immutable snapshots support independent background reads.

## Committed changes

Change callbacks observe the committed document and updated view selections. One observer failure does not prevent other sessions from receiving committed state; an aggregate exception reports observer failures after publication. An exception from an observer does not mean the edit rolled back.

Public change callbacks cannot recursively mutate the same document. Schedule a separate transaction through the dispatcher. Use transactional input rules for dependent source-input changes that must publish atomically.

`RichDocumentChangedEventArgs` supplies `Before`, `After`, `Map`, and edit metadata. `RichAddressMap` maps rich points through text replacement, paragraph split/join, and structural removal. A deleted paragraph can map to null.

`TextDocumentChangedEventArgs` supplies immutable before/after snapshots, recorded text changes, `PositionMap`, and metadata.

A `DocumentRevision` includes document identity as well as version. Never compare version numbers from separate documents as a freshness check.

## View and state notifications

| Notification | Use |
| --- | --- |
| `ContentChanged` | Refresh UI that depends on a committed document change, including edits from another view. |
| `SelectionChanged` | Refresh UI for the view's current selection. |
| `CodeEditor.SelectionsChanged` | Observe primary and secondary source selections. |
| `CompositionChanged` | Observe provisional native input without treating it as a committed edit. |
| `DocumentChanged` | Rebind application state after the control attaches a different document. |
| `RichDocument.StateChanged` | Refresh dirty-state and history properties, including after save acknowledgement. |
| `CodeDocument.PropertyChanged` | Observe source-document dirty-state and history properties through `INotifyPropertyChanged`. |

Replacing a document rebinds the control's session. Application subscriptions to the previous document must be detached separately. A save acknowledgement can change dirty state without changing text, so content notifications alone are insufficient for a file's modified indicator.
