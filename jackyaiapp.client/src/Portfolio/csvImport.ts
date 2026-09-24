import { EditorRow, rowError, toRow } from './editorTypes';
import { downloadCsv, TYPE_LABEL } from './portfolioExport';

import { AssetType, PositionInput } from '@/apis/portfolioApis/types';
import { parseTabular } from '@/utils/tabularParse';

/**
 * Purpose-built import column schema — distinct from `portfolioExport.ts`'s export schema.
 * The export CSV is a computed report (TWD-converted market value/cost/P&L); round-tripping it
 * would silently corrupt cost-basis data for foreign-currency holdings. See ARCHITECTURE.md →
 * "Decision: Portfolio CSV import for holdings — Gap Audit item 2".
 */
export const IMPORT_TEMPLATE_HEADERS = [
  '類型',
  '代號',
  '名稱',
  '市場',
  '計價幣別',
  '成本幣別',
  '數量',
  '成本',
  '估價方式',
];

export const IMPORT_TEMPLATE_ROWS: (string | number)[][] = [
  ['STOCK', '2330', '台積電', 'TW', 'TWD', 'TWD', 1000, 550000, ''],
  ['CASH', '', '現金', '', 'TWD', '', 100000, '', ''],
];

const ASSET_TYPES: AssetType[] = ['STOCK', 'CASH', 'FUND', 'LIABILITY', 'REAL_ESTATE'];

// Reverse of TYPE_LABEL so a Chinese label in the CSV resolves back to its AssetType — reusing
// the one existing mapping instead of hardcoding a second copy of it (see TASK-015).
const LABEL_TO_TYPE = Object.fromEntries(
  Object.entries(TYPE_LABEL).map(([type, label]) => [label, type]),
) as Record<string, AssetType>;

const DEFAULT_NAME: Partial<Record<AssetType, string>> = {
  CASH: '現金',
  LIABILITY: '信貸',
  REAL_ESTATE: '不動產',
};

function parseAssetType(raw: string): AssetType | null {
  const v = raw.trim();
  if (!v) return null;
  const upper = v.toUpperCase();
  if ((ASSET_TYPES as string[]).includes(upper)) return upper as AssetType;
  return LABEL_TO_TYPE[v] ?? null;
}

/** `invalid` is true when a non-blank value couldn't be parsed as a number at all. */
function parseNumber(raw: string | undefined): { value: number | null; invalid: boolean } {
  const trimmed = (raw ?? '').trim();
  if (!trimmed) return { value: null, invalid: false };
  const n = Number(trimmed);
  return Number.isFinite(n) ? { value: n, invalid: false } : { value: null, invalid: true };
}

export interface ImportRowResult {
  /** The raw parsed CSV row, for the preview table. */
  row: Record<string, string>;
  /** Best-effort mapped position — populated even when `error` is set, so the preview can still
   * show what was parsed; only `error === null` rows are eligible for "加入". */
  position: PositionInput | null;
  error: string | null;
}

/**
 * Maps one parsed CSV row to a `PositionInput`. Reuses `rowError` (the same validation every
 * manually-typed editor row goes through) so an imported row is held to exactly the same bar —
 * no second, parallel validation system to keep in sync.
 */
export function mapImportRow(row: Record<string, string>): ImportRowResult {
  const assetType = parseAssetType(row['類型'] ?? '');
  if (!assetType) {
    return { row, position: null, error: `類型未知或缺漏：「${(row['類型'] ?? '').trim()}」` };
  }

  const costBasis = parseNumber(row['成本']);
  if (costBasis.invalid) {
    return { row, position: null, error: `成本格式錯誤：「${row['成本']}」` };
  }

  const symbol = (row['代號'] ?? '').trim();
  const name = (row['名稱'] ?? '').trim() || DEFAULT_NAME[assetType] || '';
  const market =
    assetType === 'STOCK'
      ? (row['市場'] ?? '').trim().toUpperCase() === 'US'
        ? 'US'
        : 'TW'
      : assetType === 'FUND'
        ? 'FUND'
        : '';
  const currency = (row['計價幣別'] ?? '').trim() || 'TWD';
  const isPriced = assetType === 'STOCK' || assetType === 'FUND';
  const costCurrency = isPriced ? (row['成本幣別'] ?? '').trim() || currency : undefined;
  const quantity = parseNumber(row['數量']);
  const valuationMethodRaw = (row['估價方式'] ?? '').trim();
  const valuationMethod =
    assetType === 'REAL_ESTATE'
      ? valuationMethodRaw === '買價扣除折舊'
        ? '買價扣除折舊'
        : '市價'
      : undefined;

  const position: PositionInput = {
    assetType,
    symbol,
    name,
    market,
    currency,
    costCurrency,
    quantity: quantity.value ?? 0,
    costBasis: isPriced ? costBasis.value : undefined,
    valuationMethod,
  };

  const editorRow: EditorRow = toRow(position);
  return { row, position, error: rowError(editorRow) };
}

export interface ParsedImportCsv {
  headers: string[];
  results: ImportRowResult[];
}

export function parseImportCsv(text: string): ParsedImportCsv | null {
  const parsed = parseTabular(text);
  if (!parsed) return null;
  return { headers: parsed.headers, results: parsed.rows.map(mapImportRow) };
}

/** Downloads a template CSV whose headers exactly match what {@link parseImportCsv} expects. */
export function downloadImportTemplate() {
  downloadCsv([IMPORT_TEMPLATE_HEADERS, ...IMPORT_TEMPLATE_ROWS], 'portfolio_import_template.csv');
}
