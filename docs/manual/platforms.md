---
title: Platform behavior
description: Platform differences, supported features, and preview limits.
---

# Platform behavior

The editors support Windows, Android, iOS, and Mac Catalyst. They provide native text editing, input methods, primary selection, touch handles, and context menus. They do not use WebViews.

## Fidelity

Some formatting properties are preserved in saved documents but displayed approximately on a particular platform. Fonts, line wrapping, and table appearance can differ between devices. See [RTF support](rtf-support.md) for individual features.

Rich editing includes body paragraphs, semantic lists/tables, inline resources, annotations, and retained revision groups. Original and Final review views are available. Inline redline markup, Word-native revision interchange, pagination, printing, sections, headers/footers, notes, floating objects, and editable equations are outside this preview's feature profile.

## Large documents

Large files, very long lines, many widgets, and large Android tables can reduce responsiveness. Give the editor a finite viewport with fill alignment, use data-backed widget sources for many hints, and set `WordWrap = false` when wrapping very long source lines is unsuitable. Load and save large documents asynchronously with cancellation.

Extremely large Windows documents can show blank text at distant scroll positions. That scale is outside this preview's supported Windows editing profile.

## Accessibility and input

Use native touch handles and menus for the primary selection. Give meaningful widgets an `AccessibleDescription`, and make their interactive controls keyboard-accessible. Input and accessibility behavior follows the device's language, keyboard, text scaling, and assistive-technology settings. See [input](input.md) for composition and clipboard behavior.
