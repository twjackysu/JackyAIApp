import { describe, expect, it } from 'vitest';

import {
  IMPORT_TEMPLATE_HEADERS,
  IMPORT_TEMPLATE_ROWS,
  mapImportRow,
  parseImportCsv,
} from './csvImport';

function row(overrides: Partial<Record<string, string>>): Record<string, string> {
  return {
    類型: '',
    代號: '',
    名稱: '',
    市場: '',
    計價幣別: '',
    成本幣別: '',
    數量: '',
    成本: '',
    估價方式: '',
    ...overrides,
  };
}

describe('mapImportRow', () => {
  it('accepts either the raw AssetType or its Chinese TYPE_LABEL', () => {
    const byRaw = mapImportRow(row({ 類型: 'STOCK', 代號: '2330', 數量: '100' }));
    const byLabel = mapImportRow(row({ 類型: '股票', 代號: '2330', 數量: '100' }));
    expect(byRaw.error).toBeNull();
    expect(byLabel.error).toBeNull();
    expect(byRaw.position?.assetType).toBe('STOCK');
    expect(byLabel.position?.assetType).toBe('STOCK');
  });

  it('flags an unknown/missing type instead of silently dropping the row', () => {
    const result = mapImportRow(row({ 類型: '虛擬貨幣', 代號: 'BTC', 數量: '1' }));
    expect(result.error).toMatch(/類型未知或缺漏/);
    expect(result.position).toBeNull();
  });

  it('requires 代號 for STOCK/FUND but not for CASH/LIABILITY/REAL_ESTATE', () => {
    const stockNoSymbol = mapImportRow(row({ 類型: 'STOCK', 數量: '100' }));
    expect(stockNoSymbol.error).not.toBeNull();

    const cash = mapImportRow(row({ 類型: 'CASH', 數量: '50000' }));
    expect(cash.error).toBeNull();
    expect(cash.position?.symbol).toBe('');
  });

  it('falls back to the per-type default name when 名稱 is blank', () => {
    const cash = mapImportRow(row({ 類型: 'CASH', 數量: '1000' }));
    expect(cash.position?.name).toBe('現金');
    const liability = mapImportRow(row({ 類型: 'LIABILITY', 數量: '1000' }));
    expect(liability.position?.name).toBe('信貸');
    const realEstate = mapImportRow(row({ 類型: 'REAL_ESTATE', 數量: '1000' }));
    expect(realEstate.position?.name).toBe('不動產');
  });

  it('defaults 市場 to TW and only treats an explicit US as US', () => {
    const defaulted = mapImportRow(row({ 類型: 'STOCK', 代號: '2330', 數量: '1' }));
    expect(defaulted.position?.market).toBe('TW');
    const us = mapImportRow(row({ 類型: 'STOCK', 代號: 'AAPL', 市場: 'US', 數量: '1' }));
    expect(us.position?.market).toBe('US');
  });

  it('defaults 計價幣別 to TWD and 成本幣別 to 計價幣別 when blank', () => {
    const result = mapImportRow(row({ 類型: 'STOCK', 代號: '2330', 數量: '1' }));
    expect(result.position?.currency).toBe('TWD');
    expect(result.position?.costCurrency).toBe('TWD');

    const withCostCurrency = mapImportRow(
      row({ 類型: 'STOCK', 代號: 'AAPL', 市場: 'US', 計價幣別: 'USD', 數量: '1' }),
    );
    expect(withCostCurrency.position?.costCurrency).toBe('USD');
  });

  it('requires a positive 數量 (missing, zero, or non-numeric all fail)', () => {
    expect(mapImportRow(row({ 類型: 'CASH' })).error).not.toBeNull();
    expect(mapImportRow(row({ 類型: 'CASH', 數量: '0' })).error).not.toBeNull();
    expect(mapImportRow(row({ 類型: 'CASH', 數量: 'abc' })).error).not.toBeNull();
    expect(mapImportRow(row({ 類型: 'CASH', 數量: '1000' })).error).toBeNull();
  });

  it('treats 成本 as an optional, unconverted total (PositionInput.costBasis exactly)', () => {
    const noCost = mapImportRow(row({ 類型: 'STOCK', 代號: '2330', 數量: '100' }));
    expect(noCost.position?.costBasis).toBeNull();

    const withCost = mapImportRow(row({ 類型: 'STOCK', 代號: '2330', 數量: '100', 成本: '55000' }));
    expect(withCost.position?.costBasis).toBe(55000);

    const badCost = mapImportRow(row({ 類型: 'STOCK', 代號: '2330', 數量: '100', 成本: 'n/a' }));
    expect(badCost.error).toMatch(/成本格式錯誤/);
  });

  it('defaults 估價方式 to 市價 for REAL_ESTATE and ignores it for other types', () => {
    const defaulted = mapImportRow(row({ 類型: 'REAL_ESTATE', 數量: '1' }));
    expect(defaulted.position?.valuationMethod).toBe('市價');
    const depreciated = mapImportRow(
      row({ 類型: 'REAL_ESTATE', 數量: '1', 估價方式: '買價扣除折舊' }),
    );
    expect(depreciated.position?.valuationMethod).toBe('買價扣除折舊');
    const cash = mapImportRow(row({ 類型: 'CASH', 數量: '1' }));
    expect(cash.position?.valuationMethod).toBeUndefined();
  });
});

describe('parseImportCsv', () => {
  it('returns null when the text has no header + data row pair', () => {
    expect(parseImportCsv('')).toBeNull();
    expect(parseImportCsv('類型,代號')).toBeNull();
  });

  it('maps a mix of valid and invalid rows without one failure blocking the others', () => {
    const csv = [
      IMPORT_TEMPLATE_HEADERS.join(','),
      'STOCK,2330,台積電,TW,TWD,TWD,100,55000,',
      'STOCK,,,,,,,,', // missing 代號/數量 — invalid, but must not block the others
    ].join('\n');

    const parsed = parseImportCsv(csv);
    expect(parsed?.results).toHaveLength(2);
    expect(parsed?.results[0].error).toBeNull();
    expect(parsed?.results[1].error).not.toBeNull();
  });

  it('round-trips the downloadable template with zero validation errors', () => {
    const csv = [
      IMPORT_TEMPLATE_HEADERS.join(','),
      ...IMPORT_TEMPLATE_ROWS.map((r) => r.join(',')),
    ].join('\n');

    const parsed = parseImportCsv(csv);
    expect(parsed?.results).toHaveLength(IMPORT_TEMPLATE_ROWS.length);
    for (const result of parsed?.results ?? []) {
      expect(result.error).toBeNull();
    }
  });
});
