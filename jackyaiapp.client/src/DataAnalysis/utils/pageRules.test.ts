import { describe, expect, it } from 'vitest';

import { rowCountFieldOf, tableForSource } from './pageRules';

import type { FieldDefinition } from '../../apis/dataAnalysisApis/types';

const field = (name: string, canonicalType: FieldDefinition['canonicalType']): FieldDefinition => ({
  name,
  dataType: 'x',
  isNullable: false,
  canonicalType,
});

describe('tableForSource', () => {
  const sources = [
    { id: 'a', defaultTableName: 'sales.Daily' },
    { id: 'b', defaultTableName: null },
  ];

  it('the built-in source opens on dbo.Orders; another on its own default table, or none', () => {
    expect(tableForSource(sources, null)).toBe('dbo.Orders');
    expect(tableForSource(sources, 'a')).toBe('sales.Daily');
    expect(tableForSource(sources, 'b')).toBe('');
    expect(tableForSource(sources, 'gone')).toBe('');
  });
});

describe('rowCountFieldOf', () => {
  it('the date field when it is a date column of any kind; otherwise none', () => {
    const fields = [
      field('At', 'DateTime'),
      field('AtOffset', 'DateTimeOffset'),
      field('Day', 'Date'),
      field('Label', 'String'),
    ];
    expect(['At', 'AtOffset', 'Day'].map((f) => rowCountFieldOf(fields, f))).toEqual([
      'At',
      'AtOffset',
      'Day',
    ]);
    expect(rowCountFieldOf(fields, 'Label')).toBeUndefined();
    expect(rowCountFieldOf(fields, 'Missing')).toBeUndefined();
  });
});
