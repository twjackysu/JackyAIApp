import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { LazyMount } from './LazyMount';

// React 19's `act` (imported from 'react' below) warns unless this is set — normally done by
// a testing-library setup file, which this project doesn't have. Same pattern as
// `Finance/hooks/useFinanceAnalysis.test.tsx`.
(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

/** jsdom has no IntersectionObserver — stub one whose `trigger()` simulates the browser firing
 * it, so tests control exactly when a wrapper is considered "in view". */
function stubIntersectionObserver() {
  let currentCallback: IntersectionObserverCallback | null = null;
  let observedEl: Element | null = null;
  const disconnect = vi.fn();

  class FakeIntersectionObserver {
    constructor(callback: IntersectionObserverCallback) {
      currentCallback = callback;
    }
    observe(el: Element) {
      observedEl = el;
    }
    disconnect() {
      disconnect();
    }
    unobserve() {}
    takeRecords() {
      return [];
    }
  }

  vi.stubGlobal('IntersectionObserver', FakeIntersectionObserver);

  return {
    trigger(isIntersecting: boolean) {
      act(() => {
        currentCallback?.(
          [{ isIntersecting, target: observedEl } as IntersectionObserverEntry],
          {} as IntersectionObserver,
        );
      });
    },
    disconnect,
  };
}

describe('LazyMount', () => {
  let container: HTMLDivElement;
  let root: Root;

  beforeEach(() => {
    container = document.createElement('div');
    document.body.appendChild(container);
  });

  afterEach(() => {
    act(() => root.unmount());
    container.remove();
    vi.unstubAllGlobals();
  });

  it('does not mount the child (so it never fires a query) before the section enters the viewport', () => {
    const io = stubIntersectionObserver();
    act(() => {
      root = createRoot(container);
      root.render(
        <LazyMount minHeight={240}>
          <div data-testid="chart">CHART</div>
        </LazyMount>,
      );
    });

    expect(container.querySelector('[data-testid="chart"]')).toBeNull();

    io.trigger(true);
    expect(container.querySelector('[data-testid="chart"]')).not.toBeNull();
    expect(container.textContent).toContain('CHART');
  });

  it('reserves minHeight on the placeholder so the layout does not jump while scrolled past', () => {
    stubIntersectionObserver();
    act(() => {
      root = createRoot(container);
      root.render(
        <LazyMount minHeight={321}>
          <div>CHART</div>
        </LazyMount>,
      );
    });

    const placeholder = container.firstElementChild as HTMLElement;
    expect(getComputedStyle(placeholder).minHeight).toBe('321px');
  });

  it('ignores a non-intersecting callback and only mounts once isIntersecting is true', () => {
    const io = stubIntersectionObserver();
    act(() => {
      root = createRoot(container);
      root.render(
        <LazyMount minHeight={240}>
          <div data-testid="chart">CHART</div>
        </LazyMount>,
      );
    });

    io.trigger(false);
    expect(container.querySelector('[data-testid="chart"]')).toBeNull();

    io.trigger(true);
    expect(container.querySelector('[data-testid="chart"]')).not.toBeNull();
  });

  it('mounts immediately when IntersectionObserver is unavailable', () => {
    vi.stubGlobal('IntersectionObserver', undefined);
    act(() => {
      root = createRoot(container);
      root.render(
        <LazyMount minHeight={240}>
          <div data-testid="chart">CHART</div>
        </LazyMount>,
      );
    });

    expect(container.querySelector('[data-testid="chart"]')).not.toBeNull();
  });
});
