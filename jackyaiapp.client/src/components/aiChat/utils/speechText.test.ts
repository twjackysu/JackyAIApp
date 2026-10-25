import { describe, expect, it } from 'vitest';

import { plainTextOf, speechSegmentsOf } from './speechText';

describe('plainTextOf', () => {
  it('reads the words, not the Markdown', () => {
    expect(plainTextOf('## 重點\n\n- **serendipity** 是 *運氣*\n> quoted')).toBe(
      '重點 serendipity 是 運氣 quoted',
    );
  });

  it('keeps a link’s words and drops its address, images, code blocks and emoji', () => {
    expect(
      plainTextOf(
        '看 [字典](/english/dictionary) 📅\n![圖](x.png)\n```js\nconst a = 1;\n```\nhttps://x.example/a 完',
      ),
    ).toBe('看 字典 完');
  });

  it('keeps a word written in code, and the underscores inside a name', () => {
    expect(plainTextOf('用 `look_up_word` 查')).toBe('用 look_up_word 查');
  });

  it('reads a table’s cells, not its rule', () => {
    expect(plainTextOf('| 字 | 意思 |\n|---|:---:|\n| lucid | 清楚的 |')).toBe(
      '， 字 ， 意思 ， ， lucid ， 清楚的 ，',
    );
  });
});

describe('speechSegmentsOf', () => {
  it('reads English in English and Chinese in Chinese', () => {
    expect(
      speechSegmentsOf('> It was pure **serendipity**.\n\n**中文翻譯**：純屬意外的幸運。'),
    ).toEqual([
      { text: 'It was pure serendipity.', lang: 'en-US' },
      { text: '中文翻譯：純屬意外的幸運。', lang: 'zh-TW' },
    ]);
  });

  it('keeps numbers in the run they are in, and drops runs of punctuation alone', () => {
    expect(speechSegmentsOf('共 86 個字：serendipity、ephemeral')).toEqual([
      { text: '共 86 個字：', lang: 'zh-TW' },
      { text: 'serendipity ephemeral', lang: 'en-US' },
    ]);
  });

  it('reads nothing of an answer with no words', () => {
    expect(speechSegmentsOf('```\ncode\n```\n📈')).toEqual([]);
  });

  it('reads at most the first part of a long answer', () => {
    const segments = speechSegmentsOf('a'.repeat(5000), 100);
    expect(segments).toEqual([{ text: 'a'.repeat(100), lang: 'en-US' }]);
  });

  it('keeps an abbreviation in a Chinese sentence in the Chinese voice', () => {
    expect(speechSegmentsOf('這檔 ETF 的 EPS 不錯')).toEqual([
      { text: '這檔 ETF 的 EPS 不錯', lang: 'zh-TW' },
    ]);
  });

  it('cuts a long run at its sentence ends', () => {
    const sentence = 'This is a sentence of words. ';
    const segments = speechSegmentsOf(sentence.repeat(20));
    expect(segments.length).toBeGreaterThan(1);
    expect(segments.every((s) => s.text.length <= 200 && s.text.endsWith('.'))).toBe(true);
  });
});

describe('plainTextOf, with what is not Markdown', () => {
  it('keeps comparisons and multiplication', () => {
    expect(plainTextOf('本益比 < 15 且殖利率 > 5%')).toBe('本益比 < 15 且殖利率 > 5%');
    expect(plainTextOf('3*4 = 12, 5*6 = 30')).toBe('3*4 = 12, 5*6 = 30');
    expect(plainTextOf('a *word* here')).toBe('a word here');
  });
});
