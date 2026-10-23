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

/** Renders the overlay inside a parent that, like the chart, listens for presses. */
function renderInside(onParentPointerDown: () => void, props: Partial<TrendlineOverlayProps> = {}) {
  act(() =>
    root.render(
      <div onPointerDown={onParentPointerDown}>
        <TrendlineOverlay
          bars={bars}
          lines={[support]}
          projection={projection}
          measure={measure}
          selectedId={null}
          drawing={null}
          {...handlers}
          {...props}
        />
      </div>,
    ),
  );
}

/** Like `pointer`, for another finger or button. */
function pointerWith(
  type: string,
  target: Element,
  point: { clientX: number; clientY: number },
  init: { pointerId?: number; button?: number } = {},
) {
  let event!: PointerEvent;
  act(() => {
    event = new PointerEvent(type, {
      bubbles: true,
      cancelable: true,
      pointerId: 1,
      button: 0,
      ...point,
      ...init,
    });
    target.dispatchEvent(event);
  });
  return event;
}

// Ends at the newest bar, so it is a line in force today.
const resistance = measure({
  id: 'res',
  from: { date: bars[20].date, price: 110 },
  to: { date: bars[29].date, price: 112 },
  toLatest: true,
}) as LineGeometry;

const drawnLine = (id: string) =>
  container.querySelector(`[data-line-id="${id}"]`)!.previousElementSibling as SVGLineElement;
const svg = () => container.querySelector('svg[aria-label="趨勢線"]') as SVGSVGElement;
const label = () => container.querySelector('text');

describe('TrendlineOverlay — dragging', () => {
  it('a press that barely moves is a click, not a drag', () => {
    render({ selectedId: 'sup' });
    pointer('pointerdown', handle('end'), at(14, 95));
    pointer('pointermove', handle('end'), {
      clientX: projection.x(14) + 2,
      clientY: projection.y(95),
    });
    pointer('pointerup', handle('end'), at(14, 95));
    expect(handlers.onChange).not.toHaveBeenCalled();
  });

  it('an end far from a high or low stays where it is put, rounded to the nearest bar', () => {
    render({ selectedId: 'sup' });
    pointer('pointerdown', handle('end'), at(14, 95));
    // 15 px over the low of 96: too far to snap. Bar 20.6 rounds to 21.
    pointer('pointermove', handle('end'), at(20.6, 96 + 15 / 7));
    pointer('pointerup', handle('end'), at(20.6, 96 + 15 / 7));
    const moved = handlers.onChange.mock.calls[0][0] as DrawnLine;
    expect(moved.to.date).toBe(bars[21].date);
    expect(moved.to.price).toBeCloseTo(96 + 15 / 7);
  });

  it('an end dragged past the plot stays on its last bar and its lowest price', () => {
    render({ selectedId: 'sup' });
    pointer('pointerdown', handle('end'), at(14, 95));
    pointer('pointermove', handle('end'), { clientX: 2000, clientY: 2000 });
    pointer('pointerup', handle('end'), { clientX: 2000, clientY: 2000 });
    const moved = handlers.onChange.mock.calls[0][0] as DrawnLine;
    expect(moved.to.date).toBe(bars[29].date);
    expect(moved.to.price).toBeCloseTo(80);
  });

  it('a whole line dragged past the plot moves only as far as its edge', () => {
    render({ selectedId: 'sup' });
    pointer('pointerdown', hitLine(), at(9, 92.5));
    pointer('pointermove', hitLine(), { clientX: 2000, clientY: projection.y(92.5) });
    pointer('pointerup', hitLine(), { clientX: 2000, clientY: projection.y(92.5) });
    const moved = handlers.onChange.mock.calls[0][0] as DrawnLine;
    // The line keeps its length: it stops where its end reaches the newest bar.
    expect(moved.from.date).toBe(bars[19].date);
    expect(moved.to.date).toBe(bars[29].date);
  });

  it('shows the line where it is being dragged, and back where it was if the drag goes nowhere', () => {
    render({ selectedId: 'sup' });
    const x2 = drawnLine('sup').getAttribute('x2');
    pointer('pointerdown', handle('end'), at(14, 95));
    pointer('pointermove', handle('end'), at(20, 99));
    expect(drawnLine('sup').getAttribute('x2')).toBe(String(projection.x(20)));
    pointer('pointerup', handle('end'), at(20, 99));
    // The parent has not taken the change here, so the line is drawn from its props again.
    expect(drawnLine('sup').getAttribute('x2')).toBe(x2);
  });

  it('a step that would take the line off the chart is skipped: the last good one is kept', () => {
    const offAt = bars[20].date;
    render({
      selectedId: 'sup',
      measure: (l) => (l.to.date === offAt ? null : measure(l)),
    });
    pointer('pointerdown', handle('end'), at(14, 95));
    pointer('pointermove', handle('end'), at(22, 99));
    pointer('pointermove', handle('end'), at(20, 99));
    pointer('pointerup', handle('end'), at(20, 99));
    expect((handlers.onChange.mock.calls[0][0] as DrawnLine).to.date).toBe(bars[22].date);
  });

  it('only the main button drags, and the press does not reach the chart below', () => {
    const below = vi.fn();
    renderInside(below, { selectedId: 'sup' });
    pointerWith('pointerdown', handle('end'), at(14, 95), { button: 2 });
    pointerWith('pointermove', handle('end'), at(20, 99));
    pointerWith('pointerup', handle('end'), at(20, 99));
    expect(handlers.onChange).not.toHaveBeenCalled();
    expect(below).toHaveBeenCalledTimes(1); // the right-button press was not taken

    pointer('pointerdown', handle('end'), at(14, 95));
    pointer('pointerdown', hitLine(), at(9, 92.5));
    expect(below).toHaveBeenCalledTimes(1);
  });

  it('another finger moving does not make a drag of a press that did not move', () => {
    render({ selectedId: 'sup' });
    pointerWith('pointerdown', handle('end'), at(14, 95));
    pointerWith('pointermove', handle('end'), at(20, 99), { pointerId: 2 });
    pointerWith('pointerup', handle('end'), at(14, 95));
    expect(handlers.onChange).not.toHaveBeenCalled();
  });

  it('another finger neither moves nor ends the drag', () => {
    render({ selectedId: 'sup' });
    pointerWith('pointerdown', handle('end'), at(14, 95));
    pointerWith('pointermove', handle('end'), at(20, 99), { pointerId: 2 });
    pointerWith('pointerup', handle('end'), at(20, 99), { pointerId: 2 });
    expect(handlers.onChange).not.toHaveBeenCalled();

    pointerWith('pointermove', handle('end'), at(22, 99));
    pointerWith('pointerup', handle('end'), at(22, 99), { pointerId: 2 });
    expect(handlers.onChange).not.toHaveBeenCalled();
    pointerWith('pointerup', handle('end'), at(22, 99));
    expect(handlers.onChange).toHaveBeenCalledTimes(1);
  });
});

