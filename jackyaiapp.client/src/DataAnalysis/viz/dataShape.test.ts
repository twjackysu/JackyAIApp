import { describe, expect, it } from 'vitest';

import { deriveViewByHints } from './dataShape';

import type { DashboardWidget } from '@/apis/dashboardApis/types';

describe('deriveViewByHints', () => {
  it('reads the plan a viewer who is not the owner gets: an alias with no column behind it (D-195)', () => {
    const viewerPlan = {
      viewBy: [
        { alias: '月份', type: 'DateTrunc' },
        { fieldName: 'Region', type: 'Field' },
      ],
    } as unknown as DashboardWidget['plan'];

    expect(deriveViewByHints(viewerPlan.viewBy)).toEqual({
      groupHints: ['月份', 'Region'],
      temporalHints: ['月份'],
    });
  });
});
