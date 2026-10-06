import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';

import LoginGate from './LoginGate';

describe('LoginGate', () => {
  it('shows value, free note and a login link returning to the current path', () => {
    const html = renderToStaticMarkup(
      <MemoryRouter initialEntries={['/english/exam']}>
        <LoginGate title="英文測驗" description="說明" bullets={['重點一']} />
      </MemoryRouter>,
    );
    expect(html).toContain('英文測驗');
    expect(html).toContain('重點一');
    expect(html).toContain('完全免費');
    expect(html).toContain('ReturnUrl=%2Fenglish%2Fexam');
  });
});