describe('TrendlineOverlay — keyboard', () => {
  const key = (k: string) => {
    let event!: KeyboardEvent;
    act(() => {
      event = new KeyboardEvent('keydown', { key: k, bubbles: true, cancelable: true });
      hitLine().dispatchEvent(event);
    });
    return event;
  };

  it('ArrowLeft and ArrowDown move the other way, and the page does not scroll', () => {
    render({ selectedId: 'sup' });
    expect(key('ArrowLeft').defaultPrevented).toBe(true);
    expect((handlers.onChange.mock.calls[0][0] as DrawnLine).from.date).toBe(bars[3].date);
    key('ArrowDown');
    expect((handlers.onChange.mock.calls[1][0] as DrawnLine).from.price).toBeCloseTo(90 - 4 / 7);
  });

  it('Backspace deletes too; other keys are left alone', () => {
    render({ selectedId: 'sup' });
    expect(key('Backspace').defaultPrevented).toBe(true);
    expect(handlers.onDelete).toHaveBeenCalledWith('sup');
    expect(key('Delete').defaultPrevented).toBe(true);
    expect(key('a').defaultPrevented).toBe(false);
  });

  it('focusing a line selects it', () => {
    render();
    act(() => hitLine().focus());
    expect(handlers.onSelect).toHaveBeenCalledWith('sup');
  });
});

