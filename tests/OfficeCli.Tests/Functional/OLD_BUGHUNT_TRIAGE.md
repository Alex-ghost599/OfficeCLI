# Old BugHunt Test Triage

This file records the current first-pass split for historical BugHunt failures.
It is intentionally scoped to test behavior observed from:

- `scripts/test-full.sh --group baseline-assets --results-dir TestResults/goal-baseline-assets`
- `scripts/test-full.sh --group bug-regression --no-build --results-dir TestResults/goal-bug-regression`
- `scripts/test-full.sh --group bug-regression --no-build --results-dir TestResults/goal-bug-regression-after-sixth-pass`

## Rewritten As Fixed-Bug Regressions

These tests previously asserted or tolerated the buggy behavior. They now assert
the fixed behavior directly:

- `BugHuntTests.Part2.cs` `Bug161_PptxAnimations_NegativeDurationAccepted`: negative animation durations must throw.
- `BugHuntTests.Part3.cs` `Bug224_ParseEmu_UnsupportedUnitsCrash`: `mm` image units are accepted.
- `BugHuntTests.Part4.cs` `Bug324_ExcelAdd_ValidationBetweenWithoutFormula2`: `between` validation requires `formula2`.

## Fixed Product Bugs

These groups were confirmed as product bugs and fixed in handler/query code:

- Excel conditional formatting: Get now reports `cfType` and `sqref` aliases for CF nodes, color-scale readback includes lowercase `mincolor`/`maxcolor` aliases, and `type=greaterThan` routes to `cellIs` with `operator=greaterThan`.
- Word style boolean toggles: style Get now respects `w:val=false` for `bold`, `italic`, `bold.cs`, and `italic.cs` instead of treating element presence as `true`.
- PPTX query shorthand: `shape:Find me` no longer trips the unsupported descendant-combinator check on spaces inside the shorthand text.
- PPTX multiline text: shape and table-cell text writers now split both real newlines and literal `\n` API input into DrawingML paragraphs, preserving paragraph-level alignment workflows.
- PPTX empty-text shape defaults: setting run-level properties on a runless shape now materializes a run target so later text preserves those properties.

## Rebaselined To Current Readback Shape

These failures came from stable readback shape changes rather than a failed
mutation:

