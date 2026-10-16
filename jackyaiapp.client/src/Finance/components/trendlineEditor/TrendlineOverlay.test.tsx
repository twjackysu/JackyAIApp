import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { lineGeometry, type DrawnLine, type LineGeometry } from '../../utils/trendlineDrawings';

import { projectionOf } from './chartProjection';
import { TrendlineOverlay, type TrendlineOverlayProps } from './TrendlineOverlay';

import type { OhlcBar } from '../../utils/trendlines';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

// 30 weekday bars, flat at 100 with a high of 104 and a low of 96.
const bars: OhlcBar[] = [];
for (let t = Date.UTC(2026, 0, 5); bars.length < 30; t += 86_400_000) {
  const day = new Date(t).getUTCDay();
  if (day === 0 || day === 6) continue;
  bars.push({
    date: new Date(t).toISOString().slice(0, 10),
    open: 100,
    high: 104,
    low: 96,
    close: 100,
  });
}

// Bar i is centred at x = 5 + 10i; price 80 is at y = 300 and 120 at y = 20 (7 px a point).
const projection = projectionOf({
  x0: 5,
  xStep: 10,
  bars: 30,
  yMin: 300,
  yMax: 20,
  min: 80,
  max: 120,
});
const at = (index: number, price: number) => ({
  clientX: projection.x(index),
  clientY: projection.y(price),
});

const measure = (l: DrawnLine) => lineGeometry(bars, { atrs: [], swings: [] }, l);
const support = measure({
  id: 'sup',
  from: { date: bars[4].date, price: 90 },
  to: { date: bars[14].date, price: 95 },
  toLatest: false,
}) as LineGeometry;

let container: HTMLDivElement;
let root: Root;
const handlers = {
  onSelect: vi.fn(),
  onChange: vi.fn(),
  onDelete: vi.fn(),
  onCreate: vi.fn(),
};

function render(props: Partial<TrendlineOverlayProps> = {}) {
  act(() =>
    root.render(
      <TrendlineOverlay
        bars={bars}
        lines={[support]}
        projection={projection}
        measure={measure}
        selectedId={null}
        drawing={null}
        {...handlers}
        {...props}
      />,
    ),
  );
}

function pointer(type: string, target: Element, point: { clientX: number; clientY: number }) {
  act(() => {
    target.dispatchEvent(
      new PointerEvent(type, {
        bubbles: true,
        cancelable: true,
        pointerId: 1,
        button: 0,
        ...point,
      }),
    );
  });
}

const hitLine = () => container.querySelector('[data-line-id="sup"]') as SVGElement;
const handle = (part: 'start' | 'end') =>
  container.querySelector(`[data-handle="${part}"]`) as SVGElement;
const surface = () =>
  container.querySelector('[data-testid="trendline-draw-surface"]') as SVGElement;

beforeEach(() => {
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
  Object.values(handlers).forEach((h) => h.mockReset());
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
});

describe('TrendlineOverlay', () => {
  it('selects a line when it is pressed, and shows handles only on the selected one', () => {
    render();
    expect(handle('end')).toBeNull();
    pointer('pointerdown', hitLine(), at(9, 92.5));
    pointer('pointerup', hitLine(), at(9, 92.5));
    expect(handlers.onSelect).toHaveBeenCalledWith('sup');
    expect(handlers.onChange).not.toHaveBeenCalled(); // a click is not a drag

    render({ selectedId: 'sup' });
    expect(handle('start')).not.toBeNull();
    expect(handle('end')).not.toBeNull();
  });

  it('drags an end to another bar, snapping to the bar’s low when close to it', () => {
    render({ selectedId: 'sup' });
    pointer('pointerdown', handle('end'), at(14, 95));
    pointer('pointermove', handle('end'), at(20.3, 96.5)); // 3.5 px from the low of 96
    pointer('pointerup', handle('end'), at(20.3, 96.5));

    expect(handlers.onChange).toHaveBeenCalledTimes(1);
    expect(handlers.onChange).toHaveBeenCalledWith({
      id: 'sup',
      from: { date: bars[4].date, price: 90 },
      to: { date: bars[20].date, price: 96 },
      toLatest: false,
      side: 'support',
    });
  });

  it('drags the whole line, keeping its slope', () => {
    render({ selectedId: 'sup' });
    pointer('pointerdown', hitLine(), at(9, 92.5));
    pointer('pointermove', hitLine(), at(12, 94.5));
    pointer('pointerup', hitLine(), at(12, 94.5));

    const moved = handlers.onChange.mock.calls[0][0] as DrawnLine;
    expect(moved.from.date).toBe(bars[7].date);
    expect(moved.to.date).toBe(bars[17].date);
    expect(moved.from.price).toBeCloseTo(92);
    expect(moved.to.price).toBeCloseTo(97);
  });

  it('drops a drag the browser took over', () => {
    render({ selectedId: 'sup' });
    pointer('pointerdown', handle('end'), at(14, 95));
    pointer('pointermove', handle('end'), at(20, 99));
    pointer('pointercancel', handle('end'), at(20, 99));
    expect(handlers.onChange).not.toHaveBeenCalled();
  });

  it('nudges a focused line with the arrow keys and deletes it with Delete', () => {
    render({ selectedId: 'sup' });
    const key = (k: string) =>
      act(() => {
        hitLine().dispatchEvent(new KeyboardEvent('keydown', { key: k, bubbles: true }));
      });
    key('ArrowRight');
    expect((handlers.onChange.mock.calls[0][0] as DrawnLine).from.date).toBe(bars[5].date);
    key('ArrowUp');
    expect((handlers.onChange.mock.calls[1][0] as DrawnLine).from.price).toBeCloseTo(90 + 4 / 7);
    key('Delete');
    expect(handlers.onDelete).toHaveBeenCalledWith('sup');
  });

  it('draws a new line of the chosen kind from two clicks', () => {
    render({ drawing: 'resistance' });
    expect(hitLine().style.pointerEvents).toBe('none'); // clicks go to the drawing, not the lines
    pointer('pointerdown', surface(), at(3, 110));
    pointer('pointerup', surface(), at(3, 110));
    expect(handlers.onCreate).not.toHaveBeenCalled();
    pointer('pointermove', surface(), at(18, 112));
    pointer('pointerdown', surface(), at(18, 112));

    const created = handlers.onCreate.mock.calls[0][0] as DrawnLine;
    expect(created.id).toMatch(/^user-/);
    expect(created.side).toBe('resistance');
    expect(created.from).toEqual({ date: bars[3].date, price: expect.closeTo(110) });
    expect(created.to).toEqual({ date: bars[18].date, price: expect.closeTo(112) });
  });

  it('draws a new line from one drag, snapping its ends to highs and lows', () => {
    render({ drawing: 'support' });
    pointer('pointerdown', surface(), at(2, 103.4)); // 4.2 px under the high of 104
    pointer('pointermove', surface(), at(29, 96.9));
    pointer('pointerup', surface(), at(29, 96.9));

    const created = handlers.onCreate.mock.calls[0][0] as DrawnLine;
    expect(created.from).toEqual({ date: bars[2].date, price: 104 });
    expect(created.to).toEqual({ date: bars[29].date, price: 96 });
    expect(created.toLatest).toBe(true);
    // Drawn as a support, it stays one though it sits above most closes.
    expect(created.side).toBe('support');
  });

  it('waits for a second point when both clicks land on one bar', () => {
    render({ drawing: 'support' });
    pointer('pointerdown', surface(), at(6, 110));
    pointer('pointerup', surface(), at(6, 110));
    pointer('pointerdown', surface(), at(6.2, 100));
    expect(handlers.onCreate).not.toHaveBeenCalled();
    pointer('pointerdown', surface(), at(9, 100));
    expect(handlers.onCreate).toHaveBeenCalledTimes(1);
  });
});
