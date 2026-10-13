import { describe, expect, it } from 'vitest';

import { buildRowCountRequest, sumRowCounts } from './rowCount';

describe('buildRowCountRequest', () => {
  it('counts every row grouped by the date field year, never with an empty group-by', () => {
    const req = buildRowCountRequest({ dateField: 'OrderDate' });
    expect(req.viewBy).toEqual([
      { type: 'DateTrunc', fieldName: 'OrderDate', granularity: 'Year', alias: '_year' },
    ]);
    expect(req.measures).toEqual([
      { fieldName: 'OrderDate', aggregation: 'Count', alias: '_rows' },
    ]);
    expect(req.filters).toEqual([]);
    expect(req.context).toBeUndefined(); // default source
  });

  it('passes a custom source and the filters through', () => {
    const filters = [{ fieldName: 'Region', values: ['EMEA'] }];
    const req = buildRowCountRequest({
      sourceId: 'src-1',
      tableName: 'dbo.Sales',
      dateField: 'SoldAt',
      filters,
    });
    expect(req.context).toEqual({ sourceId: 'src-1', tableName: 'dbo.Sales' });
    expect(req.filters).toBe(filters);
  });
});

describe('sumRowCounts', () => {
  it('adds up the per-year counts, including the group of rows without a date', () => {
    const rows = [
      { _year: '2025', _rows: 7000 },
      { _year: '2026', _rows: 5000 },
      { _year: null, _rows: 12 },
    ];
    expect(sumRowCounts({ rows })).toBe(12012);
  });

  it('is 0 for an empty table or a missing result', () => {
    expect(sumRowCounts({ rows: [] })).toBe(0);
    expect(sumRowCounts(undefined)).toBe(0);
  });
});
