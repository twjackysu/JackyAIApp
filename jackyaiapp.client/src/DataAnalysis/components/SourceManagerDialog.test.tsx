import { act } from 'react';
import { createRoot, Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { SourceManagerDialog } from './SourceManagerDialog';

const mocks = vi.hoisted(() => ({ create: vi.fn() }));
vi.mock('../../apis/dataAnalysisApis', () => ({
  useGetSourcesQuery: () => ({ data: { data: [] }, isLoading: false }),
  useCreateSourceMutation: () => [mocks.create, { isLoading: false }],
  useUpdateSourceMutation: () => [vi.fn(), { isLoading: false }],
  useDeleteSourceMutation: () => [vi.fn()],
}));

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const FALLBACK = '儲存失敗 — 請檢查連線資訊後再試一次。';

let container: HTMLDivElement;
let root: Root;

// MUI renders the dialog in a portal, so everything is queried from the document, not `container`.
function button(label: string) {
  return Array.from(document.body.querySelectorAll('button')).find((b) =>
    b.textContent?.includes(label),
  )!;
}

// React tracks the value itself, so a plain `input.value = …` is swallowed; go through the
// native setter and fire the event React listens to.
async function type(input: HTMLInputElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!;
  await act(async () => {
    setter.call(input, value);
    input.dispatchEvent(new Event('input', { bubbles: true }));
  });
}

async function saveWith(rejection: unknown) {
  mocks.create.mockReturnValue({ unwrap: () => Promise.reject(rejection) });
  await act(async () => {
    root.render(<SourceManagerDialog open onClose={() => {}} />);
  });
  await act(async () => {
    button('新增來源').click();
  });
  await type(
    document.body.querySelector<HTMLInputElement>('input[placeholder="例如：My Production DB"]')!,
    'My DB',
  );
  await type(
    document.body.querySelector<HTMLInputElement>('input[type="password"]')!,
    'Server=s;Database=d;Authentication=ActiveDirectoryManagedIdentity',
  );
  await act(async () => {
    button('儲存').click();
  });
}

function alertText() {
  return document.body.querySelector('[role="alert"]')?.textContent;
}

describe('SourceManagerDialog 儲存失敗', () => {
  beforeEach(() => {
    mocks.create.mockReset();
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
  });
  afterEach(() => {
    act(() => root.unmount());
    container.remove();
  });

  it('後端以標準錯誤封包回 400：原樣顯示後端給的原因，不是通用那句', async () => {
    const message =
      'Authentication=ActiveDirectoryManagedIdentity is not allowed; use SQL login (SqlPassword), ActiveDirectoryPassword or ActiveDirectoryServicePrincipal.';
    await saveWith({ status: 400, data: { error: { code: 'BadRequest', message } } });

    expect(alertText()).toBe(message);
    expect(document.body.textContent).not.toContain(FALLBACK);
  });

  it('錯誤沒有可用訊息（500 ＋ HTML）：顯示原本那句 fallback', async () => {
    await saveWith({ status: 500, data: '<html><body>Bad Gateway</body></html>' });

    expect(alertText()).toBe(FALLBACK);
  });
});
