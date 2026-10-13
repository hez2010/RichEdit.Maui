---
title: RTF support
description: RTF fidelity, feature support, and limitations on Windows, iOS, Mac Catalyst, and Android.
---

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
| Lists | Semantic items, nested blocks, bullets, start counters, decimal/Roman/alphabetic numbering; imported sections retain continued numbering across intervening paragraphs | Up to nine RTF levels and 2,000 overrides |
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

## Platform support

The following matrices describe rich editing on Windows 10.0.17763+, iOS 15+, Mac Catalyst 15+, and Android API 26+. **Supported** means the editor displays the feature. **Approximate** means its appearance is simplified or depends on the native text engine. **Retained** means the value survives in the document and RTF but has no reliable visual effect on that platform. These display limits do not remove supported authored values when saving.

### Text and character formatting

| Feature | Windows | iOS | Mac Catalyst | Android |
| --- | --- | --- | --- | --- |
| Unicode, paragraph breaks, soft line breaks, and tabs | Supported | Supported | Supported | Supported |
| Font family, size, bold, and italic | Supported | Supported | Supported | Supported |
| Foreground color and solid highlight | Supported | Supported | Supported | Supported |
| Single underline | Supported | Supported | Supported | Supported |
| Words-only, double, dotted, dashed, dash-dot, dash-dot-dot, and thick underline | Supported | Supported | Supported | Approximate: single underline |
| Wave, double-wave, heavy-wave, and long-dash underline | Supported | Approximate: single underline | Approximate: single underline | Approximate: single underline |
| Underline color | Approximate: text color may be used | Supported | Supported | Approximate: text color is used |
| Single strike-through | Supported | Supported | Supported | Supported |
| Double strike-through | Approximate: single strike | Supported | Supported | Approximate: single strike |
| Superscript, subscript, and manual baseline offset | Supported | Supported | Supported | Supported; baseline rounding can differ |
| Character spacing | Supported | Supported | Supported | Supported; font metrics can differ |
| Horizontal scale | Approximate: nearest font stretch | Approximate: font expansion | Approximate: font expansion | Supported |
| Small caps | Supported, subject to font support | Supported, subject to font support | Supported, subject to font support | Supported, subject to font support |
| All-caps display without changing text | Supported | Supported, subject to font support | Supported, subject to font support | Retained |
| Outline | Supported | Supported | Supported | Supported; stroke appearance can differ |
| Shadow | Retained | Supported | Supported | Supported; shadow appearance can differ |
| Hidden text | Supported | Approximate: invisible glyphs can retain layout space | Approximate: invisible glyphs can retain layout space | Approximate: invisible glyphs retain layout space |
| Character language | Supported; proofing depends on installed languages | Retained; input-language behavior follows the device | Retained; input-language behavior follows the device | Supported; proofing depends on installed languages |
| Explicit character LTR/RTL direction | Retained; paragraph direction and Unicode bidi text determine display | Supported | Supported | Retained; paragraph direction and Unicode bidi text determine display |
| Character shading patterns | Approximate; native appearance can vary | Approximate; native appearance can vary | Approximate; native appearance can vary | Retained; a separate solid highlight is displayed |

Missing fonts use the platform fallback. Font metrics, device text scaling, and glyph availability can change wrapping and appearance. Explicit RTF foreground and highlight colors remain authored colors when the app theme changes. Bind the view's fallback text and background colors together for theme-aware unformatted text; see [formatting](formatting.md).

### Paragraph formatting

| Feature | Windows | iOS | Mac Catalyst | Android |
| --- | --- | --- | --- | --- |
| Left, center, and right alignment | Supported | Supported | Supported | Supported |
| Justification | Supported per paragraph | Supported per paragraph | Supported per paragraph | Supported when all paragraphs in a contiguous text region are justified; otherwise left-aligned |
| Distributed alignment | Approximate: justification | Approximate: justification | Approximate: justification | Approximate: justification where available |
| Paragraph LTR/RTL direction | Supported | Supported | Supported | Forced direction requires agreement within a contiguous text region; mixed regions use first-strong direction |
| Leading, first-line, and hanging indents | Supported | Supported | Supported | Supported |
| Trailing indent | Supported | Supported | Supported | Retained |
| Before/after spacing | Supported | Supported | Supported | Supported |
| Single, 1.5, double, exact, minimum, and multiple line spacing | Supported | Supported | Supported | Supported; line-height rounding can differ |
| Left tab stops | Supported | Supported | Supported | Supported |
| Center and right tab stops | Supported | Supported | Supported | Retained |
| Decimal tab stops | Supported | Approximate: natural alignment | Approximate: natural alignment | Retained |
| Tab leaders | Supported | Retained | Retained | Retained |
| Hyphenation preference | Retained | Supported, subject to language and native layout | Supported, subject to language and native layout | Enabled when all paragraphs in a contiguous text region request it |
| Solid paragraph background | Approximate; native appearance can vary | Approximate; native appearance can vary | Approximate; native appearance can vary | Supported |
| Paragraph shading patterns | Approximate; native appearance can vary | Approximate; native appearance can vary | Approximate; native appearance can vary | Retained |
| Uniform paragraph borders | Approximate; native appearance can vary | Approximate; native appearance can vary | Approximate; native appearance can vary | Single, double, dotted, and dashed borders are displayed |
| Heading levels, keep-with-next, keep-together, and widow settings | Retained; no paginated layout | Retained; no paginated layout | Retained; no paginated layout | Retained; no paginated layout |
| Named paragraph and character styles | Supported through resolved formatting | Supported through resolved formatting | Supported through resolved formatting | Supported through resolved formatting |

