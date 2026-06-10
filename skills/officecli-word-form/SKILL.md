---
name: officecli-word-form
description: "Use this skill to create fillable Word forms (.docx) with real Content Controls (SDT) + legacy FormField checkboxes + MERGEFIELD mail-merge placeholders + document protection. Trigger on: 'fillable form', 'form fields', 'content controls', 'SDT', 'word form', 'fill in', 'only editable fields', 'protect document', 'onboarding form', 'HR intake', 'survey template', 'contract / SOW template', 'mail-merge template', 'compliance checklist', 'medical intake questionnaire'. Output is a single .docx where specific fields are editable and the rest is locked. This skill is INDEPENDENT, not a scene layer on docx — payload is `<w:sdt>` + `<w:ffData>` + `<w:fldChar>` + `documentProtection`, none of which docx base skill covers. Do NOT trigger for regular reports, letters, memos, academic papers, pitch decks, or any document with no user-fillable fields — route those to officecli-docx or its scene layers."
---

# OfficeCLI Word-Form Skill

**This skill is INDEPENDENT, not a scene layer on docx.** A form's payload — `<w:sdt>` controls, `<w:ffData>` legacy fields, `<w:fldChar>` mail-merge, `documentProtection` — is a distinct element class from docx's paragraph/heading/style primitives. Its QA is different too: docx's Delivery Gate cares about visual layout and live PAGE fields, this skill's cares about data plumbing (protection enforced / alias+tag / items injected / name ≤ 20 / no underscore anti-pattern). **Reverse handoff:** if the user's document has no fillable fields (report, letter, memo, thesis, proposal), route to `officecli-docx` or a docx scene skill — don't use this one.

## BEFORE YOU START (CRITICAL)

**If `officecli` is not installed:**

`macOS / Linux`

```bash
if ! command -v officecli >/dev/null 2>&1; then
    curl -fsSL https://d.officecli.ai/install.sh | bash
fi
```

`Windows (PowerShell)`

```powershell
if (-not (Get-Command officecli -ErrorAction SilentlyContinue)) {
    irm https://d.officecli.ai/install.ps1 | iex
}
```

Verify: `officecli --version`

If `officecli` is still not found after first install, open a new terminal and run the verify command again.

If the install command above fails (e.g. blocked by security policy, no network access, or insufficient permissions), install manually — download the binary for your platform from https://github.com/iOfficeAI/OfficeCLI/releases — then re-run the verify command.

## Help-First Rule

This skill teaches what a real form needs, not every CLI flag. When a prop / alias / enum is uncertain, consult help BEFORE guessing: `officecli help docx [element] [--json]` (e.g. `sdt`, `formfield`, `field`). Help is pinned to installed version, and is authoritative for SDT/field props. Exception: in local OfficeCLI 1.0.109, legacy FormField add-time props are implemented and verified even though help under-lists them; use the FormField section below and validate readback.

## Mental Model & Inheritance

A Word form is a `.docx` plus four OpenXML payload layers plain-docx skills do not touch: **`<w:sdt>`** content controls (text / richtext / dropdown / combobox / date), **`<w:ffData>`** legacy FormField (still the CLI path for a real checkbox in 1.0.109), **`<w:fldChar>`** complex fields (MERGEFIELD, REF, PAGEREF, SEQ, IF — template-time, not user-fill), and **`documentProtection`** (the lock that makes non-field text read-only in Word).

**No inheritance from docx v2.** docx's Delivery Gate (cover-fill %, live-PAGE check) does NOT apply — form QA is `view forms` + `query sdt alias+tag` + `protectionEnforced`.

**Reverse handoff to docx.** Route back to `officecli-docx` for reports / letters / memos / thesis / pitch decks / any document with no editable fields. Use **this** skill when the document's purpose is data capture or template merge.

## Shell & Execution Discipline

**One command at a time. Read output before the next.** OfficeCLI is incremental — every `add` / `set` / `remove` immediately mutates the file. All recipes below use `FILE=form.docx` as a shell variable.

**Three shell-escape layers:**

1. **Quote every path with `[N]`** — zsh/bash glob-expand brackets. `officecli get "$FILE" /body/sdt[1]` fails with `no matches found`. Correct: `officecli get "$FILE" '/body/sdt[1]'`.
2. **Single-quote any prop containing `$`** — `"Total: $50,000"` becomes `"Total: ,000"` after `$50` variable expansion. Correct: `'Total: $50,000'`.
3. **`--after find:<text>` uses outer single quotes, never inner double quotes** — `--after find:"Client Signature:"` makes the quotes part of the search string; match fails. Correct: `--after 'find:Client Signature:'`.

**`WARNING: UNSUPPORTED` (exit 2) is a wrong command, even if the element was created.** Treat any UNSUPPORTED warning in the build log or batch step output as a stop condition: check `officecli help docx <element>`, then rewrite to a supported high-level prop, a separate `set`, or Path B (`raw-set`) only when the prop is not exposed.

**`protection=forms` is the LAST command.** After protection is saved/closed, non-field mutations require `--force`; SDT/formfield fill paths remain editable. Keeping protection last avoids accidental force usage and gives Word users a consistent locked experience on first open.

### `--after find:` micro-playbook

`--after find:<text>` matches the **first** occurrence. Bad anchor = wrong insertion location, expensive to debug. Three rules:

1. **Anchor must be globally unique.** In bilingual contracts "甲方签字" matches both parties — use a unique phrase like "甲方签字（Service Provider）" or full English title.
2. **After insert, `/body/p[last()]` is unreliable** — the find insertion changes `<w:body>` child order. To continue operating on the new paragraph, read its real paraId: `officecli query "$FILE" paragraph --json | jq -r '.data.results[-1].format.paraId'`.
3. **Chinese + full-width parens `（）`** match literally in `find`, but when unsure, `officecli view "$FILE" text | grep -n "锚点"` first to confirm the exact bytes in the file.

