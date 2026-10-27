import { describe, expect, it } from 'vitest';

import { sankeyTooltip } from './sankeyTooltip';

const SEP = '␟';

describe('sankeyTooltip', () => {
  it('escapes a node name, which ECharts writes as HTML', () => {
    const name = `1${SEP}<img src=x onerror=alert(1)>`;
    expect(sankeyTooltip({ name }, SEP)).toBe('&lt;img src=x onerror=alert(1)&gt;');
  });

  it('escapes both ends of a flow and keeps its value', () => {
    const tip = sankeyTooltip(
      { dataType: 'edge', data: { source: `0${SEP}A<b>`, target: `1${SEP}B&C` }, value: 12 },
      SEP,
    );
    expect(tip).toBe('A&lt;b&gt; → B&amp;C: 12');
  });
});
