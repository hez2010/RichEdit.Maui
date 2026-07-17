# Portable RTF support matrix

This matrix describes the current `RichEdit.Maui` implementation, not every
capability that exists in an underlying text engine. The portable targets are
iOS 15+, Mac Catalyst 15+, Android 26+, and WinUI 3 on Windows 10 1809+.
`RichTextDocument` and `RtfCodec` are authoritative; native controls render and
edit the closest representation they support.

Legend:

- **Native** — rendered with a native text attribute and read back by the handler.
- **Adapter** — rendered by a small span or handler adapter inside the native editor.
- **Degraded** — rendered with the documented simpler appearance while the canonical
  value remains in the document model.
- **Model** — retained in the document model but has no visual or behavioral effect.
- **Flattened** — converted to simpler text/inline content or discarded on import.
- **Round-trip** — parsed and serialized by the RTF codec.
- **Import only** — parsed into the model, but not emitted in the same RTF form.
- **Model only** — represented by `RichTextDocument`, but not serialized to RTF yet.

Apple and Android attach canonical metadata to attributed ranges/spans, so a
native edit normally retains properties their renderer cannot express. WinUI
readback is authoritative for character and paragraph formatting; properties
that have no TOM representation can therefore be lost after a native edit.
Fields, links, and existing images are model-owned on WinUI and are remapped as
the text changes.

## Text and character formatting

| Feature | RTF codec | iOS | Mac Catalyst | Android 26 | WinUI 3 | Portable behavior |
|---|---|---|---|---|---|---|
| Unicode text, surrogate pairs, escaped braces and backslashes | Round-trip | Native | Native | Native | Native | Writer emits `\u`; reader honors `\uc` fallback counts. |
| ANSI/Mac/PC code pages and font charsets on import | Round-trip | Native | Native | Native | Native | Imported text is normalized to .NET UTF-16. |
| Paragraph breaks | Round-trip | Native | Native | Native | Native | Canonical form is `\n`; RTF uses `\par`. |
| Soft line breaks | Round-trip | Native | Native | Native | Native | Canonical form is U+2028; RTF uses `\line`. |
| Tabs and nonbreaking/special spaces | Round-trip | Native | Native | Native | Native | Tabs remain logical text; formatted stops are listed below. |
| Font family | Round-trip | Native | Native | Native | Native | Missing fonts use the platform fallback. |
| Font size | Round-trip | Native | Native | Native | Native | RTF half-point precision applies. |
| Bold and italic | Round-trip | Native | Native | Native | Native | Fully portable. |
| Numeric font weights other than normal/bold | Degraded | Degraded | Degraded | Degraded | Native | RTF and non-Windows handlers reduce the value to normal/bold. |
| Foreground color | Round-trip | Native | Native | Native | Native | RTF stores opaque 8-bit RGB. |
| Solid text highlight/background | Round-trip | Native | Native | Native | Native | Alpha is not representable in RTF. A transparent color means reset; WinUI is never assigned an alpha-zero highlight because RichEdit ignores highlight alpha. |
| Single underline | Round-trip | Native | Native | Native | Native | Fully portable. |
| Words/double/dotted/dash/dash-dot/dash-dot-dot/thick underline | Round-trip | Native | Native | Degraded | Native | Android shows a single underline. |
| Wave/double-wave/heavy-wave/long-dash underline | Round-trip | Degraded | Degraded | Degraded | Native | Apple and Android show a single underline. |
| Underline color | Round-trip | Native | Native | Degraded | Degraded | Android and WinUI use the text foreground color. |
| Single strikethrough | Round-trip | Native | Native | Native | Native | Fully portable. |
| Double strikethrough | Round-trip | Native | Native | Degraded | Degraded | Android and WinUI show a single strike. |
| Strikethrough color | Model only | Native | Native | Degraded | Degraded | Android and WinUI use the text foreground color. |
| Superscript and subscript | Round-trip | Native | Native | Native | Native | Fully portable. |
| Arbitrary baseline offset | Round-trip | Native | Native | Adapter | Native | Canonical unit is points. |
| Character spacing/tracking | Round-trip | Native | Native | Adapter | Native | Android converts points to an `em` value for `TextPaint`. |
| Horizontal scale/stretch | Round-trip | Degraded | Degraded | Native | Degraded | Apple uses expansion; WinUI selects the nearest `FontStretch`. |
| Small caps | Round-trip | Model | Model | Adapter | Native | Android uses the font's `smcp` glyph feature; fonts without it show normal glyphs. |
| All-caps display effect | Round-trip | Model | Model | Model | Native | Logical text is never rewritten. |
| Outline | Round-trip | Native | Native | Adapter | Native | Android uses stroke paint with a size-relative width. |
| Shadow | Round-trip | Native | Native | Adapter | Model | WinUI has no TOM shadow property. |
| Hidden text | Round-trip | Adapter | Adapter | Adapter | Native | Apple/Android make glyphs transparent while retaining layout. |
| Language/locale tag | Round-trip | Model | Model | Native | Native | Canonical representation is BCP 47; unsupported LCIDs import as unspecified. |
| Explicit character direction | Round-trip | Model | Model | Model | Model | Paragraph direction and Unicode bidi characters remain effective. |
| Kerning preference | Round-trip | Model | Model | Model | Native | It is a font-dependent hint where available. |
| Ligature preference | Model only | Native | Native | Model | Model | Not currently emitted as an RTF control. |
| Character shading pattern and colors | Round-trip | Model | Model | Model | Model | A separate solid highlight still renders on every platform. |
| Named character style | Model only | Model | Model | Model | Model | Explicit formatting is authoritative. |