```bash
# Trap: first-match hits 甲方 only, 乙方 missed
officecli add "$FILE" /body --type sdt --after 'find:签字'

# Fix: two signatories, two unique anchors
officecli add "$FILE" /body --type sdt --prop alias=Party_A_Name --prop tag=party_a \
  --after 'find:甲方签字（Service Provider）'
PID_A=$(officecli query "$FILE" paragraph --json | jq -r '.data.results[-1].format.paraId')
officecli add "$FILE" "/body/p[@paraId='$PID_A']" --type sdt --prop alias=Party_A_Title --prop tag=party_a_title
```

Inline SDT via `--after find:` is added as a child of the matched paragraph, not as a new paragraph — use this when label + SDT must share a line.

## What makes a real form (identity)

A real fillable form requires **structured fields** + **document protection**.

| Approach | Word user sees | CLI-readable | Real form? |
|---|---|---|---|
| SDT controls + `protection=forms` | Gray-bordered fields; rest locked | `query sdt` / `view forms` | **YES** |
| FormField checkbox + `protection=forms` | Real clickable checkbox; rest locked | `query formfield` / `view forms` | **YES** (checkbox only) |
| MERGEFIELD placeholders | `«CustomerName»` merged by downstream engine | `query field` | **YES** (template-time) |
| Underscores `___` / blank lines | Visual-only; whole doc editable | No — no structured fields | **NO** |

**Do not simulate fields with underscores.** `姓名：_______________` produces zero structured data and leaks past every verification. Always use `--type sdt` or `--type formfield`.

**Checkbox is formfield, NOT SDT.** `--type sdt --prop type=checkbox` exits 1 (`SDT type 'checkbox' is not implemented`). Every checkbox in every recipe uses `--type formfield --prop type=checkbox`.

**MERGEFIELD is a separate track.** `view forms` lists SDT + formfield only; `query field` lists complex fields only. Two disjoint inventories; both valid in one file.

## Requirements for Outputs (hard floor)

Every form must satisfy these — Delivery Gate enforces each as an executable check.

1. `protection=forms` enforced (`get $FILE /` → `protectionEnforced=True`).
2. Every SDT has both `alias` + `tag`.
3. Every dropdown/combobox has non-empty `items=...` in `view forms`.
4. Every date SDT shows the intended `format=...`.
5. Every locked SDT shows `lock=sdtLocked` / `contentLocked` / `sdtContentLocked` as intended.
6. Zero `WARNING: UNSUPPORTED` in build log.
7. Zero `type=checkbox` on any SDT.
8. Every formfield `name` ≤ 20 characters.
9. Zero underscore-line / blank-line placeholders.
10. Field types match user intent (short text / paragraph / fixed list / list+custom / date / boolean).

## Three Paths (core decision)

OfficeCLI 1.0.109 exposes the common SDT props directly: `type`, `tag`, `alias`, `text`, `items`, `format`, `lock`, `placeholder`, `placeholderText`, `date.fullDate`, `date.calendar`, `date.lid`, `date.storeMappedDataAs`, `comboBox.lastValue`, `dropDown.lastValue`. `name` / `maxlength` are still not SDT props. The skill therefore splits uncommon OpenXML needs into three paths. **Pick the path before writing a single command.**

### Path A — Pure CLI (simple forms)

**Use when**: the field is a normal text/richtext/dropdown/combobox/date SDT, with optional items, date format, initial value, and lock. This now covers most forms.

```bash
officecli add "$FILE" /body --type sdt \
  --prop type=text \
  --prop alias="Full Name" --prop tag=full_name \
  --prop text="Enter full name"

officecli add "$FILE" /body --type sdt \
  --prop type=dropdown --prop alias="Department" --prop tag=dept \
  --prop 'items=Engineering|ENG,Finance|FIN' --prop lock=sdtContentLocked

officecli add "$FILE" /body --type sdt \
  --prop type=date --prop alias="Start Date" --prop tag=start_date \
  --prop format=yyyy-MM-dd --prop date.fullDate=2026-01-01T00:00:00Z

officecli set "$FILE" / --prop protection=forms
```

### Path B — CLI + `raw-set` bridge (complex attrs)

**Use when**: the form needs something outside `officecli help docx sdt`, such as wrapping an existing static paragraph in a locked block SDT, custom XML mappings beyond the exposed date/list props, or vendor-specific form markup. `raw-set` is OfficeCLI's universal OpenXML fallback — `officecli --help` lists it as a top-level command.

```bash
# Standard dropdown/date/lock no longer need raw-set:
officecli add "$FILE" /body --type sdt \
  --prop type=dropdown --prop alias="Department" --prop tag=dept \
  --prop 'items=Engineering|ENG,Finance|FIN' --prop lock=sdtLocked

# Reserve raw-set for unsupported structures after checking help:
officecli raw-set "$FILE" /document --xpath "..." --action replace --xml '<w:sdt>...</w:sdt>'
```

### Path C — Word template (beyond raw-set)

**Use when**: `picture` SDT (signature image), real SDT checkbox (`type=checkbox` exits 1), `placeholderDocPart` prompt text, grouped SDTs wrapping multiple paragraphs, or custom richtext appearance. These involve cross-part relationships or nesting beyond `--prop` reach.

```bash
# One-time in Word: Developer tab → Insert Content Control → Save as template.docx
cp templates/onboarding_with_signature.docx "$FILE"
officecli open "$FILE"
officecli view "$FILE" forms                 # inspect embedded controls + paths
officecli set "$FILE" '/body/sdt[@sdtId=3]' --prop text="Jane Smith"
officecli set "$FILE" / --prop protection=forms
```

### Decision table

