import { makeRow, rowError, toRow } from './editorTypes';
import { downloadCsv, TYPE_LABEL } from './portfolioExport';

import { AssetType, Market, PositionInput, ValuationMethod } from '@/apis/portfolioApis/types';

const ASSET_TYPES: readonly AssetType[] = ['STOCK', 'CASH', 'FUND', 'LIABILITY', 'REAL_ESTATE'];

// Reverse of portfolioExport's TYPE_LABEL, so the import column accepts either the raw
// AssetType value or its existing Chinese label without a second copy of the mapping.
const LABEL_TO_TYPE: Record<string, AssetType> = Object.fromEntries(
  Object.entries(TYPE_LABEL).map(([type, label]) => [label, type as AssetType]),
);

function parseAssetType(raw: string): AssetType | null {
  const v = raw.trim();
  if ((ASSET_TYPES as string[]).includes(v)) return v as AssetType;
  return LABEL_TO_TYPE[v] ?? null;
}

/** A positive, finite number, or null when the input is blank/unparseable/non-positive. */
function parsePositiveNumber(raw: string): number | null {
  const v = raw.trim();
  if (!v) return null;
  const n = Number(v);
  return Number.isFinite(n) && n > 0 ? n : null;
}

export interface ParsedImportRow {
  raw: Record<string, string>;
  position: PositionInput | null;
  /** null when the row mapped to a valid, importable position. */
  error: string | null;
}

/**
 * Maps one parsed CSV row (header-keyed, from `tabularParse`) to a `PositionInput` per the
 * import column schema, or reports why the row can't be imported. A row that maps cleanly is
 * still re-validated through the editor's own `rowError` so import can never produce a row the
 * manual editor itself would consider invalid.
 */
export function parseImportRow(raw: Record<string, string>): ParsedImportRow {
  const typeRaw = (raw['類型'] ?? '').trim();
  const assetType = parseAssetType(typeRaw);
  if (!assetType) {
    return { raw, position: null, error: `無法辨識類型「${typeRaw}」` };
  }

  const market: Market = (raw['市場'] ?? '').trim().toUpperCase() === 'US' ? 'US' : 'TW';
  // Reuse makeRow's own per-type defaults (name, market) instead of a second copy of them.
  const scaffold = makeRow(assetType, market);
  const hasSymbol = assetType === 'STOCK' || assetType === 'FUND';

  const symbol = (raw['代號'] ?? '').trim();
  if (hasSymbol && !symbol) {
    return { raw, position: null, error: '缺少代號' };
  }

  const name = (raw['名稱'] ?? '').trim() || scaffold.name;
  const currency = (raw['計價幣別'] ?? '').trim() || 'TWD';
  const costCurrency = (raw['成本幣別'] ?? '').trim() || currency;

  const quantity = parsePositiveNumber(raw['數量'] ?? '');
  if (quantity === null) {
    return { raw, position: null, error: '數量須為正數' };
  }

  const costBasisRaw = (raw['成本'] ?? '').trim();
  let costBasis: number | null = null;
  if (costBasisRaw) {
    const n = Number(costBasisRaw);
    if (!Number.isFinite(n)) {
      return { raw, position: null, error: '成本格式錯誤' };
    }
    costBasis = n;
  }

  let valuationMethod: ValuationMethod | undefined;
  if (assetType === 'REAL_ESTATE') {
    valuationMethod = (raw['估價方式'] ?? '').trim() === '買價扣除折舊' ? '買價扣除折舊' : '市價';
  }

  const position: PositionInput = {
    assetType,
    symbol: hasSymbol ? symbol : '',
    name,
    market: scaffold.market,
    currency,
    costCurrency: hasSymbol ? costCurrency : undefined,
    quantity,
    costBasis: hasSymbol ? costBasis : undefined,
    valuationMethod,
  };

  const editorError = rowError(toRow(position));
  if (editorError) {
    return { raw, position: null, error: editorError };
  }

  return { raw, position, error: null };
}

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
  ['股票', '2330', '台積電', 'TW', 'TWD', 'TWD', 1000, 600000, ''],
  ['現金', '', '', '', 'TWD', '', 50000, '', ''],
];

/** Downloads a template CSV whose headers exactly match what `parseImportRow` expects, with one
 * STOCK and one CASH example row, so a user knows the exact column names without guessing. */
export function downloadImportTemplateCsv() {
  downloadCsv([IMPORT_TEMPLATE_HEADERS, ...IMPORT_TEMPLATE_ROWS], 'portfolio_import_template.csv');
}
