import { describe, expect, it } from 'vitest';

import { liquidSharePct } from './positionShare';

describe('liquidSharePct', () => {
  // Liquid gross assets: TW 600 + US 200 + fund 100 + cash 100 = 1,000. A 3,000 house and
  // -500 of debt sit beside them but are not part of the denominator.
  const liquid = 1_000;

  it('is a share of liquid gross assets, not of everything including real estate', () => {
    expect(liquidSharePct(600, 'STOCK', liquid)).toBe(60);
    expect(liquidSharePct(100, 'FUND', liquid)).toBe(10);
    expect(liquidSharePct(100, 'CASH', liquid)).toBe(10);
  });

  it('gives real estate no share', () => {
    expect(liquidSharePct(3_000, 'REAL_ESTATE', liquid)).toBeNull();
  });

  it('shows liability as a negative share of liquid gross assets', () => {
    expect(liquidSharePct(-500, 'LIABILITY', liquid)).toBe(-50);
  });

  it('has no share without liquid assets', () => {
    expect(liquidSharePct(100, 'STOCK', 0)).toBeNull();
  });
});