describe('TrendlineOverlay — how lines look', () => {
  it('a line in force today is drawn bolder and solid; the selected one bolder still', () => {
    render({ lines: [support, resistance] });
    expect(resistance.isActive).toBe(true);
    expect(support.isActive).toBe(false);
    expect(drawnLine('res').getAttribute('stroke-width')).toBe('2.5');
    expect(drawnLine('res').getAttribute('stroke-opacity')).toBe('1');
    expect(drawnLine('sup').getAttribute('stroke-width')).toBe('1.5');
    expect(drawnLine('sup').getAttribute('stroke-opacity')).toBe('0.7');

    render({ lines: [support, resistance], selectedId: 'sup' });
    expect(drawnLine('sup').getAttribute('stroke-width')).toBe('3');
    expect(drawnLine('sup').getAttribute('stroke-opacity')).toBe('1');
  });

  it('names each line for a screen reader, with today’s price for a line in force', () => {
    render({ lines: [support, resistance], selectedId: 'res' });
    const res = container.querySelector('[data-line-id="res"]')!;
    expect(container.querySelector('[data-line-id="sup"]')!.getAttribute('aria-label')).toBe(
      `支撐線，${bars[4].date} 到 ${bars[14].date}，連 ${support.touches} 點`,
    );
    expect(res.getAttribute('aria-label')).toBe(
      `壓力線，${bars[20].date} 到 ${bars[29].date}，連 ${resistance.touches} 點，今天約 112`,
    );
    expect(res.getAttribute('aria-pressed')).toBe('true');
    expect(hitLine().getAttribute('aria-pressed')).toBe('false');
    expect((res as SVGElement).style.cursor).toBe('move');
    expect(hitLine().style.cursor).toBe('pointer');
  });

  it('a line drawn from a day between bars is named from the first bar it reaches', () => {
    const fromWeekend = measure({
      id: 'wk',
      from: { date: '2026-01-10', price: 90 }, // a Saturday, between bars 4 and 5
      to: { date: bars[14].date, price: 95 },
      toLatest: false,
    }) as LineGeometry;
    render({ lines: [fromWeekend] });
    expect(container.querySelector('[data-line-id="wk"]')!.getAttribute('aria-label')).toMatch(
      new RegExp(`^支撐線，${bars[5].date} 到`),
    );
  });

  it('the selected line’s handles sit on its ends, its price by the end — to the left near the edge', () => {
    render({ selectedId: 'sup' });
    expect(handle('start').getAttribute('cx')).toBe(String(projection.x(4)));
    expect(handle('end').getAttribute('cx')).toBe(String(projection.x(14)));
    expect(label()?.textContent).toBe('95');
    expect(label()?.getAttribute('text-anchor')).toBe('start');

    render({ lines: [support, resistance], selectedId: 'res' });
    expect(label()?.textContent).toBe('112');
    expect(label()?.getAttribute('text-anchor')).toBe('end');
  });

  it('no handles while drawing, even with a line selected', () => {
    render({ selectedId: 'sup', drawing: 'support' });
    expect(handle('start')).toBeNull();
  });

  it('swiping a line scrolls the page, until a line is selected or one is being drawn', () => {
    render();
    expect(svg().style.touchAction).toBe('auto');
    render({ selectedId: 'sup' });
    expect(svg().style.touchAction).toBe('none');
    render({ drawing: 'resistance' });
    expect(svg().style.touchAction).toBe('none');
  });
});

describe('TrendlineOverlay — drawing', () => {
  it('shows the bar and price under the pointer before the first click, then the line being drawn', () => {
    render({ drawing: 'support' });
    pointer('pointermove', surface(), at(2, 110));
    expect(label()?.textContent).toBe(`${bars[2].date}  110`);
    expect(container.querySelector('[stroke-dasharray]')).toBeNull();

    pointer('pointerdown', surface(), at(3, 110));
    pointer('pointerup', surface(), at(3, 110));
    pointer('pointermove', surface(), at(18, 112));
    expect(label()?.textContent).toBe(`${bars[18].date}  112`);
    const draft = container.querySelector('[stroke-dasharray]')!;
    expect(draft.getAttribute('x2')).toBe(String(projection.x(18)));
  });

  it('only the main button places a point, and the press does not reach the chart below', () => {
    const below = vi.fn();
    renderInside(below, { drawing: 'support' });
    pointerWith('pointerdown', surface(), at(3, 110), { button: 2 });
    pointerWith('pointerup', surface(), at(3, 110), { button: 2 });
    pointer('pointerdown', surface(), at(18, 112));
    pointer('pointerup', surface(), at(18, 112));
    expect(handlers.onCreate).not.toHaveBeenCalled();
    expect(below).toHaveBeenCalledTimes(1); // only the right-button press
  });

  it('a release a few pixels from the press is still a click; a stray release later is not a drag', () => {
    render({ drawing: 'support' });
    pointer('pointerdown', surface(), at(3, 110));
    pointer('pointerup', surface(), { clientX: projection.x(3) + 6, clientY: projection.y(110) });
    expect(handlers.onCreate).not.toHaveBeenCalled();

    pointer('pointerup', surface(), at(18, 112));
    expect(handlers.onCreate).not.toHaveBeenCalled();
  });
});
