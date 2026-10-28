# RichEdit.Maui

[![RichEdit.Maui NuGet version](https://img.shields.io/nuget/vpre/RichEdit.Maui?label=RichEdit.Maui)](https://www.nuget.org/packages/RichEdit.Maui)
[![CodeEdit.Maui NuGet version](https://img.shields.io/nuget/vpre/CodeEdit.Maui?label=CodeEdit.Maui)](https://www.nuget.org/packages/CodeEdit.Maui)

Native rich-text and source editors for .NET MAUI, with separate document models, shared-view history, multiple carets, and native input.

- **RichEdit.Core**: structured paragraphs, styles, lists, tables, images, fields, annotations, retained revision groups, and RTF persistence.
- **TextEdit.Core**: source documents, exact line endings, immutable snapshots, tracking, transactions, composition, and history without a MAUI dependency.
- **RichEdit.Maui**: native rich editing, typed selections, native Original/Final review views, and typed transient presentation.
- **CodeEdit.Maui**: plain-source editing, multiple carets, folding, input rules, syntax decorations, lazy widgets, and line numbers.

RTF is the only rich-document persistence and interchange format. Source text and ordinary plain-text clipboard fallback remain text operations. The controls use no WebViews.

[Manual and API reference](https://hez2010.github.io/RichEdit.Maui/) · [RTF support](docs/manual/rtf-support.md)

## Requirements and installation

The libraries target .NET 10. Supported minimums are Android API 26, iOS 15, Mac Catalyst 15, and Windows 10.0.17763.

```sh
dotnet add package RichEdit.Maui --version 0.5.0-preview.3
dotnet add package CodeEdit.Maui --version 0.5.0-preview.3
```

```csharp
using RichEdit.Maui;
using CodeEdit.Maui;

builder.UseRichEdit();
builder.UseCodeEditor();
```

## Rich editing

```csharp
using RichEdit.Core;
using RichEdit.Maui;

var document = RichDocument.FromPlainText("Hello, world!");
var editor = new RichEditor { Document = document, FontSize = 18 };
editor.SelectAll();
editor.ApplyCharacterFormat(new RichCharacterStyle { Bold = true });

// Independent view selection, shared authored content and undo history.
var secondView = new RichEditor { Document = document };
secondView.Undo();
```

Rich editing uses stable node identities and paragraph-local `RichTextPoint` addresses. `RichSelection` distinguishes text sets, objects, cells, blocks, and structural boundaries. Tables and their native cell editors share the parent document's history.

Load and save through the structured RTF codec:

```csharp
using RichEdit.Core.Rtf;

var imported = await RtfCodec.ReadAsync(inputStream);
editor.Document = new RichDocument(imported.Snapshot);
// Apply the application's policy to imported.Diagnostics.

var savedDocument = editor.Document;
var capture = savedDocument.CaptureSave();
await RtfCodec.WriteAsync(capture.Snapshot, outputStream);
// After successful flush and destination replacement:
savedDocument.MarkSaved(capture);
```

Complete composition before capturing a save. Streams and atomic destination replacement belong to the application. Read [history and saving](docs/manual/history-and-saving.md) for concurrent edits and [RTF support](docs/manual/rtf-support.md) for resource limits and fidelity.

## Source editing

```csharp
using CodeEdit.Maui;
using TextEdit.Core;

var code = new CodeEditor
{
    Document = new CodeDocument("first\r\nsecond\n"),
    ShowLineNumbers = true,
    WordWrap = false,
    IndentSize = 4,
    TabSize = 4,
};
code.AddCaret(0);
code.AddCaret(7);
code.ReplaceSelections("// ");
```

Source offsets are zero-based UTF-16. CRLF, LF, and CR remain exact. Snapshot line APIs and bounded reads avoid materializing the whole document. Background language work publishes with document-revision and producer-generation checks.

Applications provide language servers, completion, diagnostics, project systems, and debugging. The library supplies atomic input rules, checked edits, native geometry, decorations, folding, and lazy inline/block widgets.

## Presentation and native behavior

Rich presentation is available through `editor.Presentation.CreateDecorationLayer` and `CreateWidgetSource`, using typed rich anchors. Source presentation uses `code.Decorations`, `code.Adornments`, and `code.Folding`. Presentation never enters authored RTF or undo history.

The editors provide native typing, primary selection, input methods, scrolling, and accessibility alongside multiple carets, line numbers, diagnostics, and widgets.

This preview supports continuous body editing and Original/Final review. It does not provide pagination/printing, additional stories, inline redline markup, or Word-native tracked-change interchange. Large files, long lines, many widgets, and large Android tables can reduce responsiveness. See [platform behavior](docs/manual/platforms.md) for supported features and limitations.

[MIT license](LICENSE.txt).
