---
title: API reference
description: Public document cores and native editor APIs.
---

# API reference

| Assembly | Entry points |
| --- | --- |
| TextEdit.Core | <xref:TextEdit.Core.TextDocument>, <xref:TextEdit.Core.TextSnapshot>, <xref:TextEdit.Core.TextEditorSession>, <xref:TextEdit.Core.TextSelectionSet> |
| RichEdit.Core | <xref:RichEdit.Core.RichDocument>, <xref:RichEdit.Core.RichDocumentSnapshot>, <xref:RichEdit.Core.RichEditorSession>, <xref:RichEdit.Core.Rtf.RtfCodec> |
| RichEdit.Maui | <xref:RichEdit.Maui.RichEditor>, <xref:RichEdit.Maui.RichReviewView>, <xref:RichEdit.Maui.RichEditorPresentation>, <xref:RichEdit.Maui.RichEditorLayout> |
| CodeEdit.Maui | <xref:CodeEdit.Maui.CodeEditor>, <xref:CodeEdit.Maui.CodeDocument>, <xref:CodeEdit.Maui.CodeDocumentSnapshot> |

The document cores target ordinary .NET 10 and have no MAUI dependency. Their assemblies ship with RichEdit.Maui; CodeEdit.Maui depends on RichEdit.Maui. Live editor mutations run on the owner dispatcher; immutable snapshots can be inspected on worker threads. Native API signatures use the Windows MAUI target.
