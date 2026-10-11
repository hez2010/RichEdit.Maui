---
title: Events and threading
description: Dispatch, immutable reads, and coherent change publication.
---

# Events and threading

Live mutation is serialized by the document owner. All attached views of a shared document use the same dispatcher. Construct detached immutable snapshots on a worker, then adopt them on the UI thread. Immutable snapshots support independent background reads.

A commit validates its candidate state, updates the document and all managed view selections, synchronizes native projections, and then publishes observers. One observer failure does not prevent other sessions from receiving committed state; an aggregate exception reports observer failures after publication. An exception from an observer does not mean the edit rolled back.

Public change callbacks cannot recursively mutate the same document. Schedule a separate transaction through the dispatcher. Use transactional input rules for dependent source-input changes that must publish atomically.

`RichDocumentChangedEventArgs` supplies `Before`, `After`, `Map`, and edit metadata. `RichAddressMap` maps rich points through text replacement, paragraph split/join, and structural removal. A deleted paragraph can map to null.

`TextDocumentChangedEventArgs` supplies immutable before/after snapshots, recorded text changes, `PositionMap`, and metadata. Source edits do not rediscover changes by comparing whole managed documents.

`RichEditor.ContentChanged`, `SelectionChanged`, `CompositionChanged`, and `DocumentChanged` serve different lifetimes. Replacing a document rebinds the control's session. Provisional IME notifications are not committed-content changes.

A `DocumentRevision` includes document identity as well as version. Never compare version numbers from separate documents as a freshness check.
