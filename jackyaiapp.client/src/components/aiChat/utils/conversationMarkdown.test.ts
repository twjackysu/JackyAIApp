import { describe, expect, it } from 'vitest';

import { Message } from '../types';

import {
  canExportConversation,
  conversationFileName,
  conversationMarkdown,
  taipeiMinute,
} from './conversationMarkdown';

const at = new Date('2026-10-26T21:55:00Z'); // 2026-10-27 05:55 in Taipei
const options = {
  title: '持股的外資買賣超',
  assistant: 'Studio AI',
  exportedAt: at,
  origin: 'https://apps.jacky-studio.com',
};
const user = (content: string, extra: Partial<Message> = {}): Message => ({
  id: `u-${content}`,
  content,
  isUser: true,
  timestamp: at,
  ...extra,
});
const bot = (content: string, extra: Partial<Message> = {}): Message => ({
  id: `b-${content}`,
  content,
  isUser: false,
  timestamp: at,
  ...extra,
});

describe('conversationMarkdown (D-147)', () => {
  it('writes the title, when, and each question and answer under its heading', () => {
    const md = conversationMarkdown(
      [user('台積電最近怎樣？'), bot('**營收**創新高。\n\n- 毛利率 59%')],
      options,
    );
    expect(md).toBe(
      [
        '# 持股的外資買賣超',
        '',
        '> 匯出自 Studio AI，2026-10-27 05:55（台北時間）',
        '',
        '## 你',
        '',
        '台積電最近怎樣？',
        '',
        '## Studio AI',
        '',
        '**營收**創新高。\n\n- 毛利率 59%',
        '',
      ].join('\n'),
    );
  });

  it('lists the tools an answer used once each, and draws its table and link cards', () => {
    const md = conversationMarkdown(
      [
        user('我的持股', {
          attachments: [
            { id: 1, fileName: '成交.png', kind: 'image', charCount: 0, truncated: false },
          ],
        }),
        bot('你有兩檔。', {
          toolSteps: [
            {
              key: 't1',
              id: 't1',
              name: 'portfolio_summary',
              label: '投資組合',
              status: 'done',
              card: {
                kind: 'table',
                title: '持股',
                columns: [
                  { key: 'name', label: '名稱', numeric: false },
                  { key: 'value', label: '市值', numeric: true },
                ],
                rows: [
                  { name: 'A|B\n公司', value: 1200 },
                  { name: '台積電', value: null },
                ],
                totalRows: 5,
              },
            },
            { key: 't2', id: 't2', name: 'portfolio_summary', label: '投資組合', status: 'done' },
            {
              key: 't3',
              id: 't3',
              name: 'open_page',
              label: '開啟頁面',
              status: 'done',
              card: {
                kind: 'link',
                title: '投資組合 [頁面]',
                description: '看完整\n明細',
                href: '/portfolio',
                actionLabel: '前往',
              },
            },
          ],
        }),
      ],
      options,
    );
    expect(md).toContain('附檔：成交.png');
    expect(md).toContain('*用了：投資組合、開啟頁面*');
    expect(md).toContain(
      [
        '**持股**',
        '',
        '| 名稱 | 市值 |',
        '| --- | ---: |',
        '| A\\|B 公司 | 1,200 |',
        '| 台積電 | — |',
        '',
        '（共 5 筆，這裡列出 2 筆）',
      ].join('\n'),
    );
    expect(md).toContain(
      '[投資組合 \\[頁面\\]](<https://apps.jacky-studio.com/portfolio>)：看完整 明細',
    );
    // The cards come before the answer's text, as on screen.
    expect(md.indexOf('| 名稱')).toBeLessThan(md.indexOf('你有兩檔。'));
  });

  it("keeps a reopened answer's stored cards, and names a card it cannot write", () => {
    const md = conversationMarkdown(
      [
        user('考我'),
        bot('來吧', {
          cards: [
            { kind: 'quiz', title: '複習小測驗', data: {} },
            {
              kind: 'link',
              title: 'Docs',
              description: null,
              href: 'https://example.com/a b',
              actionLabel: null,
            },
          ],
        }),
      ],
      options,
    );
    expect(md).toContain('（「複習小測驗」在對話裡顯示，這裡沒有匯出）');
    expect(md).toContain('[Docs](<https://example.com/a b>)');
  });

  it('says an answer was cut, and leaves out one with nothing in it yet', () => {
    const md = conversationMarkdown(
      [user('長文'), bot('很長…', { truncated: true }), user('再一個'), bot('')],
      options,
    );
    expect(md).toContain('很長…\n\n*（回答太長，在這裡被截斷）*');
    expect(md.match(/## Studio AI/g)).toHaveLength(1);
  });

  it('falls back to a plain title, and keeps a title on one line', () => {
    expect(conversationMarkdown([user('hi')], { ...options, title: undefined })).toMatch(
      /^# 對話\n/,
    );
    expect(conversationMarkdown([user('hi')], { ...options, title: 'a\nb' })).toMatch(/^# a b\n/);
  });
});

describe('canExportConversation', () => {
  it('needs a question', () => {
    expect(canExportConversation([])).toBe(false);
    expect(canExportConversation([bot('歡迎')])).toBe(false);
    expect(canExportConversation([user('hi')])).toBe(true);
  });
});

describe('conversationFileName', () => {
  it('is the title without what file systems refuse', () => {
    expect(conversationFileName('Q3/Q4 營收: 比較?', at)).toBe('Q3 Q4 營收 比較.md');
    expect(conversationFileName('x'.repeat(80), at)).toBe(`${'x'.repeat(60)}.md`);
  });

  it('is the date in Taipei without a title', () => {
    expect(conversationFileName(undefined, at)).toBe('對話 2026-10-27.md');
    expect(conversationFileName(' / ', at)).toBe('對話 2026-10-27.md');
  });
});

describe('taipeiMinute', () => {
  it('turns at midnight in Taipei, 16:00 UTC', () => {
    expect(taipeiMinute(new Date('2026-10-26T15:59:00Z'))).toBe('2026-10-26 23:59');
    expect(taipeiMinute(new Date('2026-10-26T16:00:00Z'))).toBe('2026-10-27 00:00');
  });
});
