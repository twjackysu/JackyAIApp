import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { ASK_STUDIO_EVENT, FIND_MY_WEAK_SPOTS, askedOf } from './askStudio';
import AskStudioCard from './AskStudioCard';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let container: HTMLDivElement;
let root: Root;
const heard = vi.fn((e: Event) => askedOf(e));

beforeEach(() => {
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
  window.addEventListener(ASK_STUDIO_EVENT, heard);
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
  window.removeEventListener(ASK_STUDIO_EVENT, heard);
  heard.mockClear();
});

describe('AskStudioCard', () => {
  it('asks nothing until pressed, then asks Studio AI in a new conversation', () => {
    act(() =>
      root.render(
        <AskStudioCard
          icon={<span />}
          title="AI 弱點分析"
          description="說明"
          ask={FIND_MY_WEAK_SPOTS}
        />,
      ),
    );
    expect(container.textContent).toContain('AI 弱點分析');
    expect(container.textContent).toContain('說明');
    expect(heard).not.toHaveBeenCalled();

    act(() => container.querySelector('button')!.click());

    expect(heard).toHaveReturnedWith({ text: FIND_MY_WEAK_SPOTS, fresh: true });
  });
});