## Paragraph formatting

| Feature | RTF codec | iOS | Mac Catalyst | Android 26 | WinUI 3 | Portable behavior |
|---|---|---|---|---|---|---|
| Left, center, and right alignment | Round-trip | Native | Native | Native | Native | Fully portable. |
| Whole-document justification | Round-trip | Native | Native | Adapter | Native | Android uses `TextView.JustificationMode.InterWord`, available from API 26. |
| Mixed per-paragraph justification | Round-trip | Native | Native | Degraded | Native | Android preserves the values but renders justified/distributed paragraphs left-aligned unless every paragraph is justified. |
| Distributed alignment | Round-trip | Degraded | Degraded | Degraded | Degraded | Rendered as inter-word justification where justification is available. |
| Paragraph LTR/RTL direction | Round-trip | Native | Native | Model | Native | Android retains the canonical direction metadata. |
| Leading/left indent | Round-trip | Native | Native | Native | Native | Fully portable. |
| First-line and hanging indent | Round-trip | Native | Native | Native | Native | Fully portable. |
| Trailing/right indent | Round-trip | Native | Native | Model | Native | Android retains but does not render it. |
| Space before and after | Round-trip | Native | Native | Model | Native | Android retains but does not render it. |
| Single, 1.5, double, exact, at-least, and multiple line spacing | Round-trip | Native | Native | Adapter | Native | Android uses the API 1 `LineHeightSpan` contract, so all rules work at the API 26 minimum. |
| Minimum and maximum line height | Model only | Native | Native | Adapter | Model | Android clamps the calculated per-line height. |
| Left tab stops | Round-trip | Native | Native | Native | Native | Fully portable. |
| Center and right tab stops | Round-trip | Native | Native | Model | Native | Android retains them without applying a visual stop. |
| Decimal tab stops | Round-trip | Degraded | Degraded | Model | Native | Apple uses its natural tab alignment. |
| Tab leaders | Round-trip | Model | Model | Model | Native | Positions still work where the alignment is supported. |
| Hyphenation preference | Round-trip | Native | Native | Model | Model | No portable hyphenation dictionary or algorithm is imposed. |
| Solid paragraph background | Round-trip | Model | Model | Degraded | Model | Android paints behind glyph ranges, not the entire line box. |
| Paragraph shading patterns | Round-trip | Model | Model | Model | Model | Exact values remain in the canonical model. |
| Paragraph borders | Round-trip | Model | Model | Model | Model | Exact side/style/width/color values remain in the canonical model. |
| Named paragraph style | Model only | Model | Model | Model | Model | Explicit paragraph formatting is authoritative. |

## Lists

List markers are presentation metadata and are never inserted into
`RichTextDocument.Text`.

| Feature | RTF codec | iOS | Mac Catalyst | Android 26 | WinUI 3 | Portable behavior |
|---|---|---|---|---|---|---|
| Basic bullets | Round-trip | Native 16+; Model 15 | Native 16+; Model 15 | Native | Native | iOS/Mac Catalyst 15 retain list metadata but do not draw a native marker. |
| Arabic numbering | Round-trip | Native 16+; Model 15 | Native 16+; Model 15 | Model | Native | Android retains numbering without drawing a marker. |
| Roman and alphabetic numbering | Round-trip | Native 16+; Model 15 | Native 16+; Model 15 | Model | Native | Upper/lower Roman and letter styles are represented. |
| Start-at value | Round-trip | Native 16+; Model 15 | Native 16+; Model 15 | Model | Native | Values are positive integers. |
| Restart flag | Degraded | Model | Model | Model | Degraded | A distinct list ID plus `StartAt` is the portable restart mechanism. |
| Nested level | Degraded | Native 16+; Model 15 | Native 16+; Model 15 | Model | Native | Canonical levels 0–8 are retained; the current RTF writer emits a simple list definition. |
| Custom marker prefix/suffix | Degraded | Native 16+; Model 15 | Native 16+; Model 15 | Model | Degraded | RTF/WinUI use the closest single-character suffix style. |
| Alternate bullet text | Round-trip | Degraded | Degraded | Degraded | Degraded | Canonical bullet text is retained; native renderers use their standard bullet. |
| Picture bullet identifier | Model only | Model | Model | Model | Model | No picture-bullet payload/resolver is implemented yet. |