| Need | Path | Note |
|---|---|---|
| text / richtext SDT with default string | **A** | canonical props cover it |
| text SDT that must be locked | **A** | `lock` works on `add` and `set` in 1.0.109 |
| dropdown / combobox **with options** | **A** | use direct `items=Display|Value,...` |
| date SDT with non-default format | **A** | use direct `format=...` and optional `date.fullDate=...` |
| real checkbox | **FormField** | `--type formfield --prop type=checkbox` (see §Legacy FormField) |
| mail-merge placeholder | **MERGEFIELD** | `--type field --prop fieldType=mergefield` (see §MERGEFIELD) |
| signature picture, grouped SDT, placeholder part | **C** | build skeleton in Word, fill via CLI |

## Quick Start — Path A + FormField (minimal intake form)

Two SDT text fields, one checkbox, protection. Paste and adapt; this is the smallest form worth shipping.

```bash
FILE=intake.docx
officecli close "$FILE" 2>/dev/null; rm -f "$FILE"   # preflight: clear stale resident / prior file (cold-start after CLI upgrade commonly leaks a resident)
officecli create "$FILE"
officecli open "$FILE"

officecli set "$FILE" / --prop title="Employee Onboarding Intake" \
  --prop docDefaults.font="Calibri" --prop docDefaults.fontSize="12pt"

officecli add "$FILE" /body --type paragraph \
  --prop text="Employee Onboarding Intake" --prop style=Heading1 \
  --prop size=20 --prop bold=true --prop spaceAfter=18pt

officecli add "$FILE" /body --type paragraph \
  --prop text="Full Name:" --prop size=11 --prop bold=true --prop spaceAfter=4pt
officecli add "$FILE" /body --type sdt --prop type=text \
  --prop alias="Full Name" --prop tag=full_name --prop text="Enter full name"

officecli add "$FILE" /body --type paragraph \
  --prop text="Start Date:" --prop size=11 --prop bold=true --prop spaceAfter=4pt
officecli add "$FILE" /body --type sdt --prop type=date \
  --prop alias="Start Date" --prop tag=start_date

officecli add "$FILE" /body --type paragraph \
  --prop text="Read and agree to employee handbook" --prop size=11 --prop spaceAfter=4pt
officecli add "$FILE" /body --type formfield \
  --prop type=checkbox --prop name=agree_handbook --prop checked=false

officecli set "$FILE" '/body/sdt[1]' --prop lock=sdtlocked
officecli set "$FILE" '/body/sdt[2]' --prop lock=sdtlocked
officecli set "$FILE" / --prop protection=forms
officecli close "$FILE"
officecli view "$FILE" forms
```

## Path B — raw-set bridge (rare)

Do not use `raw-set` for ordinary dropdown items, combobox items, date formats, placeholder text, or locks. OfficeCLI 1.0.109 exposes those through SDT props:

```bash
officecli add "$FILE" /body --type sdt \
  --prop type=dropdown --prop alias="Department" --prop tag=dept \
  --prop 'items=Engineering|ENG,Finance|FIN,HR|HR' --prop dropDown.lastValue=ENG

officecli add "$FILE" /body --type sdt \
  --prop type=combobox --prop alias="Current Medication" --prop tag=current_med \
  --prop 'items=Antihypertensives,Insulin,Other' --prop comboBox.lastValue=Other

officecli add "$FILE" /body --type sdt \
  --prop type=date --prop alias="Contract Start Date" --prop tag=contract_start \
  --prop format=yyyy年MM月dd日 --prop date.fullDate=2026-01-01T00:00:00Z
```

Use `raw-set` only after `officecli help docx sdt` confirms the needed property is absent. Typical remaining cases:

| Need | Why raw/template is still needed |
|---|---|
| Wrap an existing static paragraph in a locked block SDT | High-level `add sdt` creates a new control; it does not wrap an existing paragraph |
| Custom XML data binding beyond exposed `date.*` / list props | Requires cross-part mapping and relationship details |
| Vendor-specific form markup | No stable high-level schema contract |

```bash
# Example fallback: replace a known paragraph with a handcrafted block SDT.
PARA_XML=$(officecli raw "$FILE" /document | awk "/w14:paraId=\"$PID\"/,/<\\/w:p>/" | tr -d '\n')
officecli raw-set "$FILE" /document \
  --xpath "//w:p[@w14:paraId='$PID']" \
  --action replace \
  --xml "<w:sdt xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:w14=\"http://schemas.microsoft.com/office/word/2010/wordml\"><w:sdtPr><w:alias w:val=\"Locked Clause\"/><w:tag w:val=\"locked_clause\"/><w:lock w:val=\"contentLocked\"/></w:sdtPr><w:sdtContent>${PARA_XML}</w:sdtContent></w:sdt>"
```

### raw-set actions & errors

| `--action` | Form use |
|---|---|
| `append` | Insert new child at end of target (B1, B2 — listItem) |
| `setattr` | Change one attribute; `--xml "key=value"` (B3 — dateFormat/@val) |
| `replace` | Replace entire target (rare — reset a full `<w:date>` wrapper) |
| `remove` | Delete the target (clear options before re-populate) |

| Symptom | Fix |
|---|---|
| `raw-set: 0 element(s) affected` | XPath did not match. Check the `tag` value and whether the SDT is block or inline. Fall back to `officecli raw $FILE /document` to read the real XML. |
| `Error: prefix 'w' is not defined` | Missing `xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"` on the fragment — every root element in `--xml` needs it. |
| Items readback empty after append | `<w:dropDownList/>` must already exist (Path A `type=dropdown` ensures this). If absent, append has nowhere to insert. |
| `VALIDATION: N new error(s) introduced` on same line as success | Your append introduced a schema-invalid child. Treat as stop-and-fix even though `raw-set` exits 0. |

## Path C — Word template workflow

For fields CLI cannot express (signature `picture` SDT, real SDT checkbox, `placeholderDocPart` prompt text, grouped SDTs, custom richtext styling), build the skeleton once in Word, then fill via CLI.

**One-time in Word:** File → Options → Customise Ribbon → Developer. Developer tab → Insert Picture / Check Box / Grouping Content Control → right-click → Properties → set Title (`alias`) + Tag. Save as `template.docx`.

**Fill via CLI:**

```bash
cp templates/onboarding_with_signature.docx "$FILE"
officecli open "$FILE"
officecli view "$FILE" forms                                    # see /body/... paths + sdtId values
officecli set "$FILE" '/body/sdt[@sdtId=3]' --prop text="Jane Smith"
officecli set "$FILE" / --prop protection=forms
officecli close "$FILE"
```

## MERGEFIELD (data-driven track)

`help docx field` declares a `fieldType` enum of 30+ values including `mergefield`, `ref`, `pageref`, `seq`, `if` — all CLI-expressible with their typed props. MERGEFIELD coexists with SDT in the same file but is reported by `query field` only; `view forms` does NOT list MERGEFIELDs (they are not user-fillable).

**Canonical MERGEFIELD:**

```bash
officecli add "$FILE" /body --type paragraph --prop text="Dear "
officecli add "$FILE" '/body/p[1]' --type field --prop fieldType=mergefield --prop name=CustomerName
officecli add "$FILE" '/body/p[1]' --type run --prop text=", "
officecli add "$FILE" '/body/p[1]' --type field --prop fieldType=mergefield --prop name=CompanyName
# Readback: "Dear «CustomerName», «CompanyName»"
```

**Element-type shortcut** (equivalent): `officecli add "$FILE" '/body/p[1]' --type mergefield --prop name=CustomerName`.

### Common field patterns

| Pattern | Call shape |
|---|---|
| Mail-merge placeholder | `--type field --prop fieldType=mergefield --prop name=<FieldName>` |
| Mail-merge with numeric picture (money, percent) | `--type field --prop fieldType=mergefield --prop name=Amount --prop instr='MERGEFIELD Amount \# "#,##0.00"'`. For mergefield picture switches, prefer `instr` (alias `instruction`) to embed the full field code. Verify: `query "$FILE" field --json \| jq '.data.results[].format.instruction'` must contain `\#` and the picture. |
| Mail-merge with date picture | `--type field --prop fieldType=mergefield --prop name=StartDate --prop instr='MERGEFIELD StartDate \@ "yyyy-MM-dd"'` |
| Cross-reference to bookmark text | `--type field --prop fieldType=ref --prop name=<BookmarkName>` |
| Cross-reference to bookmark's page number | `--type field --prop fieldType=pageref --prop name=<BookmarkName>` |
| Auto-numbering (Figure 1 / 2 / 3) | `--type field --prop fieldType=seq --prop identifier=Figure` |
| Page number in footer | `--type field --prop fieldType=page` |
| "Page X of Y" | two fields: `fieldType=page` + `fieldType=numpages` |
| Conditional text | `--type field --prop fieldType=if --prop expression='{ MERGEFIELD Gender } = "Male"' --prop trueText="Mr." --prop falseText="Ms."` |

### IF conditional (CLI-expressible)

```bash
officecli add "$FILE" /body --type paragraph --prop text=""
officecli add "$FILE" '/body/p[last()]' --type field --prop fieldType=if \
  --prop expression='{ MERGEFIELD Gender } = "Male"' \
  --prop trueText="Mr." --prop falseText="Ms."
officecli add "$FILE" '/body/p[last()]' --type run --prop text=" "
officecli add "$FILE" '/body/p[last()]' --type field --prop fieldType=mergefield --prop name=LastName
# Merge-time result: "Mr. «LastName»" or "Ms. «LastName»"
```

Nested wrappers like `{ IF { MERGEFIELD X } = "Y" { REF bm } "fallback" }` are not expressible via `--prop` chaining — drop to raw-set a hand-crafted `<w:fldChar>` / `<w:instrText>` fragment, or build once in a Word template (Path C).

**Readback.** `query $FILE field` lists `/field[N]` + instruction + `fieldType`. `view $FILE forms` does NOT list MERGEFIELDs (only SDT + formfield) — they are template-time, not end-user fillable. `get $FILE '/body/p[1]'` renders the guillemet-wrapped field name.

## Legacy FormField

Use FormField **when you need a real checkbox** or must interoperate with legacy Word form fields. For new text/dropdown fields, prefer SDT unless a downstream Word workflow specifically requires FormField.

`help docx formfield` publicly lists `type` (text/checkbox/check/dropdown), `name` (required, **≤ 20 chars** — OpenXML schema MaxLength; add passes longer but `validate` rejects), `text` (text only, alias `value`), and `checked` (checkbox only). Local 1.0.109 also writes and reads these add-time props even though help has not fully caught up: general `enabled`, `calcOnExit`, `entryMacro`, `exitMacro`, `helpText`, `statusText`; dropdown `items`, `default`, `result`; text `default`, `maxlength`, `texttype`, `textformat`; checkbox `checkboxsize`.

```bash
# CHECKBOX — use legacy formfield; SDT checkbox is still not implemented
officecli add "$FILE" /body --type formfield --prop type=checkbox \
  --prop name=agree_terms --prop checked=false

# TEXT formfield with legacy text constraints
officecli add "$FILE" /body --type formfield --prop type=text \
  --prop name=emp_name --prop default="123" --prop maxlength=12 \
  --prop texttype=number --prop textformat=0000

# DROPDOWN formfield — legacy list, use comma-separated labels
officecli add "$FILE" /body --type formfield --prop type=dropdown \
  --prop name=dept_select --prop items=Engineering,Finance,HR --prop result=1

# Read / modify by name (stable) or 1-based index
officecli get "$FILE" '/formfield[agree_terms]'
officecli set "$FILE" '/formfield[agree_terms]' --prop checked=true
officecli set "$FILE" '/formfield[emp_name]' --prop text="Jane Smith"
officecli set "$FILE" '/formfield[dept_select]' --prop text="Engineering"
```

FormField paths (`/formfield[N]` or `/formfield[<name>]`) are separate from SDT paths (`/body/sdt[N]`). Both coexist; `protection=forms` covers both.

**Scale.** Tested with 50+ checkboxes in a single document — no practical cap on formfield count; build and `validate` remain clean. `name` ≤ 20 chars (K13) is the only hard constraint.

**Renderer note — formfield checkbox `[RENDERER-BUG]`.** LibreOffice's PDF export occasionally renders the formfield checkbox as `☐☐` (doubled box). Word and WPS render a single clickable box (toggles ☑). This is a LibreOffice renderer quirk, **not a skill or document quality issue** — see K19. Do not attempt workarounds in the form; if an evaluator screenshots a LibreOffice-generated PDF and sees `☐☐`, attribute to `[RENDERER-BUG]`.

## Document protection & lock

### Enabling form protection

```bash
officecli set "$FILE" / --prop protection=forms
officecli get "$FILE" /                                  # look for: protectionEnforced=True
```

### Protection modes

| Mode | Word user can | CLI behavior |
|---|---|---|
| `forms` | Fill SDT + formfield only | Field paths work; non-field writes need `--force` after protection is saved |
| `readOnly` | Read only | Non-field writes need `--force` after protection is saved |
| `comments` | Add comments only | Non-field writes need `--force` after protection is saved |
| `trackedChanges` | Edit with tracked changes only | Non-field writes need `--force` after protection is saved |
| `none` | Full editing | All ops work |

**KEY:** Protection is enforced by OfficeCLI once it is persisted. If you set `protection=forms` and keep editing in the same resident before `close`, the disk-based protection gate may not see the new lock yet; do not rely on that. Put protection last, close the file, and use `--force` only for intentional non-field edits to an already-protected document.

### Lock values (applied via `add` or `set`)

```bash
officecli add "$FILE" /body --type sdt --prop type=text --prop alias=Name --prop tag=name --prop lock=sdtLocked
officecli set "$FILE" '/body/sdt[1]' --prop lock=sdtlocked           # content editable; control cannot be deleted
officecli set "$FILE" '/body/sdt[1]' --prop lock=contentlocked       # content read-only; control can be deleted
officecli set "$FILE" '/body/sdt[1]' --prop lock=sdtcontentlocked    # both locked
# Omit lock entirely → unlocked (default)
```

`--prop lock=...` works on SDT `add` and `set` in 1.0.109. Readback normalises to camelCase (`sdtLocked`, `contentLocked`, `sdtContentLocked`) regardless of input case — both accepted.

### lock × `protection=forms` interaction

| lock value | `protection=forms` active | Word user can edit? | Word user can delete control? |
|---|---|---|---|
| (none) | yes | **Yes** | **Yes** |
| `sdtlocked` | yes | Yes | No |
| `contentlocked` | yes | No | Yes |
| `sdtcontentlocked` | yes | No | No |
| block-level SDT wrap `contentlocked` | any | No (wrapped paragraph read-only regardless of protection) | No |
| any | `readOnly` mode | No | No |

### Block-level lock (paragraph-wrapping SDT)

`protection=forms` is document-level — once an admin unprotects, every static paragraph (disclaimer, legal attestation, contract clause) becomes editable again. Master templates need defense-in-depth: wrap the critical paragraph in a block-level `<w:sdt>` with `lock=contentLocked`, so the content stays read-only even after protection is stripped.

```bash
officecli add "$FILE" /body --type paragraph \
  --prop text="I authorize the above and acknowledge all clauses." --prop size=11 --prop spaceAfter=12pt
PID=$(officecli query "$FILE" paragraph --json | jq -r '.data.results[-1].format.paraId')

# raw-set actions: append | prepend | insertbefore | insertafter | replace | remove | setattr
# No `wrap` action — two-step instead: (1) insertbefore an empty <w:sdt><w:sdtContent/></w:sdt>,
# (2) move the original <w:p> inside by `replace` on the sdtContent with a copy of the paragraph XML.
# Simpler alternative: read the paragraph XML via `officecli raw`, then `replace` the whole <w:p> with <w:sdt>...<w:sdtContent>[original w:p]</w:sdtContent></w:sdt>:
PARA_XML=$(officecli raw "$FILE" /document | awk "/w14:paraId=\"$PID\"/,/<\\/w:p>/" | tr -d '\n')
officecli raw-set "$FILE" /document \
  --xpath "//w:p[@w14:paraId='$PID']" \
  --action replace \
  --xml "<w:sdt xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:w14=\"http://schemas.microsoft.com/office/word/2010/wordml\"><w:sdtPr><w:alias w:val=\"Authorization\"/><w:tag w:val=\"auth_para\"/><w:lock w:val=\"contentLocked\"/></w:sdtPr><w:sdtContent>${PARA_XML}</w:sdtContent></w:sdt>"
```

Verify with `query sdt --json | jq '.data.results[] | select(.format.lock == "contentLocked" and .format.type == "block")'`. Use only for legal attestations, compliance disclaimers, confidentiality clauses — regular intake fields do not need this.

### Role-gated fields (multi-role forms)

When one form is filled by two roles (patient vs physician; Party A vs Party B), use `lock=contentLocked` on the fields the other role must not touch. Under `protection=forms`, `contentLocked` SDTs display as read-only in Word; the intended role unprotects (or the admin swaps role-specific copies) to fill the other half.

```bash
# Patient section — editable (no lock, or sdtlocked to prevent accidental deletion only)
officecli set "$FILE" '/body/sdt[1]' --prop lock=sdtlocked      # patient_name
officecli set "$FILE" '/body/sdt[2]' --prop lock=sdtlocked      # patient_dob

# Physician section — locked against patient edits
officecli set "$FILE" '/body/sdt[14]' --prop lock=contentLocked # physician_diagnosis
officecli set "$FILE" '/body/sdt[15]' --prop lock=contentLocked # physician_signature
```

