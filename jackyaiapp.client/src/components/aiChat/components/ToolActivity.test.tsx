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

  it('says how long a finished call took, not one still running or one the server did not time', () => {
    act(() =>
      root.render(
        <ToolActivity
          steps={[
            { ...step('a', '查字典', 'done'), durationMs: 812 },
            { ...step('b', '彙總資料', 'failed'), durationMs: 12_400 },
            { ...step('c', 'stock history', 'running'), durationMs: 3000 },
            step('d', 'realtime quote', 'done'),
          ]}
        />,
      ),
    );

    const items = [...container.querySelectorAll('li')].map((li) => li.textContent);
    expect(items).toEqual([
      '查字典· 0.8 秒（完成）',
      '彙總資料· 12 秒（失敗）',
      'stock history（查詢中）',
      'realtime quote（完成）',
    ]);
  });
});
