import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { useWaitingTitle, WAITING_TITLE_MARK } from './useWaitingTitle';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let container: HTMLDivElement;
let root: Root;

function Probe({ studio, page }: { studio: boolean; page: boolean }) {
  return (
    <>
      <Waiting active={studio} />
      <Waiting active={page} />
    </>
  );
}

function Waiting({ active }: { active: boolean }) {
  useWaitingTitle(active);
  return null;
}

const render = (studio: boolean, page = false) =>
  act(() => root.render(<Probe studio={studio} page={page} />));

beforeEach(() => {
  document.title = "Jacky's Studio";
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
});

describe('useWaitingTitle', () => {
  it('marks the title while a card waits and takes the mark off after', () => {
    render(false);
    expect(document.title).toBe("Jacky's Studio");
    render(true);
    expect(document.title).toBe(`${WAITING_TITLE_MARK}Jacky's Studio`);
    render(false);
    expect(document.title).toBe("Jacky's Studio");
  });

  it('marks once for two chats waiting, and keeps it until neither does', () => {
    render(true, true);
    expect(document.title).toBe(`${WAITING_TITLE_MARK}Jacky's Studio`);
    render(false, true);
    expect(document.title).toBe(`${WAITING_TITLE_MARK}Jacky's Studio`);
    render(false, false);
    expect(document.title).toBe("Jacky's Studio");
  });

  it("marks the title a page sets meanwhile, and leaves that page's title after", async () => {
    render(true);
    document.title = 'Another page';
    // The observer runs as a microtask.
    await act(async () => {});
    expect(document.title).toBe(`${WAITING_TITLE_MARK}Another page`);
    render(false);
    expect(document.title).toBe('Another page');
  });

  it('takes the mark off when the chat goes away', () => {
    render(true);
    act(() => root.unmount());
    root = createRoot(container);
    expect(document.title).toBe("Jacky's Studio");
  });
});