- `BugHuntPart51.cs` `Bug5115_WordParagraphFirstLineIndentRoundTrip`: `720` twips reads back as `36pt`.
- `BugHuntPart51.cs` `Bug5126_WordRunRtlRoundTrip`: run RTL reads back as `direction=rtl` plus `effective.rtl=true`.
- `BugHuntPart28.cs` and `BugHuntPart53.cs` Word paragraph line spacing tests: raw line spacing `360` reads back as `360x`.
- `BugHuntPart28.cs` Word paragraph indent tests: twip inputs read back as point units.
- `BugHuntPart53.cs` `Bug5324` through `Bug5338`: Word run boolean properties now read back explicit `false` after being set false.
- `BugHuntPart53.cs` `Bug5339_WordRunRtlRoundTrip`: run RTL uses `direction` and `effective.rtl`.
- `PptxEnhancementTests.cs` run-level baseline lifecycle tests: baseline reads back with `%` units.
- `BugHuntPart28.cs`, `BugHuntPart35.cs`, `BugHuntPart42.cs`, and `BugHuntPart45.cs` Word paragraph/cell alignment tests: `alignment` remains an input alias, while Get returns canonical `align`.
- `BugHuntTests.Part12.cs` run RTL test: run RTL uses `direction` and `effective.rtl`.
- `BugHuntPart31.cs` Word paragraph indent tests: twip inputs read back as point units.
- `BugHuntPart41.cs`, `BugHuntPart42.cs`, and `BugHuntPart43.cs` Excel tests: blank workbooks already contain `Sheet1`; duplicate setup `Add("/", "sheet", name=Sheet1)` was removed so tests exercise their target behavior.
- `BugHuntPart41.cs` `Bug4122_Excel_Add_Cell_Clear_Incomplete`: clear now omits the default `type` key instead of reporting `Number`.
- `BugHuntPart29.cs` and `BugHuntPart31.cs` Excel validation list tests: list formulas read back with surrounding quotes.
- `BugHuntPart23.cs`, `BugHuntPart32.cs`, and `BugHuntPart45.cs` PPTX vertical alignment tests: `valign=center` reads back as canonical `middle`.
- `BugHuntPart43.cs` connector line color test: `lineColor` input reads back through the canonical `color` key.
- `BugHuntPart39.cs`, `BugHuntPart42.cs`, and `BugHuntPart49.cs` connector line color tests: `lineColor` input reads back through the canonical `color` key.
- `BugHuntPart41.cs` and `BugHuntPart44.cs` connector line color tests: connector line color also reads back through the canonical `color` key.
- `BugHuntPart47.cs` shape hyperlink test: shape links no longer gain a trailing slash.
- `BugHuntPart40.cs` line dash test: `dashdot` reads back as `dashDot`.
- `BugHuntPart40.cs` line dash test: `longdash` reads back as `lgDash`.
- `BugHuntPart16.cs` table cell paragraph alignment test: Word paragraph Get returns canonical `align`.
- `BugHuntPart19.cs` and `BugHuntPart40.cs` Word hyperlink tests: run hyperlink reads back as `isHyperlink` plus `url`.
- `BugHuntPart15.cs`, `BugHuntPart31.cs`, `BugHuntPart36.cs`, `BugHuntPart40.cs`, `BugHuntPart41.cs`, `BugHuntPart48.cs`, and `BugHuntTests.Part6.cs` Word font tests: `font` reads back through `font.latin` and related effective keys.
- `BugHuntPart23.cs`, `BugHuntPart29.cs`, and `BugHuntTests.Part2.cs` Excel underline tests: `font.underline` input reads back through canonical `underline`.
- `BugHuntPart39.cs` named range scope test: workbook scope is reported explicitly as `scope=workbook`.
- `BugHuntPart47.cs` soft edge test: soft edge radius reads back with `pt`.
- `BugHuntPart34.cs` first-line indent test: twip input reads back as point units.
- `BugHuntPart44.cs` Excel tests: blank workbooks already contain `Sheet1`; duplicate setup was removed.
- `BugHuntPart30.cs`, `BugHuntPart32.cs`, `BugHuntPart42.cs`, `BugHuntPart45.cs`, `BugHuntPart47.cs`, and `BugHuntPart54.cs` PPTX preset geometry tests: `preset` remains an input alias, while Get returns canonical `geometry`.
- `BugHuntPart32.cs`, `BugHuntPart33.cs`, `BugHuntPart41.cs`, `BugHuntPart45.cs`, and `BugHuntPart47.cs` PPTX fill/glow/shadow alpha tests: Get preserves alpha in the color token as `#RRGGBBAA`.
- `BugHuntPart28.cs`, `BugHuntPart32.cs`, `BugHuntPart41.cs`, `BugHuntPart44.cs`, `BugHuntPart49.cs`, and `BugHuntPart51.cs` Word shading tests: Get reports structured `shading.val` and `shading.fill` keys instead of legacy aggregate keys.
- `BugHuntPart28.cs` rare Word run RTL test: run RTL reads back as `direction=rtl` plus `effective.rtl=true`.
- `BugHuntPart28.cs`, `BugHuntPart45.cs`, `BugHuntPart49.cs`, and `BugHuntPart53.cs` Word boolean false tests: explicit `false` values remain visible in Get.
- `BugHuntPart29.cs` Excel font strike test: `font.strike` input reads back through canonical `strike`.
- `BugHuntPart29.cs` and `BugReproTests.cs` Excel row height tests: row height reads back with `pt` units.
- `BugHuntPart15.cs` table cell text/font and PPTX table-cell alignment tests: Get uses `font.latin` and canonical `align`.
- `BugHuntPart30.cs` PPTX `list=none` test and `ChartBugHuntTests.cs` legend none tests: absence-like states read back as explicit `none`.
- `BugHuntPart34.cs` Excel cleared cell text test: empty cells read back as `(empty)` while formula removal is still asserted.
- `BugHuntPart14.cs`, `BugHuntPart29.cs`, `BugHuntPart35.cs`, and `BugHuntPart46.cs` Excel `clear=true` style tests: current contract clears value/formula and preserves cell formatting (`StyleIndex`).
- `BugHuntPart36.cs`, `BugHuntPart37.cs`, and `BugHuntPart39.cs` Word footnote/endnote format-key tests: current `footnote`/`endnote` contract supports note-body formatting keys such as `bold`, `font`, `color`, and `italic`; tests now reserve unsupported assertions for truly unknown keys.
- `BugHuntPart14.cs` paragraph font/new-run test: bare `font` Set on an already-populated paragraph formats existing runs but intentionally does not fabricate paragraph-mark defaults; use explicit `markRPr.*` when mark inheritance is required.
- `BugHuntPart21.cs`, `BugHuntPart23.cs`, `BugHuntPart28.cs`, and `BugHuntPart49.cs` Word formatting readback tests: shading, borders, and table-cell spans read back through structured canonical keys (`shading.*`, `pbdr.*.color`, `colspan`).
- `BugHuntPart16.cs`, `BugHuntTests.Part3.cs`, `BugHuntPart30.cs`, and `BugHuntPart47.cs` PPTX table/style/alignment tests: table style reads back through canonical `style`, table-cell gradient details live on `gradient` with `fill=gradient`, and table-cell/shape valign keep their current separate vocabularies.
- `BugHuntPart30.cs`, `BugHuntPart35.cs`, `BugHuntPart41.cs`, `BugHuntPart47.cs`, and `BugHuntPart52.cs` PPTX baseline/default-align tests: baseline is run-level only and reads back on run nodes with `%` units; bare `align` is emitted only when alignment was explicitly set.
- `BugHuntPart44.cs` PPTX line dash test: `longdash` reads back as OOXML token `lgDash`.
- `BugHuntPart45.cs` PPTX indent test: `1cm` reads back as `28.35pt`.
- `BugHuntPart55.cs` multi-stop gradient position test: custom stop `@30` reads back as OOXML position token `@p30000`.

