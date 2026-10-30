import { describe, expect, it } from 'vitest';

import { clampDrawerWidth, DRAWER_WIDTH } from './drawerWidth';

describe('clampDrawerWidth', () => {
  it('keeps the drawer within its range on a wide window', () => {
    expect(clampDrawerWidth(520, 1440)).toBe(520);
    expect(clampDrawerWidth(300, 1440)).toBe(DRAWER_WIDTH.min);
    expect(clampDrawerWidth(900, 1440)).toBe(DRAWER_WIDTH.max);
    expect(clampDrawerWidth(512.6, 1440)).toBe(513);
  });

  it('leaves a strip of the page on a narrower window, but never goes under its narrowest', () => {
    expect(clampDrawerWidth(640, 700)).toBe(580);
    expect(clampDrawerWidth(640, 500)).toBe(DRAWER_WIDTH.min);
  });

  it('starts as first opened from a width it cannot read', () => {
    expect(clampDrawerWidth(Number.NaN, 1440)).toBe(DRAWER_WIDTH.initial);
  });
});
