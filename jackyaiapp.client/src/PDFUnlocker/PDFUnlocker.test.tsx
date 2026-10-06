import { act } from 'react';
import { createRoot, Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import PDFUnlocker from './index';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let container: HTMLDivElement;
let root: Root;

const flush = () =>
  act(async () => {
    await new Promise((r) => setTimeout(r, 0));
  });

async function submitWith(response: Response | Error, fileName = 'bank.pdf') {
  const fetchMock = vi.fn(() =>
    response instanceof Error ? Promise.reject(response) : Promise.resolve(response),
  );
  vi.stubGlobal('fetch', fetchMock);
  await act(async () => root.render(<PDFUnlocker />));
  const input = container.querySelector('input[type=file]') as HTMLInputElement;
  const file = new File(['x'], fileName, { type: 'application/pdf' });
  Object.defineProperty(input, 'files', { value: [file], configurable: true });
  await act(async () => {
    input.dispatchEvent(new Event('change', { bubbles: true }));
  });
  const pw = container.querySelector('input[type=password]') as HTMLInputElement;
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!;
  await act(async () => {
    setter.call(pw, 'secret');
    pw.dispatchEvent(new Event('input', { bubbles: true }));
  });
  await act(async () => {
    container
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
  });
  await flush();
}

describe('PDFUnlocker', () => {
  beforeEach(() => {
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
  });
  afterEach(() => {
    act(() => root.unmount());
    container.remove();
    vi.unstubAllGlobals();
  });

  it('錯誤密碼：顯示後端的明確訊息', async () => {
    await submitWith(
      new Response(
        JSON.stringify({ error: { code: 1, message: 'Invalid password for the PDF file.' } }),
        {
          status: 401,
        },
      ),
    );
    expect(container.querySelector('[role=alert]')?.textContent).toContain('Invalid password');
  });

  it('網路失敗：顯示無法連線訊息', async () => {
    await submitWith(new TypeError('Failed to fetch'));
    expect(container.querySelector('[role=alert]')?.textContent).toContain(
      'Cannot reach the server',
    );
  });

  it('成功：顯示完成提示並以「原檔名-unlocked.pdf」下載', async () => {
    URL.createObjectURL = vi.fn(() => 'blob:x');
    URL.revokeObjectURL = vi.fn();
    const names: string[] = [];
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (
      this: HTMLAnchorElement,
    ) {
      names.push(this.download);
    });
    await submitWith(new Response('pdf', { status: 200 }));
    expect(names).toEqual(['bank-unlocked.pdf']);
    expect(container.textContent).toContain('downloaded');
  });
});