## Remaining Failure Priority

This priority list is based on the 40 failures in
`TestResults/goal-bug-regression-after-sixth-pass/failed-tests.tsv`.

- P0 Excel `clear=true` style behavior, 4 tests: design. `schemas/help/xlsx/cell.json` defines `clear` as clearing value/formula before applying new content, so preserving cell formatting is the current contract and the tests were rebaselined.
- P0 Excel conditional formatting, 5 tests: bug, fixed. Get was missing `cfType`/`sqref` for added rules, and `greaterThan` was rejected instead of mapping to the supported `cellIs` form.
- P0 Word footnote/endnote unsupported-property reporting, 6 tests: design/new contract. `schemas/help/docx/footnote.json` and `endnote.json` declare note-body formatting keys as supported; the tests were rebaselined to verify supported formatting plus reporting for truly unknown keys.
- P1 PPTX multiline text and per-paragraph formatting, 7 tests: bug, fixed. Literal `\n` API input and real newlines now build multiple DrawingML paragraphs for shape/table-cell text.
- P1 PPTX shape-level defaults and baseline propagation, 6 tests: mixed. Empty-text shape run defaults were a bug and are fixed; baseline/default align assertions were rebaselined to the run-level/no-explicit-default contract.
- P1 PPTX table/style/query behavior, 6 tests: mostly design/new contract plus one fixed bug. Table style and gradient readback use canonical `style`/`gradient`; table-cell align already uses `align`; shape/textbox and table-cell valign keep separate readback vocabularies; `shape:text` shorthand was a query bug and is fixed.
- P1 Word formatting inheritance and structured readback, 6 tests: mostly design/new contract plus one fixed bug. Structured readback uses `shading.*`, `pbdr.*.color`, and `colspan`; populated-paragraph font Set does not create markRPr defaults; style add belongs under `/styles`; style toggle false readback was a bug and is fixed.

## Kept For Product Investigation

These are not safe to rebaseline without product review because they may signal
lost structure, lost formatting, or API contract drift:
