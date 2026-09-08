import { describe, expect, it } from 'vitest';

import { drawdownBufferColor, EXPOSURE_TOOLTIP } from './exposureGauges';

import { GAIN_COLOR, LOSS_COLOR, WARN_COLOR } from '@/utils/format';

describe('drawdownBufferColor', () => {
  it('is red below the 10% band — a fall that size fits inside a single bad week', () => {
    expect(drawdownBufferColor(6.7)).toBe(LOSS_COLOR);
    expect(drawdownBufferColor(9.9)).toBe(LOSS_COLOR);
  });

  it('is amber between 10% and 25% — within a normal correction', () => {
    expect(drawdownBufferColor(10)).toBe(WARN_COLOR);
    expect(drawdownBufferColor(24.9)).toBe(WARN_COLOR);
  });

  it('is green at 25% and above', () => {
    expect(drawdownBufferColor(25)).toBe(GAIN_COLOR);
    expect(drawdownBufferColor(120)).toBe(GAIN_COLOR);
  });

  it('stays red once the buffer is gone — no gradation past insolvency', () => {
    expect(drawdownBufferColor(0)).toBe(LOSS_COLOR);
    expect(drawdownBufferColor(-100)).toBe(LOSS_COLOR);
  });
});

describe('EXPOSURE_TOOLTIP', () => {
  // The tooltip is the only place the three denominators are spelled out, and the whole reason
  // all three gauges exist is that they differ — so if a formula line ever drifts from the
  // backend's actual computation, this is where the user would be misled.
  it('states each gauge denominator, and that they differ', () => {
    expect(EXPOSURE_TOOLTIP).toContain('水位 = 市場曝險 ÷ 流動資產（不扣負債）');
    expect(EXPOSURE_TOOLTIP).toContain('槓桿 = 市場曝險 ÷ 流動資產淨值（已扣負債）');
    expect(EXPOSURE_TOOLTIP).toContain('抗跌空間 = 流動資產淨值 ÷ 市場曝險');
  });

  it('carries both caveats the numbers depend on', () => {
    expect(EXPOSURE_TOOLTIP).toContain('不含不動產');
    expect(EXPOSURE_TOOLTIP).toContain('同幅漲跌');
  });

  it('renders as multiple lines so the pre-line tooltip has something to break on', () => {
    expect(EXPOSURE_TOOLTIP.split('\n').length).toBeGreaterThan(5);
  });
});