This is the core pattern for medical intake, two-party contracts, sequential-approval forms.

## Recipe — Contract / SOW template with MERGEFIELD + signature

Row-map across the three sub-recipes: SDT[1]=project_name, SDT[2]=contract_start, SDT[3]=payment_schedule, SDT[4]=signatory_name (inline). Run (sow-a) → (sow-b) → (sow-c) in order on the same `$FILE`; each sub-recipe stays under 20 lines so a shell-escape slip never cascades past one block.

### Recipe (sow-a) Boilerplate + cover + parties

Creates the file, sets docDefaults, writes the title / intro, and drops the two MERGEFIELD placeholders (`CustomerName`, `ContractNo`) that downstream mail-merge will fill.

```bash
FILE=sow.docx
officecli create "$FILE"
officecli open "$FILE"
officecli set "$FILE" / --prop title="Statement of Work" \
  --prop docDefaults.font="Calibri" --prop docDefaults.fontSize="12pt"

officecli add "$FILE" /body --type paragraph --prop text="Statement of Work" \
  --prop style=Heading1 --prop size=20 --prop bold=true --prop spaceAfter=12pt
officecli add "$FILE" /body --type paragraph \
  --prop text="This Statement of Work ('SOW') is entered into between the parties identified below and governs the delivery of professional services." \
  --prop size=11 --prop spaceAfter=12pt

officecli add "$FILE" /body --type paragraph --prop text="Customer: "
officecli add "$FILE" '/body/p[last()]' --type field \
  --prop fieldType=mergefield --prop name=CustomerName
officecli add "$FILE" /body --type paragraph --prop text="Contract #: "
officecli add "$FILE" '/body/p[last()]' --type field \
  --prop fieldType=mergefield --prop name=ContractNo
```

### Recipe (sow-b) SDT fields with direct list/date props

Adds the three block-level SDTs (project / date / dropdown), the inline signature SDT anchored via `--after 'find:Client Signature:'`, with date format, dropdown items, and locks set directly via SDT props.

```bash
officecli add "$FILE" /body --type sdt --prop type=text \
  --prop alias="Project Name" --prop tag=project_name --prop text="Enter project name"
officecli add "$FILE" /body --type sdt --prop type=date \
  --prop alias="Contract Start Date" --prop tag=contract_start \
  --prop format=MM/dd/yyyy
officecli add "$FILE" /body --type sdt --prop type=dropdown \
  --prop alias="Payment Schedule" --prop tag=payment_schedule \
  --prop 'items=Full Prepayment,Net 30 Upon Delivery'
officecli add "$FILE" /body --type paragraph --prop text="Client Signature:" \
  --prop bold=true --prop spaceBefore=18pt --prop spaceAfter=4pt
officecli add "$FILE" /body --type sdt --prop type=text \
  --prop alias="Signatory Name" --prop tag=signatory_name --prop text="Authorized Signatory" \
  --after 'find:Client Signature:'
```

### Recipe (sow-c) Watermark + locks + document protection

Drops the CONFIDENTIAL watermark (parent is `/`, never `/body`), locks the three block-level SDTs, instructs how to lock the inline signatory_name SDT (path only known after `view forms`), then seals the document with `protection=forms` as the last command.

```bash
officecli add "$FILE" / --type watermark \
  --prop text="CONFIDENTIAL" --prop color=FF0000 --prop rotation=315

officecli set "$FILE" '/body/sdt[1]' --prop lock=sdtlocked
officecli set "$FILE" '/body/sdt[2]' --prop lock=sdtlocked
officecli set "$FILE" '/body/sdt[3]' --prop lock=sdtlocked
officecli view "$FILE" forms   # copy signatory_name path, then: set '/body/p[@paraId=...]/sdt[1]' --prop lock=sdtlocked

officecli set "$FILE" / --prop protection=forms
officecli close "$FILE"
officecli query "$FILE" field     # expect 2 MERGEFIELDs: CustomerName, ContractNo
```

## Design principles (forms)

**Control-type decision tree:**

```
Date → type=date | Fixed list → type=dropdown | List + custom → type=combobox
Short text → type=text | Long text → type=richtext | Boolean → formfield checkbox
```

**Typography scale.** Spacing unit trap: `spaceBefore` / `spaceAfter` / `spaceLine` default to **twips** (1/20 pt) — always write `spaceBefore=18pt`.

| Element | Size | Style | Spacing |
|---|---|---|---|
| Form title (H1) | 20pt | Bold | `spaceBefore=0pt`, `spaceAfter=12pt` |
| Section heading (H2) | 14pt | Bold | `spaceBefore=18pt`, `spaceAfter=8pt` |
| Field label | 11pt | Bold | `spaceAfter=4pt` |
| Instructions / notes | 11pt | Italic `color=666666` | `spaceAfter=18pt` |

**Accessibility bump.** For medical / geriatric / accessibility-focused forms, raise field label + instruction to **12pt** (11pt default is tight for older users); keep section headings at 14pt.

**CJK forms:** set `docDefaults.font="Microsoft YaHei"` — Calibri lacks Chinese glyphs.

**Field ordering.** (1) Personal / ID, (2) role / classification, (3) dates, (4) supplemental free-text, (5) confirmation / signature.

**Yes/No + conditional follow-up** (common in compliance / medical intake): formfield checkbox followed by a richtext SDT whose `alias` carries the cue — e.g. `--type formfield --prop type=checkbox --prop name=has_cond` then `--type sdt --prop type=richtext --prop alias="If yes, explain" --prop tag=cond_detail --prop text="If yes, explain here"`.

