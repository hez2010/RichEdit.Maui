---
title: Control configuration
description: Configure view appearance, input policies, and editor lifetimes in a MAUI application.
---

# Control configuration

Create and configure controls on the UI thread. Give each editor a finite viewport, normally a star-sized Grid row. The editor provides its own native scrolling; an unconstrained layout can interfere with scrolling and lazy widget realization.

## Rich-editor options

```csharp
using RichEdit.Maui;

var editor = new RichEditor
{
    Placeholder = "Write a description…",
    FontSize = 18,
    AcceptsTab = false,
    IsSpellCheckEnabled = true,
    IsTextPredictionEnabled = true,
    MaxLength = 20_000,
};
```

| Property | Application behavior |
| --- | --- |
| `IsReadOnly` | Disables authored edits through this view. Selection and copying remain available. Other views can still edit the shared document. |
| `MaxLength` | Limits user input by UTF-16 length in the rich text projection. `-1` means unlimited. It is not a byte limit on an RTF file or its images. |
| `Placeholder`, `PlaceholderColor` | Configure the empty editor's prompt. A null placeholder color uses the native default. |
| `FontFamily`, `FontSize`, `TextColor` | Supply view fallbacks for unresolved authored values. They do not change saved formatting. |
| `IsSpellCheckEnabled`, `IsTextPredictionEnabled` | Request native proofing and prediction; null uses platform defaults. Behavior depends on the native keyboard and platform. |
| `Keyboard` | Selects the MAUI native keyboard configuration. |
| `AcceptsTab` | Allows native Tab input. Application shortcuts use the control's `KeyBindings` collection. |
| `AutoSize` | Selects native automatic height sizing with `EditorAutoSizeOption`. Use bounded layout for a scrolling document editor. |

Read-only state and input length are view policies. Calling `RichDocument.Edit` or `CodeDocument.Edit` directly is an application operation, independent of those view policies. `RichEditor.Edit` checks session read-only state, composition, and optional revision guards; application-authored rich transactions must enforce any additional length policy themselves. `CodeEditor.TryApplyEdits` also checks the view's source length limit. Lowering `MaxLength` does not truncate existing content.

## Appearance and themes

Set foreground and background together so unresolved text stays readable in both themes:

```xml
<rich:RichEditor
    TextColor="{AppThemeBinding Light=Black, Dark=White}"
    BackgroundColor="{AppThemeBinding Light=White, Dark=#192933}" />
```

Here `rich` is `clr-namespace:RichEdit.Maui;assembly=RichEdit.Maui`. Explicit document colors retain their authored values. `ApplyCharacterFormat` saves font and color choices into the document; presentation decorations provide temporary highlights.

`CodeEditor` supplies `FontFamily`, `FontSize`, `TextColor`, `BackgroundColor`, `Placeholder`, `IsReadOnly`, and `MaxLength`. Its `FontSize` is a positive display-unit value, while the rich editor also accepts null to use the native fallback. Rich-only properties such as `AcceptsTab` and `AutoSize` are not CodeEditor configuration APIs.

## Document and feature lifetimes

Keep a document for the lifetime of an open file. Attach it to multiple controls for split views; each control owns its selection and presentation. A view's `IsReadOnly` flag is useful for a live preview of the same content. `RichReviewView` renders Original or Final projections of pending tracked revisions.

Before replacing a document or saving, call `TryCompleteComposition()` and defer the operation if it returns false. Document replacement rebinds the view session and clears document-bound presentation. Cancel pending analysis and start new work against the replacement document's revision.

Keep decoration layers, widget sources, tracking handles, and input-rule registrations alive while their features are active. Dispose them on the owner thread when those features end, and unsubscribe application event handlers. A `using var` example represents that lifetime; creating and disposing a producer inside a short setup method immediately removes the feature.
