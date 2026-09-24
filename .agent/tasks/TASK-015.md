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

Implemented exactly as scoped in `ARCHITECTURE.md` — frontend-only, no backend change.

- **`utils/tabularParse.ts`** (new): `detectSep`/`parseLine`/`parseTabular` extracted verbatim
  out of `DataAnalysis/components/UploadDataDialog.tsx`. `UploadDataDialog.tsx` now imports
  `parseTabular` from there instead of defining its own copy — behavior-preserving, confirmed via
  `npx vitest run` (no regressions in the existing suite) and a manual read of the diff (logic
  byte-identical, only moved).
- **`Portfolio/csvImport.ts`** (new): `mapCsvRowToPosition`/`mapCsvRows`, the purpose-built
  import-column-schema → `PositionInput` mapper described in the Architecture doc (distinct from
  `portfolioExport.ts`'s computed/TWD-converted export schema). Reuses `TYPE_LABEL` (now exported
  from `portfolioExport.ts`) for the Chinese-label reverse lookup and `makeRow` from
  `editorTypes.ts` for the per-type default name/valuation-method, per the task's anti-duplication
  notes — no second copy of either mapping. `計價幣別` defaults flatly to `TWD` regardless of
  market (matching `SaveHoldingsAsync`'s own default), not `makeRow`'s market-dependent default,
  per the schema's own explicit wording.
- **`Portfolio/csvImport.test.ts`** (new): unit tests for the pure mapping/validation logic
  (raw-value and Chinese-label `類型`, per-type defaults, REAL_ESTATE valuation-method default,
  and every required-field rejection case — unrecognized type, missing 代號, missing/zero/negative
  數量, non-numeric 成本), plus a `mapCsvRows` split test confirming one bad row doesn't drop the
  good ones.
- **`Portfolio/ImportCsvDialog.tsx`** (new): the "匯入 CSV" sub-dialog — file picker
  (`accept=".csv"`, `FileReader.readAsText`, same pattern as `UploadDataDialog`'s file tab) →
  `parseTabular` → `mapCsvRows` → preview table (valid/invalid split count, first ~5 rows, each
  row's parsed fields or its rejection reason) → "加入" calls `onImport(valid)` only; invalid rows
  are excluded from "加入" by default with a visible count, per the task's first accepted UX
  option. Does not call `useSaveHoldingsMutation` itself.
- **`Portfolio/portfolioExport.ts`**: exported `TYPE_LABEL` and `downloadCsv` (previously private
  to the module) and added `downloadPortfolioImportTemplateCsv()` — a template CSV (headers +
  1 STOCK/1 CASH example row) using the existing `downloadCsv` helper, per the task's explicit
  "reuse, don't write a second CSV builder" instruction.
- **`Portfolio/HoldingsEditor.tsx`**: added "下載匯入範本"/"匯入 CSV" buttons to the update
  dialog's title row; `handleImportCsv` appends `positions.map(toRow)` into the existing `rows`
  state (same as a manually-added row) — persisting still requires the existing "儲存快照" button,
  so a bad import is always reviewable/editable first. `ImportCsvDialog` renders as a second
  top-level `Dialog` (MUI's modal stack handles the nested-dialog z-ordering automatically, same as
  the existing `CostBasisInput` popover pattern already relies on for the outer edit dialog).

### Files Changed
- `jackyaiapp.client/src/utils/tabularParse.ts` (new)
- `jackyaiapp.client/src/DataAnalysis/components/UploadDataDialog.tsx`
- `jackyaiapp.client/src/Portfolio/csvImport.ts` (new)
- `jackyaiapp.client/src/Portfolio/csvImport.test.ts` (new)
- `jackyaiapp.client/src/Portfolio/ImportCsvDialog.tsx` (new)
- `jackyaiapp.client/src/Portfolio/portfolioExport.ts`
- `jackyaiapp.client/src/Portfolio/HoldingsEditor.tsx`

### Tests
- `npm run lint` — 0 warnings/errors (after `npx eslint --fix` for two Prettier formatting nits).
- `npm run build` (tsc + vite build) — passes.
- `npx vitest run` — 157/157 passed (20 test files, up from 147/19), including the 10 new
  `csvImport.test.ts` cases; no regressions elsewhere (`DataAnalysis` upload dialog's own behavior
  confirmed unchanged).
- Manually round-tripped the exact template CSV content (headers + both example rows) through
  `parseTabular` → `mapCsvRows` in a throwaway test run (not committed): 0 invalid, 2 valid —
  confirms the template's headers exactly match what the import path expects, per the acceptance
  criterion.
- No `@testing-library/react` in the project — `ImportCsvDialog` itself has no dedicated component
  test (its meaningful logic — the CSV→`PositionInput` mapping — is fully covered by
  `csvImport.test.ts`'s pure-function tests instead; a `renderToStaticMarkup` test of the dialog's
  empty initial state would only assert static JSX, same reasoning `TASK-007` used for
  `ReadingTestCard` having no dedicated test). Interactive file-picker/preview flow was not
  exercised in a browser this session — see Known Issues.

### Known Issues
- Not manually verified in a running browser this session (no dev server / browser check
  performed) — build/lint/unit-test + the template round-trip check above are the only
  verification. Worth a quick manual pass at QA: download the template, edit a row to be invalid
  (e.g. blank 數量), re-upload, confirm the preview excludes it with a visible count, and that
  "加入" lands the valid rows in the correct editor sections (STOCK→台股/美股 by 市場, FUND→基金,
  etc.) with symbol-set rows rendering as already-picked chips.
- STOCK/FUND rows imported by symbol are not re-validated against the live search APIs (same
  known, accepted trade-off as `UpdateHistoryDialog`'s snapshot-load path — see Architecture doc's
  Trade-offs section, not a gap introduced here).

### Blockers
None.

**PR**: https://github.com/twjackysu/JackyAIApp/pull/366

## Review
(Fill in after PR review.)
