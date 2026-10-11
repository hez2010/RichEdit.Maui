---
title: Platform behavior
description: Native implementation boundaries, feature fidelity, and preview limits.
---

# Platform behavior

| Platform | Default text engine | Structured content |
| --- | --- | --- |
| Windows | WinUI RichEditBox | Native RichEdit RTF tables, lists, fields, and attachments |
| Android | Native editable text with spans and InputConnection | Native scroll/container/grid views with editable flows and cells coordinated by one document/session |
| iOS / Mac Catalyst | UITextView and TextKit | Native attributed text and RTF structures |

The controls use no WebViews. Small custom regions cover missing native features: secondary selections/carets, source gutters, unsupported decoration strokes, widget positioning, and Android cell-edge borders. Text shaping, ordinary editing, IME, and primary selection remain native.

## Fidelity

Model retention, editing, rendering, and RTF persistence are separate contracts. A retained property may be approximated by a native engine. See the [RTF matrix](../../RTF_SUPPORT.md) for the supported persistent profile. Native fonts and layout can produce different wrapping on different platforms.

Rich editing includes body paragraphs, semantic lists/tables, inline resources, annotations, and retained revision groups. Original and Final review views are available. Inline redline markup, Word-native revision interchange, pagination, printing, sections, headers/footers, notes, floating objects, and editable equations are outside this preview's feature profile.

## Performance

Source storage is persistent and line lookup is indexed. Native controls still keep their own buffers. Windows source readback can inspect the native story, and native formatting/reservations can incur broad layout work.

Source views avoid rich paragraph metadata, and Android/Apple native replacement notifications restore bounded source edits. Apple source synchronization updates native storage incrementally. Android bounds text-service payloads separately from document size; see [input](input.md).

The test suite contains 100-sample dense-presentation and large-source workloads. A rendering callback is a frame opportunity, not proof of GPU presentation. The 50 ms desktop / 100 ms mobile p95 targets and 16 ms publication-slice target are engineering goals, not supported guarantees for this preview. Native 10 MiB split-view load and long-line input can exceed those targets.

Android rich tables currently realize their native cell/container structure eagerly. Large table performance is therefore bounded by native view count. Lazy widget realization does not virtualize the table grid itself. Rectangular source gestures currently operate on visible rows and do not retain their rectangle across scrolling.

## Accessibility and input

Native accessibility is retained and adapted for canonical source text, tables, multiple ranges, and widget children where implemented. Automated checks cover provider text and table/selection metadata. Actual Narrator, TalkBack, and VoiceOver operation, minimum-platform behavior, dictation, and mixed-DPI behavior require application/device validation; provider checks alone do not establish that certification.

Use native touch handles and menus for the primary selection. Secondary caret and widget behavior should be tested with the application's accessibility settings, fonts, and text scaling.
