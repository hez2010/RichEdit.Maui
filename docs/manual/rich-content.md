---
title: Links, fields, images, and fragments
description: Structured content and resource ownership.
---

# Links, fields, images, and fragments

Tables, lists, and inline objects belong to the same document and history as surrounding paragraphs. Cells hold blocks, including nested tables.

## Tables

See [Tables and cell editing](tables.md) for insertion, cell content, merged selections, row/column operations, widths, cell formatting, and native navigation.

## Hyperlinks

`SetHyperlink` assigns or removes a link over a paragraph-local range. For a selection crossing paragraphs, get the bounded ranges from `snapshot.GetSelectedText(selection)` and apply a link to each nonempty range. The application decides what to do when a link is invoked.

```csharp
using RichEdit.Core;
using RichEdit.Maui;

var editor = new RichEditor { Document = RichDocument.FromPlainText("Open the guide") };
var snapshot = editor.Document.CurrentSnapshot;
var paragraph = snapshot.Get<RichStory>(snapshot.Body).Blocks[0];
var point = new RichTextPoint(snapshot.Body, paragraph, 0);
editor.Edit(edit => edit.SetHyperlink(point, 14,
    new RichHyperlink("https://example.com/guide", "Developer guide")));
```

Pass `null` as the link to unlink the range while retaining its text and formatting. `LinkInvoked` supplies the typed `Range` and `Link` (`Target` and optional `ToolTip`), plus `Handled`. Route targets through your application's navigation policy and set `Handled` when your handler consumes the action. Link creation does not require the library to fetch the target.

## Fields and soft breaks

Use `SetHyperlink` for a text range, `InsertField` for an instruction/result pair, and `SetFieldResult` to update a retained field result. A field is an atomic authored object; the application supplies field evaluation. `LinkInvoked` and `ObjectInvoked` support application handling.

```csharp
RichNodeId field = default;
editor.Edit(edit =>
    field = edit.InsertField(point, "DATE", "28 October 2026"));

// Refresh the displayed result in a later transaction:
editor.Edit(edit => edit.SetFieldResult(field, "29 October 2026"));
```

The instruction is stored as data and is never executed. A field occupies one rich model offset, even when its display result has several characters. An image or soft break also occupies one offset. Do not calculate rich offsets from displayed field text.

`InsertSoftBreak(point)` inserts a line break within a paragraph. `SplitParagraph(point)` creates a new paragraph; `JoinParagraphs(first, second)` joins adjacent paragraphs. These operations have different effects on paragraph styles, lists, and annotations. Ordinary replacement containing paragraph terminators uses the selection replacement APIs rather than inserting terminators into a text run.

## Images

Add an image resource with `AddImageAsset`, then reference it with `InsertImage`. The object has explicit dimensions and alternative text. Supported retained image kinds are PNG, JPEG, DIB, EMF, and WMF; native display support varies. Resource identity and bytes remain document-owned.

With encoded PNG bytes in `pngBytes` and a current insertion `point`, add the asset and image together:

```csharp
editor.Edit(edit =>
{
    var asset = edit.AddImageAsset(RichImageKind.Png, pngBytes);
    edit.InsertImage(point, asset,
        RichLength.FromPoints(144), RichLength.FromPoints(96),
        alternativeText: "Architecture overview");
}, new RichEditOptions(Description: "Insert image"));
```

Dimensions are document lengths, independent of encoded pixel dimensions. `AddImageAsset` copies bytes into the document; several images can reference the same asset. `RemoveImageAsset` cannot commit while a current image still references that resource. `ObjectInvoked` exposes the immutable inline node for application actions such as an image inspector.

## Fragments

`RichDocumentFragment.FromSelection(snapshot, selection)` freezes selected rich content and its resources. `ReplaceSelection(fragment)` inserts a fragment with identity/resource remapping. A fragment is serialized through `RtfCodec` using its `Content` snapshot. There is no separate fragment file format.

```csharp
var fragment = RichDocumentFragment.FromSelection(
    editor.Document.CurrentSnapshot, editor.Selection);
var destination = new RichEditor();
destination.ReplaceSelection(fragment);
```

Use fragments to transfer authored structure between documents without carrying source node IDs into the destination. For insertion at a block boundary, `edit.InsertFragment(parent, index, fragment)` returns the inserted block IDs. Clipboard commands perform their own capture and validation; see [Input and clipboard](input.md).

Application widgets are transient view objects and do not enter the rich schema, clipboard RTF, or undo history. Use [presentation widgets](adornments.md) for completion actions, hints, or review controls.
