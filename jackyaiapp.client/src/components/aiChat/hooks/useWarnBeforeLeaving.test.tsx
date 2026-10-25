import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { useWarnBeforeLeaving } from './useWarnBeforeLeaving';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let container: HTMLDivElement;
let root: Root;

function Probe({ active }: { active: boolean }) {
  useWarnBeforeLeaving(active);
  return null;
}

const render = (active: boolean) => act(() => root.render(<Probe active={active} />));

/** Whether reloading or closing the page now would ask the reader first. */
const unloadIsAsked = () => {
  const event = new Event('beforeunload', { cancelable: true });
  window.dispatchEvent(event);
  return event.defaultPrevented;
};

beforeEach(() => {
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
});

describe('useWarnBeforeLeaving', () => {
  it('asks before leaving while active', () => {
    render(true);
    expect(unloadIsAsked()).toBe(true);
  });

  it('lets the page go when not active', () => {
    render(false);
    expect(unloadIsAsked()).toBe(false);
  });

  it('stops asking once no longer active, or once gone', () => {
    render(true);
    render(false);
    expect(unloadIsAsked()).toBe(false);

    render(true);
    act(() => root.unmount());
    root = createRoot(container);
    expect(unloadIsAsked()).toBe(false);
  });
});
