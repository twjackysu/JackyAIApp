# TASK-015

## Title
Frontend: CSV import for Portfolio holdings (Gap Audit item 2)

## Owner
Frontend

## Status
PR_OPENED

## Priority
MEDIUM (per PM's approved Gap Audit order — Portfolio proposal 2, next
default item, unblocked, no further Boss sign-off needed)

## Objective
Let a user bulk-load holdings into the existing `HoldingsEditor` from a CSV
file instead of typing every row by hand, closing the export/import
asymmetry (`portfolioExport.ts` has export, nothing has import).

## Context
See `.agent/architecture/ARCHITECTURE.md` → "Decision: Portfolio CSV import
for holdings — Gap Audit item 2" for the full investigation and reasoning.
Read it before implementing — in particular:
- The import column schema is **new and purpose-built**, not a round-trip
  of the existing export CSV. The export schema is a computed report
  (TWD-converted market value/cost/P&L) with no clean mapping back to
  `PositionInput`; reusing it as-is would silently corrupt cost-basis data
  for foreign-currency holdings. Do not skip this — it's a real correctness
  trap, not a style preference.
- A generic CSV/TSV parser already exists in
  `DataAnalysis/components/UploadDataDialog.tsx` (`detectSep`/`parseLine`/
  `parseTabular`). Extract it into a shared util; do not write a second one.

## Requirements

1. **Extract the shared parser**: move `detectSep`, `parseLine`, and
   `parseTabular` out of `DataAnalysis/components/UploadDataDialog.tsx` into
   a new `jackyaiapp.client/src/utils/tabularParse.ts`. Update
   `UploadDataDialog.tsx` to import from there instead. This must be a
   behavior-preserving refactor for `DataAnalysis` — no functional change,
   verify its existing usage still works (run its own tests / lint / build).

