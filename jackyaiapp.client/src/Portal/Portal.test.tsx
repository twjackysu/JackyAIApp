import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import Portal from './index';

const mocks = vi.hoisted(() => ({
  user: vi.fn(),
  stats: vi.fn(),
  due: vi.fn(),
  history: vi.fn(),
}));

vi.mock('@/apis/accountApis', () => ({ useGetUserInfoQuery: mocks.user }));
vi.mock('@/apis/dailyChallengeApis', () => ({ useGetStatsQuery: mocks.stats }));
vi.mock('@/apis/reviewApis', () => ({ useGetDueCountQuery: mocks.due }));
vi.mock('@/apis/portfolioApis', () => ({ useGetUpdateHistoryQuery: mocks.history }));

const ok = <T,>(data: T) => ({ data: { data }, isLoading: false, isError: false });
const failed = { data: undefined, isLoading: false, isError: true };

const stats = (over = {}) =>
  ok({ currentStreak: 5, todayCompleted: false, todayScore: null, ...over });

function render() {
  return renderToStaticMarkup(
    <MemoryRouter>
      <Portal />
    </MemoryRouter>,
  );
}

describe('Portal', () => {
  beforeEach(() => {
    mocks.user.mockReturnValue(ok({ name: 'Jacky', email: 'j@x.com' }));
    mocks.stats.mockReturnValue(stats());
    mocks.due.mockReturnValue(ok({ dueCount: 7 }));
    mocks.history.mockReturnValue(ok([{ snapshotId: 1, positionCount: 3 }]));
  });

  it('logged in with due reviews: shows today items linking to their pages', () => {
    const html = render();
    expect(html).toContain('7 個單字待複習');
    expect(html).toContain('href="/english/review"');
    expect(html).toContain('開始今日挑戰');
    expect(html).toContain('連續 5 天');
    expect(html).toContain('href="/portfolio"');
    expect(html).not.toContain('使用 Google 登入');
  });

  it('logged in: offers the AI briefing, which asks nothing until clicked', () => {
    const asked = vi.fn();
    window.addEventListener('studio:ask', asked);
    const html = render();
    window.removeEventListener('studio:ask', asked);
    expect(html).toContain('AI 今日簡報');
    expect(asked).not.toHaveBeenCalled();
  });

  it('logged in: the hero names Studio AI and its shortcut; a guest reads the apps line', () => {
    const html = render();
    expect(html).toContain('⌘K（Ctrl+K）');
    expect(html).not.toContain('想切換時再回到這個目錄即可');
    mocks.user.mockReturnValue({ data: undefined, isLoading: false, isError: true });
    const guest = render();
    expect(guest).toContain('想切換時再回到這個目錄即可');
    expect(guest).not.toContain('Studio AI');
  });

  it('logged in, nothing due: hides the review item and shows completed challenge', () => {
    mocks.due.mockReturnValue(ok({ dueCount: 0 }));
    mocks.stats.mockReturnValue(stats({ todayCompleted: true, todayScore: 4 }));
    const html = render();
    expect(html).not.toContain('待複習');
    expect(html).toContain('今日挑戰已完成');
    expect(html).toContain('得分 4');
  });

  it('logged in without holdings: hides the portfolio item; failures hide only their item', () => {
    mocks.history.mockReturnValue(ok([]));
    mocks.stats.mockReturnValue(failed);
    const html = render();
    expect(html).not.toContain('查看投資組合');
    expect(html).not.toContain('今日挑戰');
    expect(html).toContain('7 個單字待複習');
  });

  it('guest: shows value line + login, no today block, English Coach first', () => {
    mocks.user.mockReturnValue({ data: undefined, isLoading: false, isError: true });
    const html = render();
    expect(html).toContain('使用 Google 登入');
    expect(html).not.toContain('aria-label="今日"');
    expect(html).not.toContain('AI 今日簡報');
    expect(html.indexOf('English Coach')).toBeLessThan(html.indexOf('Wealth Portfolio'));
    expect(html).toContain('Job Finder'); // other apps stay visible
  });
});
