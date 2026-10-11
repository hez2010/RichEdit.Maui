# RTF support

RTF is the sole persistence and interchange format for `RichEdit.Core.RichDocument` and rich fragments. The codec stores supported authored semantics independently of native presentation. Source text APIs and plain-text clipboard fallback are separate text operations.

`RtfCodec.ReadAsync` returns a detached validated snapshot plus fidelity diagnostics. `RequireLossless` rejects input when unsupported content would be omitted or approximated. `Assess` examines the same frozen state used for export; `WriteAsync` refuses unsupported loss before writing bytes. Export does not offer a silent lossy-save mode.

## Authored profile

| Feature | Model and RTF codec | Native rendering/editing notes |
| --- | --- | --- |
| Unicode paragraphs and soft breaks | Semantic round trip | Native paragraph/line conventions are mapped to rich points |
| Font family and half-point size | Retained with inheritance metadata | Native fonts and metrics determine appearance |
| Bold and italic | Retained | Native attributes |
| Underline variants and color | Retained | Displayed style and color can vary by platform |
| Single/double strike-through | Retained | Native rendering can approximate double strike |
| RGB foreground and highlight | Retained | View fallback colors are not authored |
| Shading | Character and paragraph values retained independently of highlight | Native patterns can be approximated |
| Superscript, subscript, manual baseline | Retained | Native metrics/rounding vary |
| Character spacing and horizontal scale | Retained | Displayed metrics can vary by platform |
| Small caps, all caps, outline, shadow, hidden text | Retained | Some backends retain or approximate these properties |
| Language and direction | Retained | Native shaping and proofing availability vary |
| Paragraph alignment and indents | Retained | Native paragraph layout |
| Before/after spacing and line spacing | Retained; automatic, minimum, exact, and multiple modes | Engine-specific line-height behavior |
| Tab stops and leaders | Retained | Native stop/leader support and decimal conventions vary |
| Heading, keep, widow, hyphenation settings | Retained | Continuous editing is not a paginated layout engine |
| Paragraph borders | Uniform style across selected sides retained | Mixed external edge styles produce a diagnostic; native visual fidelity varies |
| Named character/paragraph styles | References, inheritance, next-style, and direct overrides retained | Views resolve the authored cascade |
| Lists | Semantic items, nested blocks, bullets, start counters, decimal/Roman/alphabetic numbering | Up to nine RTF levels and 2,000 overrides |
| Tables | Nested blocks, explicit grid, merged cells, row/column edits retained | Table layout can differ between platforms |
| Cell formatting | Independent borders, padding, shading, vertical alignment retained | Padding around a cell containing only a nested table can differ on Windows |
| Hyperlinks | Target and tooltip retained | Application activation events and native text mapping |
| Fields | Instruction/result retained as an atomic inline | Application owns evaluation; native display uses the result |
| Images | Resource bytes, dimensions, alternative text retained | PNG/JPEG display is broadly available; DIB/EMF/WMF display varies by platform |
| Bookmarks and comments | IDs/relationships, text/author/date, anchors and orphan state retained | Application supplies annotation UI |
| Tracked revision groups | Before/after content, resources, relationships, authorship and dependencies retained | Native Original/Final views; no inline redline mode |
| Undo, view selections, folding, widgets, diagnostics | Transient; excluded | Owned by document history or view sessions, not RTF |

Authored lengths use integer twips and font sizes use half-points. Authored colors are RGB; arbitrary alpha belongs to presentation. Properties distinguish inheritance, explicit values, and schema-initial values.

Arbitrary font weights, custom list marker templates/fonts/pictures, extra document stories, section/page settings, floating objects, and editable equations are outside this authored profile. Unknown imported controls/destinations are diagnosed rather than copied as unsafe opaque data.

## Interoperability

Saving and reopening RTF preserves supported document content and relationships. The saved bytes need not be identical, and document-local IDs can change when importing. Other RTF applications may omit metadata they do not support.

Pending revision groups survive saving and reopening with this library. Other readers may show only the current body. Word tracked-change interchange is not supported; strict import rejects unsupported revision controls.

Rich fragments carry required styles and resources. Insertion remaps identities and resolves conflicting resources. Android exchanges RTF with cooperating applications and uses ordinary text when an external application supplies no supported RTF payload.

Native readers may normalize fonts, line spacing, list labels, table layout, or unsupported image types. Native appearance is not evidence of codec losslessness, and codec retention does not promise pixel-identical rendering.

## Limits and cancellation

| Read option | Default |
| --- | --- |
| MaximumBytes | 512 MiB |
| MaximumDepth | 128 groups |
| MaximumAssetBytes | 64 MiB per picture/binary payload |
| MaximumTextLength | 64 Mi UTF-16 units across content and metadata |
| MaximumNodes | 1,000,000 document and resource records |
| MaximumRevisionWork | 64,000,000 historical graph/dependency entries |

The limits include retained revision content. Invalid references, identities, table grids, styles, or revision relationships fail import. Streams remain caller-owned; canceled output may be partial.

Large imports/exports should run on a worker with cancellation. The host validates diagnostics, chooses the destination, performs atomic replacement, and acknowledges the captured save token only after success.
