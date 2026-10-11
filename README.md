# RichEdit.Maui

[![RichEdit.Maui NuGet version](https://img.shields.io/nuget/vpre/RichEdit.Maui?label=RichEdit.Maui)](https://www.nuget.org/packages/RichEdit.Maui)
[![CodeEdit.Maui NuGet version](https://img.shields.io/nuget/vpre/CodeEdit.Maui?label=CodeEdit.Maui)](https://www.nuget.org/packages/CodeEdit.Maui)

Native rich-text and source editors for .NET MAUI, with separate document models, shared-view history, multiple carets, and native input. The current package version is **0.5.0-preview.1**.

- **RichEdit.Core**: structured paragraphs, styles, lists, tables, images, fields, annotations, retained revision groups, and RTF persistence.
- **TextEdit.Core**: persistent source storage, exact line endings, immutable snapshots, tracking, transactions, composition, and history without a MAUI dependency.
- **RichEdit.Maui**: native rich editing, typed selections, native Original/Final review views, and typed transient presentation.
- **CodeEdit.Maui**: plain-source editing, multiple carets, folding, input rules, syntax decorations, lazy widgets, and line numbers.

RTF is the only rich-document persistence and interchange format. Source text and ordinary plain-text clipboard fallback remain text operations. The controls use no WebViews.

[Manual and API reference](https://hez2010.github.io/RichEdit.Maui/) · [Migration to 0.5](docs/manual/migration-0.5.md) · [RTF support](RTF_SUPPORT.md)

## Requirements and installation

The libraries target .NET 10. Supported minimums are Android API 26, iOS 15, Mac Catalyst 15, and Windows 10.0.17763. Native text controls are Android editable text, UIKit UITextView, and WinUI RichEditBox.

```sh
dotnet add package RichEdit.Maui --version 0.5.0-preview.1
dotnet add package CodeEdit.Maui --version 0.5.0-preview.1
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

Complete composition before capturing a save. Streams and atomic destination replacement belong to the application. Read [history and saving](docs/manual/history-and-saving.md) for concurrent edits and [RTF support](RTF_SUPPORT.md) for resource limits and fidelity.

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

Native controls own ordinary editing, primary selection, IME, shaping, scrolling, and accessibility. Small overlays/extensions cover secondary carets, gutters, widget placement, unsupported diagnostic strokes, and Android table edges.

This preview supports continuous body editing and Original/Final review. It does not provide pagination/printing, additional stories, inline redline markup, or Word-native tracked-change interchange. Native large-document and dense-presentation latency can exceed the proposed engineering targets; Android table containers are eager, and rectangular source gestures cover visible rows. See [platform behavior](docs/manual/platforms.md) for exact boundaries and accessibility validation limits.

## Development

```sh
dotnet build RichEdit.Maui.slnx
dotnet run --project TextEdit.Core.Tests -- -parallelMode none -nocolor
dotnet run --project RichEdit.Core.Tests -- -parallelMode none -nocolor
```

Build the Windows native test executable with `Platform=x64` and `WindowsPackageType=None`, then run it with `-parallelMode none`. The sample hosts Android and Apple native contract tests. Opt-in `performance` and `large` source workloads write machine-readable measurements to the application's cache; they distinguish input, dispatcher checkpoints, and frame opportunities.

Build the Windows CodeEdit.Maui target in Release with `-p:CopyLocalLockFileAssemblies=true`, then run `dotnet tool restore` and `dotnet docfx docs/docfx.json --warningsAsErrors`. The API reference includes both document cores and both MAUI packages.

[MIT license](LICENSE.txt).
