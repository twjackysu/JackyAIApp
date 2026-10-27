import { EditorRow, rowError, toRow } from './editorTypes';
import { MAX_POSITIONS, OVER_POSITIONS_LIMIT } from './holdingsLimits';
import { downloadCsv, TYPE_LABEL } from './portfolioExport';

import { AssetType, PositionInput } from '@/apis/portfolioApis/types';

/**
 * Import column schema — deliberately distinct from `portfolioExport.ts`'s export schema.
 * `成本` here is `PositionInput.CostBasis` verbatim: a total, unconverted, in `成本幣別` — never
 * the export's TWD-converted `成本(TWD)` figure.
 */
export const IMPORT_HEADERS = [
  '類型',
  '代號',
  '名稱',
  '市場',
  '計價幣別',
  '成本幣別',
  '數量',
  '成本',
  '估價方式',
] as const;

const ASSET_TYPES: AssetType[] = ['STOCK', 'CASH', 'FUND', 'LIABILITY', 'REAL_ESTATE'];

const LABEL_TO_TYPE: Record<string, AssetType> = Object.fromEntries(
  Object.entries(TYPE_LABEL).map(([type, label]) => [label, type as AssetType]),
);

// Same per-type defaults makeRow() falls back to — kept here rather than re-derived so a blank
// 名稱 column lands on an identical row to a freshly-added one.
const DEFAULT_NAME: Partial<Record<AssetType, string>> = {
  CASH: '現金',
  LIABILITY: '信貸',
  REAL_ESTATE: '不動產',
};

const VALUATION_METHODS = ['市價', '買價扣除折舊'];

function parseAssetType(raw: string): AssetType | null {
  const trimmed = raw.trim();
  if (!trimmed) return null;
  const upper = trimmed.toUpperCase() as AssetType;
  if (ASSET_TYPES.includes(upper)) return upper;
  return LABEL_TO_TYPE[trimmed] ?? null;
}

export interface ImportRowResult {
  position: PositionInput | null;
  /** Set only when the row is unrecoverable (unrecognized type, non-numeric 成本, bad 估價方式) —
   * quantity/symbol gaps are left for the shared `rowError()` check instead of being duplicated
   * here. */
  error: string | null;
}

/** Maps one parsed CSV row (header-keyed, from `parseTabular`) to a `PositionInput` candidate. */
export function mapImportRow(row: Record<string, string>): ImportRowResult {
  const typeRaw = row['類型'] ?? '';
  const assetType = parseAssetType(typeRaw);
  if (!assetType) {
    return { position: null, error: typeRaw.trim() ? `無法辨識的類型：${typeRaw}` : '缺少類型' };
  }

  const symbol = (row['代號'] ?? '').trim();
  const market =
    assetType === 'STOCK'
      ? (row['市場'] ?? '').trim().toUpperCase() === 'US'
        ? 'US'
        : 'TW'
      : assetType === 'FUND'
        ? 'FUND'
        : '';

  const currency = (row['計價幣別'] ?? '').trim() || 'TWD';
  const costCurrency = (row['成本幣別'] ?? '').trim() || currency;
  const name = (row['名稱'] ?? '').trim() || DEFAULT_NAME[assetType] || '';

  const quantityRaw = (row['數量'] ?? '').trim();
  const quantity = quantityRaw === '' ? 0 : Number(quantityRaw);

  const costRaw = (row['成本'] ?? '').trim();
  if (costRaw && !Number.isFinite(Number(costRaw))) {
    return { position: null, error: `成本欄位不是數字：${costRaw}` };
  }
  const costBasis = costRaw ? Number(costRaw) : null;

  let valuationMethod: string | undefined;
  if (assetType === 'REAL_ESTATE') {
    const raw = (row['估價方式'] ?? '').trim();
    if (!raw) valuationMethod = '市價';
    else if (VALUATION_METHODS.includes(raw)) valuationMethod = raw;
    else return { position: null, error: `估價方式須為 市價 或 買價扣除折舊：${raw}` };
  }

  const position: PositionInput = {
    assetType,
    symbol,
    name,
    market,
    currency,
    costCurrency,
    quantity: Number.isFinite(quantity) ? quantity : 0,
    costBasis,
    ...(valuationMethod !== undefined ? { valuationMethod } : {}),
  };

  return { position, error: null };
}

export interface ImportPreviewRow {
  raw: Record<string, string>;
  /** Non-null only when the row is fully valid and ready to append to the editor. */
  editorRow: EditorRow | null;
  error: string | null;
}

/** The preview's rows: the first `limit`, then every row that will be left out wherever it is, each
 * with its line in the file (the header is line 1), so a reader can find and fix it. */
export function previewRowsShown(preview: ImportPreviewRow[], limit: number) {
  return preview
    .map((row, i) => ({ row, line: i + 2 }))
    .filter(({ row }, i) => i < limit || !row.editorRow);
}

/** Maps + validates a parsed CSV's rows, reusing `toRow`/`rowError` as-is so an imported row is
 * held to exactly the same rules as a manually-typed one. `room` is how many rows the editor can still
 * take before one save holds more than the server's `MAX_POSITIONS`: valid rows past it are marked, each
 * by itself, rather than the save failing later for all of them. */
export function buildImportPreview(
  rows: Record<string, string>[],
  room: number = MAX_POSITIONS,
): ImportPreviewRow[] {
  let accepted = 0;
  return rows.map((raw) => {
    const { position, error } = mapImportRow(raw);
    if (!position) return { raw, editorRow: null, error };

    const editorRow = toRow(position);
    const editorErr = rowError(editorRow);
    if (editorErr) return { raw, editorRow: null, error: editorErr };

    if (accepted >= room) return { raw, editorRow: null, error: OVER_POSITIONS_LIMIT };
    accepted++;
    return { raw, editorRow, error: null };
  });
}

/** Downloads a template CSV with the exact headers `mapImportRow` expects, plus one STOCK and one
 * CASH example row. */
export function downloadImportTemplateCsv() {
  const rows: (string | number)[][] = [
    [...IMPORT_HEADERS],
    [TYPE_LABEL.STOCK, '2330', '台積電', 'TW', 'TWD', 'TWD', 1000, 550000, ''],
    [TYPE_LABEL.CASH, '', '現金', '', 'TWD', '', 100000, '', ''],
  ];
  downloadCsv(rows, 'portfolio_import_template.csv');
}
