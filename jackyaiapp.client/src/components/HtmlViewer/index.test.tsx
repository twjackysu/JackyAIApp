import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { escapeHtml } from './escapeHtml';

import HtmlViewer from './index';

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

describe('HtmlViewer', () => {
  it('previews an answer in a sandboxed frame, never in the page', () => {
    const html = '<p>hi</p><img src="x" onerror="window.__xss = 1">';
    act(() => root.render(<HtmlViewer content={html} isComplete />));

    const frame = container.querySelector('iframe')!;
    expect(frame.getAttribute('sandbox')).toBe('');
    expect(frame.getAttribute('srcdoc')).toBe(html);
    expect(container.querySelector('img')).toBeNull();
    expect((window as unknown as { __xss?: number }).__xss).toBeUndefined();
  });

  it('escapes the code when it cannot be highlighted', () => {
    expect(escapeHtml('<img onerror="x">&')).toBe('&lt;img onerror="x"&gt;&amp;');
  });
});
