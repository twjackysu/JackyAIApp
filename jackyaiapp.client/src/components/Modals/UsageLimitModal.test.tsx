import { act } from 'react';
import { createRoot, Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import UsageLimitModal from './UsageLimitModal';

const mocks = vi.hoisted(() => ({ user: vi.fn(), open: true }));
vi.mock('@/apis/accountApis', () => ({ useGetUserInfoQuery: mocks.user }));
vi.mock('@/redux/hooks', () => ({
  useAppDispatch: () => vi.fn(),
  useAppSelector: () => mocks.open,
}));

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let container: HTMLDivElement;
let root: Root;

// MUI renders the dialog in a portal, so the text is read from the document, not `container`.
async function render() {
  await act(async () => {
    root.render(<UsageLimitModal />);
  });
  return document.body.textContent ?? '';
}

describe('UsageLimitModal', () => {
  beforeEach(() => {
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
    mocks.user.mockReturnValue({ data: { data: { id: 'u1', email: 'someone@example.com' } } });
  });

  afterEach(async () => {
    await act(async () => root.unmount());
    container.remove();
  });

  it('說明額度是作者自費的免費試用、何時重置，並請使用者贊助或來信', async () => {
    const text = await render();
    expect(text).toContain('今天的免費額度用完了');
    expect(text).toContain('作者自掏腰包');
    expect(text).toContain('台灣時間每天早上 8');
    expect(text).toContain('歡迎贊助作者');
    expect(text).toContain('jacky19918@gmail.com');
  });

  it('附上使用者自己的登入帳號，方便作者找到要調整的帳號', async () => {
    expect(await render()).toContain('someone@example.com');
  });

  it('還沒有贊助頁時，不顯示贊助按鈕', async () => {
    await render();
    const buttons = Array.from(document.body.querySelectorAll('a, button')).map(
      (b) => b.textContent,
    );
    expect(buttons).not.toContain('贊助作者');
  });
});
