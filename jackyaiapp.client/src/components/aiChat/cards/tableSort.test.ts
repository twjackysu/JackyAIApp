import { describe, expect, it } from 'vitest';

import { nextSort, sortTableRows } from './tableSort';

import type { CardCell, TableColumn } from './toolCard';

const text: TableColumn = { key: 'name', label: '名稱', numeric: false };
const num: TableColumn = { key: 'value', label: '市值', numeric: true };

const col = (rows: Record<string, CardCell>[], key: string) => rows.map((r) => r[key]);

describe('nextSort', () => {
  it('starts a number column from the largest, a text column from the start, then the other way, then none', () => {
    const a = nextSort(null, num);
    expect(a).toEqual({ key: 'value', direction: 'desc' });
    const b = nextSort(a, num);
    expect(b).toEqual({ key: 'value', direction: 'asc' });
    expect(nextSort(b, num)).toBeNull();
    expect(nextSort(null, text)).toEqual({ key: 'name', direction: 'asc' });
    expect(nextSort(nextSort(null, text), text)).toEqual({ key: 'name', direction: 'desc' });
  });

  it('starts over on another column', () => {
    expect(nextSort({ key: 'value', direction: 'asc' }, text)).toEqual({
      key: 'name',
      direction: 'asc',
    });
  });
});

describe('sortTableRows', () => {
  const rows = [
    { name: '長榮', value: 98000, pct: '-18.0%' },
    { name: '台積電', value: 1245000, pct: '49.5%' },
    { name: '現金', value: null, pct: null },
    { name: 'VOO', value: 352000, pct: '8.9%' },
  ];

  it('leaves the rows as the tool sent them when not sorted, and never changes the card', () => {
    expect(sortTableRows(rows, null)).toBe(rows);
    const copy = [...rows];
    sortTableRows(rows, { key: 'value', direction: 'desc' });
    expect(rows).toEqual(copy);
  });

  it('sorts numbers as numbers, empty cells last either way', () => {
    expect(col([...sortTableRows(rows, { key: 'value', direction: 'desc' })], 'value')).toEqual([
      1245000,
      352000,
      98000,
      null,
    ]);
    expect(col([...sortTableRows(rows, { key: 'value', direction: 'asc' })], 'value')).toEqual([
      98000,
      352000,
      1245000,
      null,
    ]);
  });

  it('sorts a column of numbers written as text (percentages, thousands) by their value', () => {
    expect(col([...sortTableRows(rows, { key: 'pct', direction: 'asc' })], 'pct')).toEqual([
      '-18.0%',
      '8.9%',
      '49.5%',
      null,
    ]);
    const thousands = [{ v: '1,200' }, { v: '950' }, { v: '10,000' }];
    expect(col([...sortTableRows(thousands, { key: 'v', direction: 'desc' })], 'v')).toEqual([
      '10,000',
      '1,200',
      '950',
    ]);
  });

  it('sorts text in zh-TW order with digits by their value, a mixed column as text', () => {
    const codes = [{ c: '台積電 2330' }, { c: '台積電 911' }, { c: 'abc' }, { c: 'ABD' }];
    // zh-TW puts Chinese before Latin letters; case does not count.
    expect(col([...sortTableRows(codes, { key: 'c', direction: 'asc' })], 'c')).toEqual([
      '台積電 911',
      '台積電 2330',
      'abc',
      'ABD',
    ]);
    const mixed = [{ m: '12' }, { m: '—' }, { m: '3' }];
    expect(col([...sortTableRows(mixed, { key: 'm', direction: 'asc' })], 'm')).toEqual([
      '—',
      '3',
      '12',
    ]);
  });

  it('keeps the tool order for rows that compare equal', () => {
    const ties = [
      { k: 1, id: 'a' },
      { k: 1, id: 'b' },
      { k: 0, id: 'c' },
    ];
    expect(col([...sortTableRows(ties, { key: 'k', direction: 'desc' })], 'id')).toEqual([
      'a',
      'b',
      'c',
    ]);
  });
});
