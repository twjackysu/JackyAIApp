import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import ToolActivity from './ToolActivity';

import type { ToolStep } from '../utils/toolActivity';

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
});

const step = (key: string, label: string, status: ToolStep['status']): ToolStep => ({
  key,
  name: key,
  label,
  status,
});

describe('ToolActivity', () => {
  it('draws nothing for an answer that used no tool', () => {
    act(() => root.render(<ToolActivity steps={[]} />));

    expect(container.querySelector('[data-testid="tool-activity"]')).toBeNull();
  });

  it('lists each call with its label and where it is', () => {
    act(() =>
      root.render(
        <ToolActivity
          steps={[
            step('a', 'realtime quote', 'done'),
            step('b', 'stock history', 'running'),
            step('c', '彙總資料', 'failed'),
          ]}
        />,
      ),
    );

    const items = [...container.querySelectorAll('li')].map((li) => li.textContent);
    expect(items).toEqual([
      'realtime quote（完成）',
      'stock history（查詢中）',
      '彙總資料（失敗）',
    ]);
    expect(container.querySelector('[role="progressbar"]')).not.toBeNull();
  });
});
