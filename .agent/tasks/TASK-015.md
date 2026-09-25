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

**PR**: https://github.com/twjackysu/JackyAIApp/pull/370

Implemented as scoped in `ARCHITECTURE.md`'s Decision — frontend-only, no
backend/DB change.

- **`utils/tabularParse.ts`** (new): `detectSep`/`parseLine`/`parseTabular`
  extracted out of `DataAnalysis/components/UploadDataDialog.tsx`
  (behavior-preserving refactor; `UploadDataDialog` now imports from the
  shared util instead of its own local copy).
- **`Portfolio/csvImport.ts`** (new): pure `parseImportRow(raw)` maps one
  header-keyed CSV row (from `tabularParse`) to a `PositionInput` per the
  new import schema, or an error message.
  - `類型` accepts either the raw `AssetType` value or its `TYPE_LABEL`
    Chinese label — reverse-looked-up from `portfolioExport.ts`'s existing
    map (now exported), not hardcoded a second time.
  - `名稱`/`市場` fall back to `editorTypes.ts`'s `makeRow` per-type
    defaults when blank (reused directly, not duplicated).
  - `成本` maps to `PositionInput.CostBasis` exactly — total, unconverted,
    in `成本幣別` — deliberately distinct from the export CSV's
    TWD-converted `成本(TWD)` figure (see Architecture doc's Investigation).
  - `成本幣別` defaults to `計價幣別`; `計價幣別` defaults to `TWD`
    (matching `SaveHoldingsAsync`'s own server-side default).
  - Every mapped row is re-validated through the editor's own `rowError()`
    (from `editorTypes.ts`) before being accepted, so import can never
    produce a row the manual editor itself would reject.
  - `downloadImportTemplateCsv()` — one STOCK + one CASH example row,
    headers matching exactly what `parseImportRow` reads; round-tripped
    through `parseImportRow` in `csvImport.test.ts` as a direct check.
- **`Portfolio/ImportCsvDialog.tsx`** (new): file-picker sub-dialog — pick a
  `.csv` → parse via `tabularParse` → map via `parseImportRow` → preview
  (row count, valid/invalid split, first 5 rows with per-row status) →
  "加入" appends only the valid rows (`.map(toRow)`) into
  `HoldingsEditor`'s existing `rows` state. Invalid rows are excluded from
  "加入" by default with a visible "N 筆略過，欄位有誤" count rather than
  silently importing as valid. Never calls `useSaveHoldingsMutation`
  directly — the user still reviews in the normal editor sections and must
  click the existing "儲存快照" button to persist.
- **`Portfolio/HoldingsEditor.tsx`**: new "下載匯入範本" / "匯入 CSV"
  buttons in the update dialog's title bar (next to each other, per the
  requirement).
- **`Portfolio/portfolioExport.ts`**: exported `TYPE_LABEL` and
  `downloadCsv` (previously module-private) so `csvImport.ts` can reuse
  both instead of duplicating them.

### Files Changed
- `jackyaiapp.client/src/utils/tabularParse.ts` (new)
- `jackyaiapp.client/src/utils/tabularParse.test.ts` (new)
- `jackyaiapp.client/src/DataAnalysis/components/UploadDataDialog.tsx`
- `jackyaiapp.client/src/Portfolio/csvImport.ts` (new)
- `jackyaiapp.client/src/Portfolio/csvImport.test.ts` (new)
- `jackyaiapp.client/src/Portfolio/ImportCsvDialog.tsx` (new)
- `jackyaiapp.client/src/Portfolio/HoldingsEditor.tsx`
- `jackyaiapp.client/src/Portfolio/portfolioExport.ts`

### UI / UX
- "下載匯入範本" / "匯入 CSV" buttons sit in the update dialog's title bar,
  wrapping on narrow screens.
- Import sub-dialog preview table shows 類型/代號/名稱/數量/狀態 for the
  first 5 parsed rows, plus a summary line with total/valid/skipped counts.
- Imported STOCK/FUND rows land in `rows` state with `symbol` already set,
  so `StockRow`/`FundRow` render them as picked chips immediately (no live
  search dependency), confirming the architecture doc's assumption.

### Tests
- `npm run lint` — 0 warnings/errors.
- `npm run build` (tsc + vite build) — passes.
- `npx vitest run` — 166/166 passed (21 files), no regressions. New:
  `tabularParse.test.ts` (separator detection, quoted-CSV parsing,
  header-keyed row output) and `csvImport.test.ts` (schema mapping per
  field — type label/raw value, required 代號 for STOCK/FUND, per-type name
  defaults, positive-number validation for 數量, 成本 parse-or-reject,
  currency defaulting, 估價方式 default, and a template round-trip).
- No `@testing-library/react` in this project, and MUI `Dialog` renders via
  a portal so it can't be captured with `renderToStaticMarkup` either (a
  `ImportCsvDialog.test.tsx` attempt returned empty markup for the open
  case) — the pure row-mapping/validation logic, which is where the real
  correctness risk lives, is unit-tested directly instead, same convention
  as `Portfolio/concentration.ts`/`exposureGauges.ts`.
- Manual browser verification (open the editor, download the template,
  re-upload it, confirm the preview and that a bad row is excluded) was
  not performed in this automated session — see Known Issues.

### Known Issues
- Not manually verified in a running browser this session — only
  build/lint/unit-test verified, plus a template-round-trip unit test.
  Worth a quick manual pass before/at QA.
- STOCK/FUND rows imported by symbol are not validated against the live
  stock/fund search — a typo'd symbol saves as-is. Deliberate, matches
  `UpdateHistoryDialog`'s existing snapshot-load behavior (Architecture
  doc's Trade-offs), not a gap.

### Blockers
None.

## Review
(Fill in after PR review.)
