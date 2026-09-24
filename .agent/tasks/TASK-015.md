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

**PR**: https://github.com/twjackysu/JackyAIApp/pull/367

Implemented as scoped in `ARCHITECTURE.md` — frontend-only, no backend/DB change.

- **Extracted the shared parser**: `detectSep`/`parseLine`/`parseTabular` moved out of
  `DataAnalysis/components/UploadDataDialog.tsx` into new `jackyaiapp.client/src/utils/tabularParse.ts`
  (unit tested directly). `UploadDataDialog.tsx` now imports from there — behavior-preserving,
  verified via its own component test plus `npx vitest run`/`npm run build`.
- **`Portfolio/csvImport.ts`** (new): the purpose-built import schema, distinct from
  `portfolioExport.ts`'s export schema —
  - `mapImportRow(row)`: maps one parsed CSV row to a `PositionInput` candidate. `類型` accepts
    either the raw `AssetType` or its Chinese label, reverse-looked-up from `portfolioExport.ts`'s
    existing `TYPE_LABEL` (now exported) rather than a second hardcoded map. `市場` only applies to
    STOCK (defaults `TW`); FUND is forced to `market: 'FUND'`; CASH/LIABILITY/REAL_ESTATE get `''`,
    matching `editorTypes.ts`'s `makeRow`. `計價幣別` defaults `TWD`; `成本幣別` defaults to
    `計價幣別`. `成本` is parsed as `PositionInput.costBasis` verbatim — a total, unconverted, in
    `成本幣別` — never the export CSV's TWD-converted `成本(TWD)`. `估價方式` (REAL_ESTATE only)
    defaults `市價`, or errors on an unrecognized value. `名稱` blank falls back to the same
    per-type default `makeRow` uses (現金/信貸/不動產) for those three types only. Quantity gaps
    are left as `0` rather than hard-erroring — deliberately, so the shared `rowError()` check
    (not a second parallel validator) is what actually flags a missing/zero quantity or missing
    symbol.
  - `buildImportPreview(rows)`: maps every row via `mapImportRow`, then runs the successfully
    mapped ones through `toRow`/`rowError` as-is (imported via `editorTypes.ts`, unmodified) — one
    bad row (unrecognized type, non-numeric `成本`, missing 數量/代號) never blocks the other rows.
  - `downloadImportTemplateCsv()`: reuses the now-exported `downloadCsv` helper from
    `portfolioExport.ts` to produce a template CSV (`IMPORT_HEADERS` + one STOCK + one CASH example
    row) — round-tripped through `mapImportRow`/`buildImportPreview` in a dedicated test to confirm
    the template's own headers/example rows parse back into fully valid rows.
- **`Portfolio/ImportCsvDialog.tsx`** (new): self-contained trigger-button + sub-dialog component
  (same pattern as `UpdateHistoryDialog.tsx`) — file picker (`<input type="file" accept=".csv">`,
  `FileReader.readAsText`, same pattern as `UploadDataDialog.tsx`'s file tab) → `parseTabular` →
  `buildImportPreview` → a preview table (row count, valid/invalid split, first 10 rows with a
  per-row status/error). Invalid rows are excluded from "加入" by default with a visible
  "N 筆略過，欄位有誤" count (the task's first listed option, not the pre-flag alternative).
  "加入" appends only the valid `EditorRow[]` to the parent's `rows` state — never calls
  `useSaveHoldingsMutation` directly; the user still reviews in the normal editor sections and
  clicks the existing "儲存快照" button.
- **`Portfolio/HoldingsEditor.tsx`**: update dialog's `DialogTitle` now also holds a "下載匯入範本"
  button and `<ImportCsvDialog onImport={(imported) => setRows((rs) => [...rs, ...imported])} />`,
  next to the existing per-section "新增" buttons pattern.
- **`Portfolio/portfolioExport.ts`**: `TYPE_LABEL` and `downloadCsv` exported (previously module-
  private) so `csvImport.ts` can reuse both without a second copy of the type-label map or a second
  CSV-download helper.

### Files Changed
- `jackyaiapp.client/src/utils/tabularParse.ts` (new, extracted)
- `jackyaiapp.client/src/utils/tabularParse.test.ts` (new)
- `jackyaiapp.client/src/DataAnalysis/components/UploadDataDialog.tsx` (imports from the extracted util)
- `jackyaiapp.client/src/Portfolio/portfolioExport.ts` (`TYPE_LABEL`/`downloadCsv` exported)
- `jackyaiapp.client/src/Portfolio/csvImport.ts` (new)
- `jackyaiapp.client/src/Portfolio/csvImport.test.ts` (new)
- `jackyaiapp.client/src/Portfolio/ImportCsvDialog.tsx` (new)
- `jackyaiapp.client/src/Portfolio/ImportCsvDialog.test.tsx` (new)
- `jackyaiapp.client/src/Portfolio/HoldingsEditor.tsx` (new entry points in the update dialog)

### Tests
- `npm run lint` — 0 warnings/errors.
- `npm run build` (tsc + vite build) — passes.
- `npx vitest run` — 182/182 passed (22 files), no regressions; includes the new
  `tabularParse.test.ts`, `csvImport.test.ts` (mapping/validation/template-round-trip), and
  `ImportCsvDialog.test.tsx` (`renderToStaticMarkup` pattern, per `TASK-014`'s precedent — no
  `@testing-library/react` in this project, so interactive open/upload/click flows aren't covered
  by an automated test; the pure mapping/validation logic and the template round-trip are, which is
  where the actual row-shape correctness risk is).
- Manual browser verification (open the editor, download the template, re-upload it, confirm rows
  land in the right sections, try a CSV with one bad row) was not performed in this session — see
  Known Issues.

### Known Issues
- Not manually verified in a running browser this session (no dev server / browser check performed)
  — same limitation as `TASK-014`. The template round-trip is covered by an automated test
  (`csvImport.test.ts`), which reduces but doesn't eliminate the risk a manual pass would catch.
- STOCK/FUND rows imported by symbol are not re-validated against the live search API (matches the
  Architecture doc's stated, deliberate trade-off — same as `UpdateHistoryDialog`'s existing
  snapshot-load path).

### Blockers
None.

## Review
(Fill in after PR review.)
