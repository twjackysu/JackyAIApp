import { describe, expect, it } from 'vitest';

// Imported through Vite's `?raw` loader rather than read with node:fs — the client tsconfig ships
// DOM typings only, and pulling @types/node in just for this test would hand every app file a
// `process` it has no business touching.
import backendCostsSource from '../../../JackyAIApp.Server/Common/CreditCosts.cs?raw';

import { CREDIT_COSTS } from './creditCosts';

/**
 * CREDIT_COSTS is a hand-maintained mirror of the backend's Common/CreditCosts.cs, and the numbers
 * it holds are rendered straight into "消耗 N 點使用額度" captions. Nothing used to connect the two
 * files, so a backend price change (or a client entry for a feature that is actually free) only
 * showed up as the UI quoting one price while the server charged another. This test reads the .cs
 * file and fails on exactly that.
 *
 * It is deliberately one-directional: every client key must exist on the backend with the same
 * value, but backend-only constants are fine — server-side bonuses (DailyLoginBonus, …) have no UI
 * caption to keep honest.
 */

/** `MARKET_DIGEST` → `MarketDigest`, matching the C# naming of the same constant. */
function toPascalCase(screamingSnake: string): string {
  return screamingSnake
    .split('_')
    .map((part) => part.charAt(0) + part.slice(1).toLowerCase())
    .join('');
}

function readBackendCosts(): Record<string, number> {
  const costs: Record<string, number> = {};
  for (const match of backendCostsSource.matchAll(
    /public\s+const\s+ulong\s+(\w+)\s*=\s*(\d+)\s*;/g,
  )) {
    costs[match[1]] = Number(match[2]);
  }
  return costs;
}

describe('CREDIT_COSTS mirrors the backend', () => {
  const backendCosts = readBackendCosts();

  it('parses the backend constants', () => {
    // Guards the regex itself: a C# refactor that changes the declaration shape must not turn this
    // whole suite into a no-op that passes on an empty map.
    expect(Object.keys(backendCosts).length).toBeGreaterThan(10);
  });

  it.each(Object.entries(CREDIT_COSTS))('%s matches CreditCosts.cs', (key, value) => {
    const backendName = toPascalCase(key);
    expect(backendCosts, `CreditCosts.${backendName} does not exist on the backend`).toHaveProperty(
      backendName,
    );
    expect(backendCosts[backendName]).toBe(value);
  });
});
