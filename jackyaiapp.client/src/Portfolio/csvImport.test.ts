import { describe, expect, it } from 'vitest';

import {
  buildImportPreview,
  downloadImportTemplateCsv,
  IMPORT_HEADERS,
  mapImportRow,
} from './csvImport';

import { parseTabular } from '@/utils/tabularParse';

function row(overrides: Partial<Record<(typeof IMPORT_HEADERS)[number], string>>) {
  const base: Record<string, string> = {
    類型: '',
    代號: '',
    名稱: '',
    市場: '',
    計價幣別: '',
    成本幣別: '',
    數量: '',
    成本: '',
    估價方式: '',
  };
  return { ...base, ...overrides };
}

describe('mapImportRow', () => {
  it('accepts the raw AssetType value', () => {
    const { position, error } = mapImportRow(
      row({ 類型: 'STOCK', 代號: '2330', 名稱: '台積電', 數量: '1000' }),
    );
    expect(error).toBeNull();
    expect(position?.assetType).toBe('STOCK');
    expect(position?.symbol).toBe('2330');
  });

  it('accepts the Chinese TYPE_LABEL, reverse-looked-up rather than hardcoded', () => {
    const { position, error } = mapImportRow(row({ 類型: '股票', 代號: '2330', 數量: '1000' }));
    expect(error).toBeNull();
    expect(position?.assetType).toBe('STOCK');
  });

  it('errors on an unrecognized type', () => {
    const { position, error } = mapImportRow(row({ 類型: '虛擬貨幣' }));
    expect(position).toBeNull();
    expect(error).toContain('無法辨識的類型');
  });

  it('errors when 類型 is missing entirely', () => {
    const { position, error } = mapImportRow(row({}));
    expect(position).toBeNull();
    expect(error).toBe('缺少類型');
  });

  it('defaults 市場 to TW for STOCK when blank', () => {
    const { position } = mapImportRow(row({ 類型: 'STOCK', 代號: '2330', 數量: '1' }));
    expect(position?.market).toBe('TW');
  });

  it('honors 市場=US for STOCK', () => {
    const { position } = mapImportRow(row({ 類型: 'STOCK', 代號: 'AAPL', 市場: 'US', 數量: '1' }));
    expect(position?.market).toBe('US');
  });

  it('forces market to FUND for FUND rows regardless of the 市場 column', () => {
    const { position } = mapImportRow(row({ 類型: 'FUND', 代號: 'ALI006', 數量: '1' }));
    expect(position?.market).toBe('FUND');
  });

  it('leaves market blank for CASH/LIABILITY/REAL_ESTATE', () => {
    expect(mapImportRow(row({ 類型: 'CASH', 數量: '1' })).position?.market).toBe('');
    expect(mapImportRow(row({ 類型: 'LIABILITY', 數量: '1' })).position?.market).toBe('');
    expect(mapImportRow(row({ 類型: 'REAL_ESTATE', 數量: '1' })).position?.market).toBe('');
  });

  it('falls back to the same per-type default name makeRow uses when 名稱 is blank', () => {
    expect(mapImportRow(row({ 類型: 'CASH', 數量: '1' })).position?.name).toBe('現金');
    expect(mapImportRow(row({ 類型: 'LIABILITY', 數量: '1' })).position?.name).toBe('信貸');
    expect(mapImportRow(row({ 類型: 'REAL_ESTATE', 數量: '1' })).position?.name).toBe('不動產');
  });

  it('does not invent a name default for STOCK/FUND when 名稱 is blank', () => {
    expect(mapImportRow(row({ 類型: 'STOCK', 代號: '2330', 數量: '1' })).position?.name).toBe('');
  });

  it('defaults 計價幣別 to TWD and 成本幣別 to 計價幣別 when blank', () => {
    const { position } = mapImportRow(row({ 類型: 'STOCK', 代號: '2330', 數量: '1' }));
    expect(position?.currency).toBe('TWD');
    expect(position?.costCurrency).toBe('TWD');

    const usd = mapImportRow(row({ 類型: 'STOCK', 代號: 'AAPL', 計價幣別: 'USD', 數量: '1' }));
    expect(usd.position?.currency).toBe('USD');
    expect(usd.position?.costCurrency).toBe('USD'); // falls back to 計價幣別, not TWD
  });

  it('honors an explicit 成本幣別 distinct from 計價幣別', () => {
    const { position } = mapImportRow(
      row({ 類型: 'STOCK', 代號: 'AAPL', 計價幣別: 'USD', 成本幣別: 'TWD', 數量: '1' }),
    );
    expect(position?.costCurrency).toBe('TWD');
  });

  it('parses 成本 as the unconverted total cost, not per-unit or TWD-converted', () => {
    const { position } = mapImportRow(
      row({ 類型: 'STOCK', 代號: '2330', 數量: '1000', 成本: '550000' }),
    );
    expect(position?.costBasis).toBe(550000);
  });

  it('leaves costBasis null when 成本 is blank', () => {
    const { position } = mapImportRow(row({ 類型: 'STOCK', 代號: '2330', 數量: '1' }));
    expect(position?.costBasis).toBeNull();
  });

  it('errors on a non-numeric 成本', () => {
    const { position, error } = mapImportRow(
      row({ 類型: 'STOCK', 代號: '2330', 數量: '1', 成本: 'abc' }),
    );
    expect(position).toBeNull();
    expect(error).toContain('成本欄位不是數字');
  });

  it('defaults 估價方式 to 市價 for REAL_ESTATE when blank', () => {
    const { position } = mapImportRow(row({ 類型: 'REAL_ESTATE', 數量: '1' }));
    expect(position?.valuationMethod).toBe('市價');
  });

  it('accepts an explicit valid 估價方式', () => {
    const { position } = mapImportRow(
      row({ 類型: 'REAL_ESTATE', 數量: '1', 估價方式: '買價扣除折舊' }),
    );
    expect(position?.valuationMethod).toBe('買價扣除折舊');
  });

  it('errors on an invalid 估價方式', () => {
    const { position, error } = mapImportRow(
      row({ 類型: 'REAL_ESTATE', 數量: '1', 估價方式: '亂填' }),
    );
    expect(position).toBeNull();
    expect(error).toContain('估價方式須為');
  });

  it('does not set valuationMethod for non-REAL_ESTATE types', () => {
    const { position } = mapImportRow(row({ 類型: 'CASH', 數量: '1' }));
    expect(position?.valuationMethod).toBeUndefined();
  });

  it('leaves an unparseable/missing 數量 as 0 rather than hard-erroring (caught by rowError)', () => {
    const { position, error } = mapImportRow(row({ 類型: 'STOCK', 代號: '2330' }));
    expect(error).toBeNull();
    expect(position?.quantity).toBe(0);
  });
});

