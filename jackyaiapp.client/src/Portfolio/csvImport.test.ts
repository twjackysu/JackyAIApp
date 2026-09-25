import { describe, expect, it } from 'vitest';

import { IMPORT_TEMPLATE_HEADERS, IMPORT_TEMPLATE_ROWS, parseImportRow } from './csvImport';

function row(overrides: Record<string, string>): Record<string, string> {
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

describe('parseImportRow', () => {
  it('maps a STOCK row using the raw AssetType value', () => {
    const result = parseImportRow(
      row({
        類型: 'STOCK',
        代號: '2330',
        名稱: '台積電',
        市場: 'TW',
        數量: '1000',
        成本: '600000',
      }),
    );
    expect(result.error).toBeNull();
    expect(result.position).toEqual({
      assetType: 'STOCK',
      symbol: '2330',
      name: '台積電',
      market: 'TW',
      currency: 'TWD',
      costCurrency: 'TWD',
      quantity: 1000,
      costBasis: 600000,
      valuationMethod: undefined,
    });
  });

  it('maps a STOCK row using the Chinese TYPE_LABEL', () => {
    const result = parseImportRow(row({ 類型: '股票', 代號: 'AAPL', 市場: 'US', 數量: '10' }));
    expect(result.error).toBeNull();
    expect(result.position?.assetType).toBe('STOCK');
    expect(result.position?.market).toBe('US');
  });

  it('rejects an unrecognized 類型', () => {
    const result = parseImportRow(row({ 類型: '虛擬貨幣', 數量: '1' }));
    expect(result.position).toBeNull();
    expect(result.error).toContain('無法辨識類型');
  });

  it('requires 代號 for STOCK/FUND', () => {
    const stock = parseImportRow(row({ 類型: 'STOCK', 數量: '10' }));
    expect(stock.position).toBeNull();
    expect(stock.error).toBe('缺少代號');

    const fund = parseImportRow(row({ 類型: 'FUND', 數量: '10' }));
    expect(fund.position).toBeNull();
    expect(fund.error).toBe('缺少代號');
  });

  it('does not require 代號 for CASH/LIABILITY/REAL_ESTATE and falls back to the editor default name', () => {
    const cash = parseImportRow(row({ 類型: 'CASH', 數量: '50000' }));
    expect(cash.error).toBeNull();
    expect(cash.position).toMatchObject({
      assetType: 'CASH',
      symbol: '',
      name: '現金',
      quantity: 50000,
    });

    const liability = parseImportRow(row({ 類型: 'LIABILITY', 數量: '10000' }));
    expect(liability.position).toMatchObject({ name: '信貸' });

    const realEstate = parseImportRow(row({ 類型: 'REAL_ESTATE', 數量: '1' }));
    expect(realEstate.position).toMatchObject({ name: '不動產', valuationMethod: '市價' });
  });

  it('rejects a missing or non-positive 數量', () => {
    expect(parseImportRow(row({ 類型: 'CASH', 數量: '' })).error).toBe('數量須為正數');
    expect(parseImportRow(row({ 類型: 'CASH', 數量: '0' })).error).toBe('數量須為正數');
    expect(parseImportRow(row({ 類型: 'CASH', 數量: '-5' })).error).toBe('數量須為正數');
    expect(parseImportRow(row({ 類型: 'CASH', 數量: 'abc' })).error).toBe('數量須為正數');
  });

  it('rejects an unparsable 成本 without rejecting a blank one', () => {
    const badCost = parseImportRow(row({ 類型: 'STOCK', 代號: '2330', 數量: '1', 成本: 'abc' }));
    expect(badCost.error).toBe('成本格式錯誤');

    const noCost = parseImportRow(row({ 類型: 'STOCK', 代號: '2330', 數量: '1' }));
    expect(noCost.error).toBeNull();
    expect(noCost.position?.costBasis).toBeNull();
  });

  it('defaults 計價幣別 to TWD and 成本幣別 to 計價幣別 when blank', () => {
    const result = parseImportRow(row({ 類型: 'STOCK', 代號: '2330', 數量: '1' }));
    expect(result.position?.currency).toBe('TWD');
    expect(result.position?.costCurrency).toBe('TWD');

    const foreign = parseImportRow(
      row({ 類型: 'STOCK', 代號: 'AAPL', 市場: 'US', 數量: '1', 計價幣別: 'USD' }),
    );
    expect(foreign.position?.currency).toBe('USD');
    expect(foreign.position?.costCurrency).toBe('USD');
  });

  it('reads a custom 估價方式 for REAL_ESTATE, defaulting to 市價', () => {
    const custom = parseImportRow(
      row({ 類型: 'REAL_ESTATE', 數量: '1', 估價方式: '買價扣除折舊' }),
    );
    expect(custom.position?.valuationMethod).toBe('買價扣除折舊');

    const blank = parseImportRow(row({ 類型: 'REAL_ESTATE', 數量: '1' }));
    expect(blank.position?.valuationMethod).toBe('市價');
  });

  it('exposes template headers matching what parseImportRow reads', () => {
    expect(IMPORT_TEMPLATE_HEADERS).toEqual([
      '類型',
      '代號',
      '名稱',
      '市場',
      '計價幣別',
      '成本幣別',
      '數量',
      '成本',
      '估價方式',
    ]);
  });

  it('round-trips the downloadable template rows back through parseImportRow with no errors', () => {
    for (const values of IMPORT_TEMPLATE_ROWS) {
      const raw = Object.fromEntries(
        IMPORT_TEMPLATE_HEADERS.map((h, i) => [h, String(values[i] ?? '')]),
      );
      const result = parseImportRow(raw);
      expect(result.error).toBeNull();
      expect(result.position).not.toBeNull();
    }
  });
});
