import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import ToolPermissionToggle from './ToolPermissionToggle';

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

const labels = () => [...container.querySelectorAll('button')].map((b) => b.textContent);

describe('ToolPermissionToggle', () => {
  it('offers the three permissions, the current one pressed, and reports a change', () => {
    const onChange = vi.fn();
    act(() =>
      root.render(
        <ToolPermissionToggle
          toolName="search"
          effect="write"
          value="confirm"
          onChange={onChange}
        />,
      ),
    );

    expect(labels()).toEqual(['自動', '需確認', '停用']);
    const pressed = container.querySelector('button[aria-pressed="true"]');
    expect(pressed?.textContent).toBe('需確認');

    act(() => (container.querySelectorAll('button')[0] as HTMLButtonElement).click());
    expect(onChange).toHaveBeenCalledWith('auto');
  });

  it('a tool that cannot be undone has no 自動', () => {
    act(() =>
      root.render(
        <ToolPermissionToggle
          toolName="delete"
          effect="destructive"
          value="confirm"
          onChange={vi.fn()}
        />,
      ),
    );

    expect(labels()).toEqual(['需確認', '停用']);
  });
});