describe('buildImportPreview', () => {
  it('marks a fully valid row as importable', () => {
    const preview = buildImportPreview([
      row({ 類型: 'STOCK', 代號: '2330', 名稱: '台積電', 數量: '1000' }),
    ]);
    expect(preview[0].editorRow).not.toBeNull();
    expect(preview[0].error).toBeNull();
  });

  it('rejects a STOCK row missing 代號 via the shared rowError() check', () => {
    const preview = buildImportPreview([row({ 類型: 'STOCK', 數量: '1000' })]);
    expect(preview[0].editorRow).toBeNull();
    expect(preview[0].error).toBe('請選擇股票');
  });

  it('rejects a row with a missing/zero 數量 via the shared rowError() check', () => {
    const preview = buildImportPreview([row({ 類型: 'CASH' })]);
    expect(preview[0].editorRow).toBeNull();
    expect(preview[0].error).toBe('請輸入金額');
  });

  it('keeps good rows importable alongside a bad one in the same file', () => {
    const preview = buildImportPreview([
      row({ 類型: 'STOCK', 代號: '2330', 數量: '1000' }),
      row({ 類型: 'CASH', 數量: '100000' }),
      row({ 類型: 'STOCK', 數量: '' }), // missing 代號 and 數量
    ]);
    expect(preview.filter((p) => p.editorRow !== null)).toHaveLength(2);
    expect(preview.filter((p) => p.editorRow === null)).toHaveLength(1);
  });

  it('rejects an unrecognized type at the mapping stage, before rowError ever runs', () => {
    const preview = buildImportPreview([row({ 類型: '不明類型', 數量: '1' })]);
    expect(preview[0].editorRow).toBeNull();
    expect(preview[0].error).toContain('無法辨識的類型');
  });
});

describe('downloadImportTemplateCsv round-trip', () => {
  it('produces a CSV whose headers/example rows parse back into fully valid PositionInputs', () => {
    let captured: Blob | null = null;
    const originalCreateObjectURL = URL.createObjectURL;
    const originalRevokeObjectURL = URL.revokeObjectURL;
    URL.createObjectURL = (blob: Blob) => {
      captured = blob;
      return 'blob:mock';
    };
    URL.revokeObjectURL = () => {};

    try {
      downloadImportTemplateCsv();
    } finally {
      URL.createObjectURL = originalCreateObjectURL;
      URL.revokeObjectURL = originalRevokeObjectURL;
    }

    expect(captured).not.toBeNull();
    return (captured as unknown as Blob).text().then((text) => {
      // Strip the UTF-8 BOM the export helper prepends for Excel.
      const bom = String.fromCharCode(0xfeff);
      const withoutBom = text.startsWith(bom) ? text.slice(bom.length) : text;
      const parsed = parseTabular(withoutBom);
      expect(parsed).not.toBeNull();
      expect(parsed?.headers).toEqual([...IMPORT_HEADERS]);

      const preview = buildImportPreview(parsed?.rows ?? []);
      expect(preview.every((p) => p.editorRow !== null)).toBe(true);
    });
  });
});