On Android, a contiguous text region is a run of paragraphs between structural boundaries such as tables and lists. Separate list items and table cells can therefore have different justification, direction, and hyphenation settings, while mixed paragraphs within one region are subject to the limits above.

### Lists, tables, and inline content

| Feature | Windows | iOS | Mac Catalyst | Android |
| --- | --- | --- | --- | --- |
| Bullets; decimal, Roman, and alphabetic numbering | Supported | Supported | Supported | Supported |
| List start numbers, nested items, and continued numbering | Supported | Supported | Supported | Supported |
| Enter to add an item; Backspace to join items or leave an empty item | Supported | Supported | Supported | Supported |
| Tables, nested tables, merged cells, row/column edits | Supported | Supported | Supported | Supported; large tables can reduce responsiveness |
| Cell borders, padding, shading, and vertical alignment | Supported; padding can differ for a cell containing only a nested table | Supported; dimensions and shading can differ from Windows | Supported; dimensions and shading can differ from Windows | Supported; shading patterns can be approximated |
| Hyperlink text and target | Supported; application handles activation | Supported; application handles activation | Supported; application handles activation | Supported; application handles activation |
| Hyperlink tooltip | Retained; tooltip display is application-defined | Retained; tooltip display is application-defined | Retained; tooltip display is application-defined | Retained; tooltip display is application-defined |
| Fields | Stored result displayed; application evaluates instructions | Stored result displayed; application evaluates instructions | Stored result displayed; application evaluates instructions | Stored result displayed; application evaluates instructions |
| Inline PNG/JPEG and explicit image dimensions | Supported | Supported | Supported | Supported |
| Inline DIB | Native-dependent display | May display an object placeholder | May display an object placeholder | May display an object placeholder |
| Inline EMF/WMF | Supported by native RTF display | Object placeholder; bytes retained | Object placeholder; bytes retained | Object placeholder; bytes retained |
| Image alternative text | Retained and exposed where supported | Retained and exposed where supported | Retained and exposed where supported | Native image description on API 30+; retained on API 26–29 |
| Bookmarks and comments | Retained; application supplies annotation UI | Retained; application supplies annotation UI | Retained; application supplies annotation UI | Retained; application supplies annotation UI |
| Tracked revision groups and Original/Final review | Supported; no inline redline display | Supported; no inline redline display | Supported; no inline redline display | Supported; no inline redline display |
| RTF clipboard | RTF and plain-text fallback | RTF and plain-text fallback | RTF and plain-text fallback | RTF with cooperating apps; plain-text fallback for other apps |

An unsupported or undecodable image can display as an object placeholder without losing its retained bytes. Custom list marker templates, marker fonts, and picture bullets are outside the supported authored profile on every platform.

### Editing limits by platform

- **Windows:** extremely large documents can show blank text at distant scroll positions. Such document sizes are outside the supported editing profile. Nested-table cell padding can differ from the authored value.
- **iOS:** text, list, and table layout follows available fonts and device text scaling. Some advanced underline styles, tab leaders, image types, and shading are retained or simplified as listed above.
- **Mac Catalyst:** the formatting profile matches iOS. Font-size and line-spacing edits can reposition the viewport; native table and font metrics can differ from iOS devices.
- **Android:** table size and the number of list items or embedded resources can affect responsiveness. Paragraph properties shared by a contiguous text region have the limits listed above. Use app-theme bindings for fallback text and background colors.

All platforms provide continuous body editing. Pagination, printing, additional document stories, inline redline markup, and Word-native tracked-change interchange are not provided. See [platform behavior](platforms.md) for general input and large-document guidance.

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
