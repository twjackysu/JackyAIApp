import { makeRow } from './editorTypes';
import { TYPE_LABEL } from './portfolioExport';

import { AssetType, PositionInput } from '@/apis/portfolioApis/types';

const ASSET_TYPES: AssetType[] = ['STOCK', 'CASH', 'FUND', 'LIABILITY', 'REAL_ESTATE'];

function resolveAssetType(raw: string): AssetType | null {
  const upper = raw.toUpperCase();
  if ((ASSET_TYPES as string[]).includes(upper)) return upper as AssetType;
  const entry = Object.entries(TYPE_LABEL).find(([, label]) => label === raw);
  return (entry?.[0] as AssetType | undefined) ?? null;
}

const cell = (row: Record<string, string>, key: string): string => (row[key] ?? '').trim();

export interface CsvRowError {
  row: Record<string, string>;
  error: string;
}

/**
 * Maps one header-keyed CSV row (see `portfolioExport.ts`'s `downloadPortfolioImportTemplateCsv`
 * for the exact column schema) to a `PositionInput`, or a human-readable validation error.
 *
 * This is a purpose-built **import** schema, distinct from the export CSV's computed/TWD-converted
 * report columns — see `ARCHITECTURE.md` → "Portfolio CSV import for holdings" for why the two
 * cannot be unified.
 */
export function mapCsvRowToPosition(row: Record<string, string>): PositionInput | CsvRowError {
  const assetType = resolveAssetType(cell(row, '類型'));
  if (!assetType) {
    return { row, error: `無法辨識的類型：${cell(row, '類型') || '(空白)'}` };
  }

  const needsSymbol = assetType === 'STOCK' || assetType === 'FUND';
  const symbol = cell(row, '代號');
  if (needsSymbol && !symbol) {
    return { row, error: '缺少代號' };
  }

  const quantityRaw = cell(row, '數量');
  const quantity = parseFloat(quantityRaw);
  if (!quantityRaw || !Number.isFinite(quantity) || quantity <= 0) {
    return { row, error: '數量必須為正數' };
  }

  const costBasisRaw = cell(row, '成本');
  const costBasis = costBasisRaw ? parseFloat(costBasisRaw) : null;
  if (costBasisRaw && !Number.isFinite(costBasis)) {
    return { row, error: '成本必須為數字' };
  }

  const market =
    assetType === 'STOCK' ? (cell(row, '市場').toUpperCase() === 'US' ? 'US' : 'TW') : '';
  // Same per-type defaults `makeRow` already uses (e.g. 現金/信貸/不動產 for the amount-only types),
  // reused here rather than duplicated so the two can't drift apart.
  const defaults = makeRow(assetType, market || 'TW');

  const position: PositionInput = {
    assetType,
    symbol: needsSymbol ? symbol : '',
    name: cell(row, '名稱') || defaults.name,
    market: assetType === 'FUND' ? 'FUND' : market,
    // Deliberately a flat TWD default regardless of market, matching `SaveHoldingsAsync`'s own
    // server-side default — not `makeRow`'s market-dependent currency default.
    currency: cell(row, '計價幣別') || 'TWD',
    quantity,
  };

  if (needsSymbol) {
    position.costCurrency = cell(row, '成本幣別') || position.currency;
    position.costBasis = costBasis;
  }
  if (assetType === 'REAL_ESTATE') {
    position.valuationMethod = cell(row, '估價方式') || defaults.valuationMethod;
  }

  return position;
}

export interface CsvImportResult {
  valid: PositionInput[];
  invalid: CsvRowError[];
}

export function mapCsvRows(rows: Record<string, string>[]): CsvImportResult {
  const valid: PositionInput[] = [];
  const invalid: CsvRowError[] = [];
  for (const row of rows) {
    const result = mapCsvRowToPosition(row);
    if ('error' in result) invalid.push(result);
    else valid.push(result);
  }
  return { valid, invalid };
}
