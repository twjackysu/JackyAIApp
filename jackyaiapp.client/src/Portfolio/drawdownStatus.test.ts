import { describe, expect, it } from 'vitest';

import { currentDrawdownStatus } from './drawdownStatus';

describe('currentDrawdownStatus', () => {
  it('reports being at the peak when there is no current drawdown', () => {
    expect(currentDrawdownStatus(0)).toEqual({ label: '目前位於高點', color: 'success' });
  });

  it('reports being below the peak for any negative drawdown, however small', () => {
    expect(currentDrawdownStatus(-0.01)).toEqual({ label: '低於前高', color: 'warning' });
    expect(currentDrawdownStatus(-18.2)).toEqual({ label: '低於前高', color: 'warning' });
  });

  it('is independent of whether the worst historical episode recovered', () => {
    // The reported bug: net worth fell hard, made a new high (so the worst episode IS resolved,
    // stillUnderwater === false), then drifted down again. The badge must describe NOW, so this
    // has to read 低於前高 despite that episode being over — otherwise it contradicts the
    // 目前回撤 figure sitting right next to it.
    expect(currentDrawdownStatus(-3.1).label).toBe('低於前高');
  });
});