## Links, fields, and inline pictures

| Feature | RTF codec | iOS | Mac Catalyst | Android 26 | WinUI 3 | Portable behavior |
|---|---|---|---|---|---|---|
| Hyperlink display text and target | Round-trip | Native | Native | Adapter | Native | Target and display range are independent. URL activation remains an application policy. |
| Hyperlink tooltip | Round-trip | Model | Model | Model | Model | Retained and reattached when the native range/target still match. |
| General fields | Round-trip | Model | Model | Model | Model | The result text is displayed and the instruction is retained/remapped; fields are not evaluated. |
| Inline PNG and JPEG bytes | Round-trip | Native | Native | Native | Native | Invalid payloads safely fall back to the U+FFFC object placeholder. |
| Inline EMF and WMF bytes | Round-trip | Model | Model | Model | Native | Apple/Android retain the object placeholder and canonical bytes. |
| PICT, DIB, DDB, and OS/2 metafile import | Import only | Model | Model | Model | Degraded | Parsed into an image object, but the current writer cannot emit those formats. |
| Image width and height | Round-trip | Native | Native | Native | Native | Canonical unit is device-independent points. |
| RTF image scale and crop | Round-trip | Model | Model | Model | Native | Import resolves scale into width/height; crop metadata is retained. |
| Vertical alignment | Model only | Model | Model | Degraded | Model | Android supports baseline versus bottom; center/top fall back to bottom. |
| Alternative text | Model only | Model | Model | Model | Model | Retained for application use but not attached to the native object yet. |
| Rotation | Model only | Model | Model | Model | Model | Retained without a visual transform. |
| Source URI/identifier | Model only | Model | Model | Model | Model | Owned bytes remain the portable persistence mechanism. |

## RTF document structures and interoperability

| Area | Current behavior |
|---|---|
| Font table, color table, default character properties, and default paragraph properties | Parsed and serialized for the supported formatting subset. |
| Group scoping, ignorable destinations, unknown controls, Unicode fallbacks, and binary picture data | Handled by the custom reader. Unknown optional destinations are skipped as required by RTF. |
| Tables and nested tables | Flattened to tab-separated cells and newline-separated rows for continuous editing. |
| Sections, pages, and columns | Section/page breaks become paragraph breaks; page-layout properties are not retained. |
| Headers, footers, footnotes, and endnotes | Not part of the continuous editor model; destination content is skipped. |
| Stylesheets and named styles | Style definitions/names are not persisted; explicit formatting on content is retained when present. |
| Bookmarks, comments, annotations, tracked revisions, and protection data | Not represented and are skipped. |
| OLE objects and file attachments | Not represented; visible fallback/result text may remain, otherwise the object is skipped. |
| Drawing shapes, text boxes, and floating/wrapped pictures | A recognized `\shppict` picture is imported inline; positioning and wrapping are flattened. |
| Equations and math zones | No portable math model; plain field/result text or pictures can be used as a fallback. |
| Document information, user properties, themes, mail merge, forms, and XML/custom data | Not serialized into the portable model. Application metadata can be stored in `RichTextDocument.Metadata`, which is currently model-only. |
| Rich clipboard paste | Supported attributes are read back from each native editor. Unsupported attributes follow the same degradation rules as programmatic documents; full cross-platform RTF clipboard equivalence is not guaranteed. |

## Native API basis

- Apple: [`NSAttributedString` attributes](https://developer.apple.com/documentation/foundation/nsattributedstring/key), [`NSMutableParagraphStyle.textLists`](https://developer.apple.com/documentation/uikit/nsmutableparagraphstyle/textlists), and `NSTextAttachment`.
- Android: [`Spannable`](https://developer.android.com/develop/ui/views/text-and-emoji/spans), [`TextView.setJustificationMode`](https://developer.android.com/reference/android/widget/TextView#setJustificationMode(int)), and [`LineHeightSpan`](https://developer.android.com/reference/android/text/style/LineHeightSpan).
- WinUI 3: [`ITextCharacterFormat`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.text.itextcharacterformat?view=windows-app-sdk-1.8), [`ITextParagraphFormat`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.text.itextparagraphformat?view=windows-app-sdk-1.8), and `RichEditBox` RTF loading.
