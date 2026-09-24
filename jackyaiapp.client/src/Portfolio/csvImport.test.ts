import { describe, expect, it } from 'vitest';

import { mapCsvRowToPosition, mapCsvRows } from './csvImport';

describe('mapCsvRowToPosition', () => {
  it('maps a STOCK row using the raw AssetType value', () => {
    const result = mapCsvRowToPosition({
      類型: 'STOCK',
      代號: '2330',
      名稱: '台積電',
      市場: 'TW',
      計價幣別: 'TWD',
      成本幣別: 'TWD',
      數量: '1000',
      成本: '500000',
    });
    expect(result).toEqual({
      assetType: 'STOCK',
      symbol: '2330',
      name: '台積電',
      market: 'TW',
      currency: 'TWD',
      quantity: 1000,
      costCurrency: 'TWD',
      costBasis: 500000,
    });
  });

  it('maps a STOCK row using the Chinese TYPE_LABEL value', () => {
    const result = mapCsvRowToPosition({
      類型: '股票',
      代號: 'AAPL',
      名稱: '',
      市場: 'US',
      計價幣別: '',
      成本幣別: '',
      數量: '10',
      成本: '',
    });
    expect(result).toMatchObject({
      assetType: 'STOCK',
      symbol: 'AAPL',
      market: 'US',
      // Flat TWD default regardless of market — matches SaveHoldingsAsync's own default.
      currency: 'TWD',
      costCurrency: 'TWD',
      quantity: 10,
      costBasis: null,
    });
  });

  it('defaults CASH name/market/currency and ignores 代號/成本', () => {
    const result = mapCsvRowToPosition({
      類型: '現金',
      代號: 'irrelevant',
      名稱: '',
      市場: '',
      計價幣別: '',
      成本幣別: '',
      數量: '100000',
      成本: '999',
    });
    expect(result).toEqual({
      assetType: 'CASH',
      symbol: '',
      name: '現金',
      market: '',
      currency: 'TWD',
      quantity: 100000,
    });
  });

  it('defaults REAL_ESTATE valuationMethod to 市價 when blank', () => {
    const result = mapCsvRowToPosition({
      類型: '不動產',
      代號: '',
      名稱: '',
      市場: '',
      計價幣別: '',
      成本幣別: '',
      數量: '15000000',
      成本: '',
      估價方式: '',
    });
    expect(result).toMatchObject({
      assetType: 'REAL_ESTATE',
      name: '不動產',
      valuationMethod: '市價',
    });
  });

  it('rejects an unrecognized 類型', () => {
    const result = mapCsvRowToPosition({ 類型: '虛擬貨幣', 數量: '1' });
    expect(result).toMatchObject({ error: expect.stringContaining('無法辨識的類型') });
  });

  it('rejects a STOCK row missing 代號', () => {
    const result = mapCsvRowToPosition({ 類型: 'STOCK', 數量: '10' });
    expect(result).toMatchObject({ error: '缺少代號' });
  });

  it('rejects a row missing 數量', () => {
    const result = mapCsvRowToPosition({ 類型: 'CASH', 數量: '' });
    expect(result).toMatchObject({ error: '數量必須為正數' });
  });

  it('rejects a row with a zero or negative 數量', () => {
    expect(mapCsvRowToPosition({ 類型: 'CASH', 數量: '0' })).toMatchObject({
      error: '數量必須為正數',
    });
    expect(mapCsvRowToPosition({ 類型: 'CASH', 數量: '-5' })).toMatchObject({
      error: '數量必須為正數',
    });
  });

  it('rejects a non-numeric 成本', () => {
    const result = mapCsvRowToPosition({
      類型: 'STOCK',
      代號: '2330',
      數量: '10',
      成本: 'abc',
    });
    expect(result).toMatchObject({ error: '成本必須為數字' });
  });
});

describe('mapCsvRows', () => {
  it('splits valid and invalid rows, one bad row does not drop the good ones', () => {
    const { valid, invalid } = mapCsvRows([
      { 類型: 'STOCK', 代號: '2330', 數量: '1000' },
      { 類型: 'STOCK', 代號: '', 數量: '10' }, // missing 代號
      { 類型: 'CASH', 數量: '50000' },
    ]);
    expect(valid).toHaveLength(2);
    expect(invalid).toHaveLength(1);
    expect(invalid[0].error).toBe('缺少代號');
  });
});
