import { describe, expect, it } from 'vitest';

import holdingsLimitsCs from '../../../JackyAIApp.Server/Services/Portfolio/HoldingsLimits.cs?raw';

import { buildImportPreview, previewRowsShown } from './csvImport';
import { makeRow, rowError } from './editorTypes';
import {
  lengthProblem,
  MAX_CODE_LENGTH,
  MAX_NAME_LENGTH,
  MAX_POSITIONS,
  MAX_SYMBOL_LENGTH,
  OVER_POSITIONS_LIMIT,
  tooManyPositionsMessage,
} from './holdingsLimits';

const csConst = (name: string) =>
  Number(new RegExp(`const int ${name} = (\\d+);`).exec(holdingsLimitsCs)?.[1]);

describe('holdingsLimits', () => {
  it('holds the same numbers as the server (HoldingsLimits.cs)', () => {
    expect(MAX_POSITIONS).toBe(csConst('MaxPositions'));
    expect(MAX_SYMBOL_LENGTH).toBe(csConst('MaxSymbolLength'));
    expect(MAX_NAME_LENGTH).toBe(csConst('MaxNameLength'));
    expect(MAX_CODE_LENGTH).toBe(csConst('MaxCodeLength'));
  });

  it('names the field past its limit, in the server’s order', () => {
    expect(lengthProblem({ symbol: '2330', name: '台積電' })).toBeNull();
    expect(lengthProblem({ symbol: 'x'.repeat(MAX_SYMBOL_LENGTH) })).toBeNull();
    expect(
      lengthProblem({ symbol: 'x'.repeat(MAX_SYMBOL_LENGTH + 1), name: 'y'.repeat(999) }),
    ).toBe('代號最多 40 個字');
    expect(lengthProblem({ name: 'y'.repeat(MAX_NAME_LENGTH + 1) })).toBe('名稱最多 200 個字');
    expect(lengthProblem({ currency: 'z'.repeat(MAX_CODE_LENGTH + 1) })).toBe(
      '市場、幣別與估價方式最多 20 個字',
    );
  });

  it('makes an editor row past a limit invalid, a cash row too', () => {
    const stock = { ...makeRow('STOCK'), symbol: '2330', quantity: 1000 };
    expect(rowError(stock)).toBeNull();
    expect(rowError({ ...stock, name: 'y'.repeat(MAX_NAME_LENGTH + 1) })).toBe('名稱最多 200 個字');
    const cash = { ...makeRow('CASH'), quantity: 100, name: 'y'.repeat(MAX_NAME_LENGTH + 1) };
    expect(rowError(cash)).toBe('名稱最多 200 個字');
    // A missing amount is still said first.
    expect(rowError({ ...cash, quantity: 0 })).toBe('請輸入金額');
  });

  it('says how many rows to take out', () => {
    expect(tooManyPositionsMessage(305)).toBe(
      '一次最多儲存 300 筆持倉，目前有 305 筆，請刪掉 5 筆。',
    );
  });
});

describe('buildImportPreview room', () => {
  const stock = (symbol: string) => ({ 類型: 'STOCK', 代號: symbol, 數量: '1000' });

  it('marks each valid row past the room, leaving earlier ones and invalid ones as they are', () => {
    const preview = buildImportPreview(
      [stock('2330'), { 類型: '???' }, stock('2317'), stock('2454')],
      2,
    );
    expect(preview.map((p) => p.editorRow?.symbol ?? p.error)).toEqual([
      '2330',
      '無法辨識的類型：???',
      '2317',
      OVER_POSITIONS_LIMIT,
    ]);
  });

  it('takes up to the server’s limit when no room is given', () => {
    const rows = Array.from({ length: MAX_POSITIONS + 1 }, (_, i) => stock(String(1000 + i)));
    const preview = buildImportPreview(rows);
    expect(preview.filter((p) => p.editorRow)).toHaveLength(MAX_POSITIONS);
    expect(preview[MAX_POSITIONS].error).toBe(OVER_POSITIONS_LIMIT);
  });
});

describe('previewRowsShown', () => {
  it('lists the first rows and every left-out row after them, by its line in the file', () => {
    const rows = Array.from({ length: 30 }, (_, i) => ({
      類型: 'STOCK',
      代號: String(1000 + i),
      數量: i === 24 ? '' : '1000',
    }));
    const shown = previewRowsShown(buildImportPreview(rows), 3);
    expect(shown.map((s) => s.line)).toEqual([2, 3, 4, 26]);
    expect(shown[3].row.error).toBe('請輸入股數');
  });
});