2. **Import column schema** (new `PositionInput`-shaped schema, distinct
   from `portfolioExport.ts`'s export schema):
   - `類型` — accepts either the raw `AssetType` value (`STOCK`/`CASH`/
     `FUND`/`LIABILITY`/`REAL_ESTATE`) or its Chinese label from
     `portfolioExport.ts`'s existing `TYPE_LABEL` map (reverse-lookup it,
     don't hardcode a second copy of the mapping).
   - `代號` — required for STOCK/FUND, ignored for CASH/LIABILITY/REAL_ESTATE.
   - `名稱` — optional; if blank, fall back to the same per-type default
     `editorTypes.ts`'s `makeRow` already uses (e.g. "現金", "信貸", "不動產").
   - `市場` — STOCK only; `TW` or `US`, defaults to `TW` if blank.
   - `計價幣別` — defaults to `TWD` if blank (matching `SaveHoldingsAsync`'s
     own server-side default).
   - `成本幣別` — optional, defaults to `計價幣別`.
   - `數量` — required, must parse as a positive number.
   - `成本` — optional. **Total cost in `成本幣別`, unconverted** — this is
     `PositionInput.CostBasis` exactly (a total, not per-unit — see
     `CostBasisInput.tsx`). Do NOT treat this as TWD.
   - `估價方式` — REAL_ESTATE only, `市價` or `買價扣除折舊`.

3. **New "匯入 CSV" entry point** in `HoldingsEditor.tsx`'s update dialog:
   - A button (near the dialog title / alongside existing per-section "新增"
     buttons) opens a small sub-dialog: file picker (`<input type="file"
     accept=".csv">`, `FileReader.readAsText`, same pattern as
     `UploadDataDialog.tsx`'s file tab) → parse via the shared util → map
     each row to `PositionInput` per the schema above → run through the
     existing `rowError()` (from `editorTypes.ts`) for validation → show a
     preview (row count, how many valid/invalid, a short table of the first
     few rows) before committing.
   - An "加入" (add) action in the sub-dialog appends the mapped+validated
     rows to the *existing* `HoldingsEditor` `rows` state via
     `.map(toRow)` (reuse `toRow` from `editorTypes.ts` as-is) — it does
     **not** call `useSaveHoldingsMutation` directly. The user still reviews
     the imported rows in the normal editor sections and clicks the
     existing "儲存" button to persist, going through all existing
     client-/server-side validation unchanged.
   - Invalid rows (bad/missing required field) must not be silently
     imported as if valid — either exclude them from "加入" by default with
     a visible count ("3 筆略過，欄位有誤"), or import them but pre-flag them
     so the existing red-highlight treatment shows immediately without
     requiring a save attempt first. Either is acceptable; silently
     importing a broken row with no indication is not.

4. **"下載匯入範本" button** next to "匯入 CSV": downloads a template CSV
   (headers from the schema above + 1-2 example rows, one STOCK one CASH is
   enough) using the existing `downloadCsv` helper in `portfolioExport.ts`.

## Acceptance Criteria
- [ ] `utils/tabularParse.ts` exists with the extracted parser;
      `UploadDataDialog.tsx` uses it and its existing behavior/tests are
      unchanged.
- [ ] `HoldingsEditor`'s update dialog has a working "匯入 CSV" button that
      opens a file picker, parses a `.csv`, previews mapped rows, and an
      "加入" action that appends them into the editor's row sections (a
      STOCK row lands in 台股/美股, CASH in 現金, etc. — sections already key
      off `assetType`/`market`, this should need no change there).
- [ ] Imported STOCK/FUND rows render as already-picked chips (not a live
      search box) — confirms the `symbol`-set-means-picked assumption holds.
- [ ] A CSV with one bad row (e.g. missing 數量) alongside good rows does
      not silently import the bad row as valid — it's excluded or flagged.
- [ ] "下載匯入範本" downloads a CSV whose headers exactly match what
      "匯入 CSV" expects to parse (round-trip the template through import
      yourself as a manual check while implementing).
- [ ] Importing never calls the save mutation directly — persisting still
      requires the existing "儲存" button, so a bad import is always
      reviewable/editable before it touches the backend.
- [ ] `npm run lint` (0 warnings), `npm run build`, `npx vitest run` all
      pass with no regressions.

## Technical Notes
- No backend change of any kind — do not touch `JackyAIApp.Server`.
  `SaveHoldingsRequest`/`PositionInput`/`SaveHoldingsAsync` are unchanged;
  imported rows ride the exact same save path as manually-typed ones.
- Reuse `toRow`/`rowError`/`isRowEmpty` from `editorTypes.ts` as-is — do not
  fork or duplicate row validation logic for the import path.
- `成本(TWD)` in the *export* CSV is a different number from `成本` in the
  *import* CSV (converted vs. unconverted, in different currencies for
  foreign holdings) — these are deliberately different schemas. Don't try
  to unify them; see Architecture doc's Alternatives.
- Unit-test the pure row-mapping/validation logic (CSV row → `PositionInput`
  or a validation error) directly, same convention as
  `Portfolio/concentration.ts`/`exposureGauges.ts`'s existing `*.test.ts`
  siblings — no need for `@testing-library/react`-style interaction tests
  (not available in this project; use `renderToStaticMarkup` for any
  component-level test, per `TASK-014`'s precedent).

## Dependencies
None — pure frontend, no other open task's code needed.

## Result

**PR**: https://github.com/twjackysu/JackyAIApp/pull/368

Implemented exactly as scoped in `ARCHITECTURE.md` — frontend-only, no backend/DB change.

- **`utils/tabularParse.ts`** (new): extracted `detectSep`/`parseLine`/`parseTabular` out of
  `DataAnalysis/components/UploadDataDialog.tsx` verbatim (behavior-preserving —
  `UploadDataDialog.tsx` now just imports `parseTabular` from the shared module). Added
  `tabularParse.test.ts` covering separator detection, quoted-CSV parsing, and short-row padding
  (no prior test existed for this logic).
- **`Portfolio/csvImport.ts`** (new): the purpose-built import schema, distinct from
  `portfolioExport.ts`'s export schema —
  - `mapImportRow(row)` maps one parsed CSV row to a `PositionInput`, accepting either the raw
    `AssetType` or its Chinese `TYPE_LABEL` (reverse-looked-up from `portfolioExport.ts`'s
    existing map, exported for this purpose — no second copy of the mapping), applying every
    default from the schema (市場→TW, 計價幣別→TWD, 成本幣別→計價幣別, 估價方式→市價,
    per-type 名稱 fallback via the same defaults `editorTypes.ts`'s `makeRow` uses), then builds
    an `EditorRow` via `toRow` and validates it with the existing `rowError()` — no second,
    parallel validation system. An unknown/missing 類型 or an unparseable 成本 short-circuits
    with its own explicit error message (both are hard-to-guess-a-default failures); everything
    else rides `rowError()`'s existing bar (a missing/zero/non-numeric 數量, or a missing 代號
    for STOCK/FUND, both surface as `rowError()`'s ordinary messages).
  - `parseImportCsv(text)` — `parseTabular` + `mapImportRow` per row; one bad row's error
    doesn't affect the others.
  - `IMPORT_TEMPLATE_HEADERS`/`IMPORT_TEMPLATE_ROWS` + `downloadImportTemplate()` (via the newly
    exported `downloadCsv` from `portfolioExport.ts`) — single source of truth shared with the
    parser, so the template can never drift from what import actually expects; verified by a
    test that round-trips the template rows through `parseImportCsv` and asserts zero errors.
- **`Portfolio/HoldingsImportDialog.tsx`** (new): self-contained trigger-button + sub-dialog
  component, same pattern as the existing `UpdateHistoryDialog`. Renders "匯入 CSV" (file picker
  → `parseImportCsv` → preview table with a per-row status column) and "下載匯入範本" buttons.
  The preview shows total/valid/skipped counts and, per the acceptance criteria, invalid rows
  are excluded from "加入" by default with a visible "N 筆略過，欄位有誤" count rather than
  silently importing them. "加入" calls `onImport(positions)` with only the valid rows — it never
  calls `useSaveHoldingsMutation` itself.
- **`HoldingsEditor.tsx`**: renders `<HoldingsImportDialog onImport={importRows} />` in the
  update dialog's title row (flex `space-between`, wraps on mobile); `importRows` is
  `setRows((rs) => [...rs, ...positions.map(toRow)])` — imported rows land in `rows` state
  exactly like manually-added ones, so they show up in the correct section (台股/美股/現金/...)
  automatically and go through the existing red-highlight-on-save-attempt validation. No direct
  save call added anywhere in the import path.

### Files Changed
- `jackyaiapp.client/src/utils/tabularParse.ts` (new)
- `jackyaiapp.client/src/utils/tabularParse.test.ts` (new)
- `jackyaiapp.client/src/DataAnalysis/components/UploadDataDialog.tsx` (import from the shared util)
- `jackyaiapp.client/src/Portfolio/portfolioExport.ts` (export `TYPE_LABEL`/`downloadCsv`)
- `jackyaiapp.client/src/Portfolio/csvImport.ts` (new)
- `jackyaiapp.client/src/Portfolio/csvImport.test.ts` (new)
- `jackyaiapp.client/src/Portfolio/HoldingsImportDialog.tsx` (new)
- `jackyaiapp.client/src/Portfolio/HoldingsImportDialog.test.tsx` (new)
- `jackyaiapp.client/src/Portfolio/HoldingsEditor.tsx` (new import entry point)

### Tests
- `npm run lint` — 0 warnings/errors.
- `npm run build` (tsc + vite build) — passes.
- `npx vitest run` — 168/168 passed (22 files), including the 3 new/updated test files above; no
  regressions (`DataAnalysis`'s existing behavior unchanged — verified by the full suite passing,
  though there was no pre-existing dedicated test for `UploadDataDialog` to compare against).
- Manually traced the template round-trip via the `csvImport.test.ts` test rather than a live
  browser session (no dev server / browser check performed this run — see Known Issues).

### Known Issues
- Not manually verified in a running browser this session (no dev server / browser check
  performed) — only build/lint/unit-test verified, same caveat `TASK-014` flagged. Worth a quick
  manual pass (pick a CSV with a good + a bad row, confirm the preview/skip-count, confirm
  "加入" lands rows in the right editor section) before/at QA.
- STOCK/FUND rows imported by symbol are not re-validated against the live search API (by
  design — see ARCHITECTURE.md's Trade-offs; a typo'd symbol saves as-is, same as the existing
  snapshot-restore path).
- No `@testing-library/react` in the project, so the file-picker interaction itself isn't
  exercised by an automated test (same limitation noted in `TASK-014`) — `HoldingsImportDialog`'s
  test only covers its closed-by-default static render; the row-mapping logic that matters most
  (`csvImport.ts`) is fully covered as pure functions instead.

### Blockers
None.

## Review
(Fill in after PR review.)
