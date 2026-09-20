---
title: Platform behavior and RTF
description: Understand native control differences, portable formatting, clipboard interchange, and RTF preservation limits.
---

# Platform behavior and RTF

RichEdit.Maui stores formatting independently of the native control. A format can remain in the document even when a platform cannot display it fully.

## Native controls

| Platform | Minimum version | Text surface |
| --- | --- | --- |
| Windows | Windows 10 1809 | WinUI 3 `RichEditBox` |
| Android | API 26 | `AppCompatEditText` with editable spans |
| iOS | 15.0 | `UITextView` with attributed text |
| Mac Catalyst | 15.0 | `UITextView` with attributed text |

CodeEditor uses these same text controls.

## Rendering and persistence

Android displays several underline styles as a single underline. Apple displays picture bullets as fallback text. General fields show their supplied result without evaluating the instruction. The original format or metadata can still survive editing and RTF export.

Other properties, such as application metadata and image source identifiers, remain in memory but are not saved to RTF. Rendering and persistence are listed separately in the [RTF support matrix](../../RTF_SUPPORT.md).

## RTF import and export

The editor works with continuous text rather than pages. Import converts some RTF structures and discards others:

- Tables are flattened to tab-separated cells and newline-separated rows.
- Page and section breaks become paragraph breaks; page-layout properties are discarded.
- Headers, footers, footnotes, comments, tracked revisions, and other unsupported destinations are skipped.
- OLE objects and drawing content can be flattened to result text, inline pictures, or placeholders; opaque object payloads are not re-emitted.

Paragraph breaks use LF in the model, and soft line breaks use U+2028. RTF stores opaque RGB colors and half-point font sizes, so export rounds higher-precision values and loses color alpha.

PNG and JPEG render on all supported platforms. Some other image formats can be retained without being rendered.

## Clipboard and menus

Windows and Apple publish rich RTF and plain text to the system clipboard. Android imports plain text from other applications. Copies within the running application can retain a complete rich fragment while the clipboard still identifies that copy. CodeEditor uses plain source text.

Custom context and selection menus require iOS/Mac Catalyst 16 or later; earlier Apple versions retain their standard menus. Native menu adapters omit menu-item icons and keyboard-accelerator labels.