**Signature block order.** Label on its own paragraph, SDT on the next paragraph (with `spaceBefore=18pt` on the label, `spaceAfter=4pt` on the SDT). Never `Label: SDT` inline — Word renders the runs as touching, visually stuck together.

**Build order.** create+open → metadata → structure (headings, label paragraphs) → SDT/formfield controls with direct props → rare Path B raw-set fallback only if needed → per-field lock → `protection=forms` LAST → close.

**Header / footer note.** Headers/footers are **predefined** when the section is created (default/first/even, 3 each). The first mutation must be `set` against the existing part, not `add` — `add $FILE /header ...` returns `already exists` or silently no-ops. Inspect first with `officecli query "$FILE" header --json` to read the `type` values, then `officecli set "$FILE" '/header[@type=default]' --prop text=...`. Only use `add` when creating an additional section with its own header/footer.

## Batch mode (brief)

For forms with many controls, batch reduces overhead. Direct SDT/FormField props work in batch; include `raw-set` steps only for rare Path B fallbacks.

```bash
cat <<'EOF' | officecli batch "$FILE"
[
  {"command":"add","parent":"/body","type":"sdt","props":{"type":"text","alias":"Full Name","tag":"full_name","text":"Enter name","lock":"sdtLocked"}},
  {"command":"add","parent":"/body","type":"sdt","props":{"type":"dropdown","alias":"Department","tag":"dept","items":"Engineering|ENG,Finance|FIN","lock":"sdtLocked"}}
]
EOF
officecli set "$FILE" / --prop protection=forms
```

- Escape inner `"` in `xml` with `\"` if you include `raw-set` XML. Use single-quoted heredoc `<<'EOF'` so `$var` does not expand.
- In 1.0.109, SDT `items`, `format`, `lock`, and `date.*` props work inside batch JSON. Still run `get '/sdt[N]' --json` or `view forms` after batch; unsupported props in any element are a build failure.
- `batch` supports `add`, `set`, `get`, `query`, `remove`, `move`, `swap`, `view`, `raw`, `raw-set`, `validate`.

## Delivery Gate (executable)

Run every gate below after every form. Each gate must print its `OK` line. Any `REJECT` = do not deliver.

```bash
# Assumes FILE=<your-form.docx>, document has been closed with officecli close "$FILE"

# Gate 1 — Validate (documentProtection waiver: K8 allows this ONE schema error under protection=forms)
VAL_OUT=$(officecli validate "$FILE" 2>&1)
VAL_ERRS=$(echo "$VAL_OUT" | grep -c '\[Schema\]')
VAL_PROT=$(echo "$VAL_OUT" | grep -c 'documentProtection')
if   [ "$VAL_ERRS" -eq 0 ]; then echo "Gate 1 OK (validate clean)"
elif [ "$VAL_ERRS" -eq 1 ] && [ "$VAL_PROT" -eq 1 ]; then echo "Gate 1 OK (1 documentProtection waiver — K8)"
else echo "REJECT Gate 1: $VAL_ERRS schema errors beyond the K8 waiver"; echo "$VAL_OUT"; exit 1
fi

# Gate 2 — Token / placeholder leak (labels used as visual underscore substitutes)
LEAK=$(officecli view "$FILE" text | grep -niE '_{3,}|TBD|\(fill in\)|\{\{|xxxx|lorem|placeholder')
[ -z "$LEAK" ] && echo "Gate 2 OK (no underscore / placeholder leak)" || { echo "REJECT Gate 2:"; echo "$LEAK"; exit 1; }

# Gate 3 — At least one structured field exists
SDT_N=$(officecli query "$FILE" sdt --json | jq '.data.results | length')
FF_N=$(officecli query "$FILE" formfield --json | jq '.data.results | length')
FLD_N=$(officecli query "$FILE" field --json | jq '.data.results | length')
TOTAL=$((SDT_N + FF_N + FLD_N))
[ "$TOTAL" -gt 0 ] && echo "Gate 3 OK ($SDT_N sdt + $FF_N formfield + $FLD_N field)" || { echo "REJECT Gate 3: 0 structured fields — this is not a form"; exit 1; }

# Gate 4 — Every SDT has alias + tag (skill-imposed H2)
# NOTE: `query --json` wraps prop fields under `.format.{prop}` — jq paths below use `.format.alias` / `.format.tag` (not bare `.alias`).
SDT_MISSING=$(officecli query "$FILE" sdt --json | jq '[.data.results[] | select(.format.alias == null or .format.alias == "" or .format.tag == null or .format.tag == "")] | length')
[ "$SDT_MISSING" -eq 0 ] && echo "Gate 4 OK (every SDT has alias+tag)" || { echo "REJECT Gate 4: $SDT_MISSING SDT(s) missing alias or tag"; exit 1; }

# Gate 5 — Protection enforced + per-field lock inventory
PROT=$(officecli get "$FILE" / --json | jq -r '.data.format.protection // "none"')
[ "$PROT" = "forms" ] && echo "Gate 5 OK (protection=forms enforced)" || { echo "REJECT Gate 5: protection is '$PROT', expected 'forms'"; exit 1; }
officecli view "$FILE" forms | head -40   # visual spot-check: every dropdown shows items=; every date shows format=; every locked SDT shows lock=

# Gate 6 — No type=checkbox leaked onto any SDT
BAD_CB=$(officecli query "$FILE" sdt --json | jq '[.data.results[] | select(.format.type == "checkbox")] | length')
[ "$BAD_CB" -eq 0 ] && echo "Gate 6 OK (no SDT checkbox — formfield only)" || { echo "REJECT Gate 6: $BAD_CB SDT with type=checkbox"; exit 1; }
```

**Why `view issues` is not a gate.** It runs only prose-style checks (first-line-indent, heading size) and flags every form label as `Body paragraph missing first-line indent` — a false-positive avalanche on forms. Ignore for this skill. Use `validate` (schema integrity) and `view forms` (field inventory).

