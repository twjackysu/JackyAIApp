import { describe, expect, it } from 'vitest';

import { dashboardPlan, rememberQuery, resultColumnHints } from './summarizeResult';

import type { SummarizeRequest } from '../../../apis/dataAnalysisApis/types';
import type { FieldTransformDef, ViewByEntry } from '../../types';

const transform = (id: number, type: FieldTransformDef['type']): FieldTransformDef => ({
  id,
  fieldName: 'OrderDate',
  name: 'x',
  type,
  granularity: 'Month',
  bins: [],
  groups: [],
  fallback: '',
});

const entry = (overrides: Partial<ViewByEntry>): ViewByEntry => ({
  id: 1,
  fieldName: 'Region',
  type: 'Plain',
  alias: '',
  transformId: null,
  ...overrides,
});

const plan: SummarizeRequest = {
  viewBy: [
    { type: 'DateTrunc', fieldName: 'OrderDate', alias: 'Month' },
    { type: 'Plain', fieldName: 'Region' },
  ],
  measures: [{ fieldName: 'Revenue', aggregation: 'Sum' }],
  filters: [{ fieldName: 'Status', values: ['Completed'] }],
};

describe('resultColumnHints', () => {
  it('AI：計畫的分組，名稱優先用 alias；DateTrunc 的是時間欄位', () => {
    expect(resultColumnHints('ai', plan, [], [])).toEqual({
      groupHints: ['Month', 'Region'],
      temporalHints: ['Month'],
    });
    expect(resultColumnHints('ai', undefined, [], [])).toEqual({
      groupHints: [],
      temporalHints: [],
    });
  });

  it('手動：只有套用 DateTrunc 轉換的分組是時間欄位', () => {
    const entries = [
      entry({ fieldName: 'OrderDate', type: 'Transform', alias: 'Month', transformId: 1 }),
      entry({ fieldName: 'Tier', type: 'Transform', transformId: 2 }),
      entry({ fieldName: 'Gone', type: 'Transform', transformId: 99 }),
      entry({ fieldName: 'Shipped', type: 'Plain', transformId: 1 }),
    ];
    expect(
      resultColumnHints('manual', plan, entries, [
        transform(1, 'DateTrunc'),
        transform(2, 'NumericBin'),
      ]),
    ).toEqual({
      groupHints: ['Month', 'Tier', 'Gone', 'Shipped'],
      temporalHints: ['Month'],
    });
  });
});

describe('dashboardPlan', () => {
  const lastRun: SummarizeRequest = { viewBy: [], measures: [] };

  it('AI：回傳的計畫加上頁面的來源；沒有來源就不帶 context；沒有計畫就是 null', () => {
    expect(dashboardPlan('ai', plan, lastRun, 's1', 'dbo.Orders')).toEqual({
      context: { sourceId: 's1', tableName: 'dbo.Orders' },
      viewBy: plan.viewBy,
      measures: plan.measures,
      filters: plan.filters,
    });
    expect(dashboardPlan('ai', plan, lastRun, undefined, 'dbo.Orders')?.context).toBeUndefined();
    expect(dashboardPlan('ai', undefined, lastRun, 's1', 'dbo.Orders')).toBeNull();
  });

  it('手動：就是上一次執行的那份', () => {
    expect(dashboardPlan('manual', plan, lastRun, 's1', 'dbo.Orders')).toBe(lastRun);
    expect(dashboardPlan('manual', plan, null, 's1', 'dbo.Orders')).toBeNull();
  });
});

describe('rememberQuery', () => {
  it('新的在前、不重複、最多 5 筆', () => {
    expect(rememberQuery([], 'a')).toEqual(['a']);
    expect(rememberQuery(['c', 'b', 'a'], 'a')).toEqual(['a', 'c', 'b']);
    expect(rememberQuery(['e', 'd', 'c', 'b', 'a'], 'f')).toEqual(['f', 'e', 'd', 'c', 'b']);
  });
});