## Known Issues

| # | Issue | Behavior | Workaround |
|---|---|---|---|
| K1 | SDT `type=checkbox` not implemented in 1.0.109 | `add ... --type sdt --prop type=checkbox` → `Error: SDT type 'checkbox' is not implemented`, exit 1 | Use `--type formfield --prop type=checkbox`, or Path C template |
| K2 | SDT dropdown/date/lock props are high-level now | `items`, `format`, `lock`, `placeholder`, `date.*`, `comboBox.lastValue`, `dropDown.lastValue` are in `help docx sdt` | Use direct props first; raw-set only after help confirms the needed prop is absent |
| K3 | SDT `name` / `maxlength` are not SDT props | `WARNING: UNSUPPORTED`, exit 2; element still created | Use `alias`+`tag` instead of `name`; enforce maxlength downstream or in template |
| K4 | SDT `type` cannot be changed after creation | `set --prop type=...` is unsupported; type is add/get | Remove and re-add with the intended type |
| K5 | FormField help under-lists add-time props in 1.0.109 | `help docx formfield` lists only the common public props, but local CLI accepts legacy props such as `maxlength`, `texttype`, `textformat`, `items`, `default`, `result` | Use the examples above, then verify with `get '/formfield[name]' --json` and `validate` |
| K6 | FormField dropdown item format differs from SDT | FormField uses comma-separated labels only; SDT supports `Display|Value` pairs | Prefer SDT for new dropdowns; use FormField dropdown only for legacy Word-form compatibility |
| K7 | Watermark `opacity` / `width` / `height` / `size` UNSUPPORTED | Watermark created without them; `get /watermark` still prints hardcoded `opacity=0.5` | Do not set them. For size, open Word + adjust shape (Phase 2) |
| K8 | `validate` reports a `documentProtection` Schema error under `protection=forms` | Prints the error line, exits **0**. Gate 1 waives this one specific error | Confirm protection with `get $FILE /` → `protectionEnforced=True`. Known validator bug, not a document bug |
| K9 | Batch hides interactive warnings in dense output | It can report success while you miss unsupported-prop text in logs | After batch, verify with `view forms` / `get '/sdt[N]' --json`; direct SDT `items/format/lock/date.*` are supported in 1.0.109 |
| K13 | FormField `name` > 20 characters | `add` returns exit 0 with no warning; `validate` later reports `[Schema] ... MaxLength=20` on `/w:ffData/w:name` | Keep `name` ≤ 20 characters (OpenXML schema limit). SDT `alias` / `tag` have no such limit |
| K14 | `shd.fill` on a paragraph emits schema-invalid `<w:pPr>/<w:shd>` | `validate` reports 2 schema errors per instance (`unexpected child element`, `required attribute 'val' missing`); Word renders it anyway | Apply highlight on the run instead (`shading=HEX`, flat canonical), or raw-set `<w:shd w:val="clear" w:fill="HEX"/>` into the run's `<w:rPr>` |
| K15 | `view forms` does NOT list MERGEFIELDs | Only SDT + formfield in output; MERGEFIELDs are template-time, not end-user fillable | Treat `query field` and `view forms` as two disjoint inventories. Every recipe verifies both |
| K16 | Header / footer are predefined at section creation (default/first/even, 3 each) | `add $FILE /header ...` returns `already exists` or silently no-ops on the first call | First mutation uses `set` against the existing part: `officecli query $FILE header --json` to read `type`, then `set '/header[@type=default]' --prop text=...`. Only use `add` for a brand-new section's header/footer |
| K17 | Watermark injected into header emits `<w:noProof>` child that is schema-invalid | `validate` adds an extra `[Schema]` error at `/header[N]/w:sdt/.../w:noProof` — NOT covered by K8's documentProtection waiver | After `add $FILE / --type watermark`, run once per header part: `officecli raw-set $FILE /word/header1.xml --xpath "//w:noProof" --action remove` (repeat for `header2.xml`, `header3.xml` if present) |
| K18 | `query --json` wraps prop fields under `.format.{prop}` | Writing jq against bare `.alias` / `.tag` / `.protection` returns 0 matches, Gate 4/5 falsely report "missing=N" | Always prefix jq with `.format.`: `.data.results[].format.alias`, `.data.results[].format.tag`, `.data.format.protection` (for `get /`). Same for `.format.type` and `.format.paraId` |
| K19 | LibreOffice renders formfield checkbox as `☐☐` (double box) in PDF export | Cosmetic only — Word / WPS render a single box, clickable to toggle ☑. A LibreOffice renderer quirk, flagged as [RENDERER-BUG] | Do not try to "fix" in the skill. If an evaluator screenshots from LibreOffice-generated PDF and sees `☐☐`, attribute to [RENDERER-BUG], not a form-quality defect |

## Phase 2 — enhance in Word

Some polish is out of CLI scope. Hand the file to a human for these; none are required for a valid form.

| Need | Why open Word |
|---|---|
| Signature image field (`picture` SDT) | Cross-part relationship + media file |
| Real SDT checkbox with specific locking | `type=checkbox` exits 1; use Developer → Check Box Content Control |
| Prompt text ("Click here to enter a date") | Needs `placeholderDocPart` in `/word/glossary/document.xml` |
| Grouped SDT wrapping multiple paragraphs | Block-level `<w:sdt>` nesting beyond `add` |
| Custom richtext default appearance | Adjust the referenced style in Word's style pane |
| Watermark resize | `width` / `height` not in schema; drag shape handles |

For the first four, build the skeleton once (Path C) and reuse.

## Help pointer

When in doubt: `officecli help docx`, `officecli help docx <element>`, `officecli help docx <element> --json`. Help is the authoritative schema; this skill is the decision guide for building real fillable Word forms on top of it.
