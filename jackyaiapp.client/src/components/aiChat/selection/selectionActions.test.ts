import { describe, expect, it } from 'vitest';

import {
  dictionaryPathOf,
  draftWithSelection,
  isMostlyEnglish,
  SELECTION_MAX,
  selectedTextOf,
  selectedWordOf,
  selectionActionsFor,
  selectionMessage,
  selectionStepOf,
} from './selectionActions';

describe('selectedTextOf', () => {
  it('makes runs of spaces and line breaks one space, trimmed', () => {
    expect(selectedTextOf('  The market\n\n  rallied\ttoday. ')).toBe('The market rallied today.');
  });

  it('has nothing to ask about one character or only spaces', () => {
    expect(selectedTextOf(' a ')).toBeNull();
    expect(selectedTextOf('\n \t')).toBeNull();
    expect(selectedTextOf('台')).toBeNull();
    expect(selectedTextOf('台積')).toBe('台積');
  });

  it('cuts a long selection, with an ellipsis', () => {
    const text = selectedTextOf('字'.repeat(SELECTION_MAX + 50))!;
    expect([...text]).toHaveLength(SELECTION_MAX + 1);
    expect(text.endsWith('…')).toBe(true);
  });
});

describe('selectedWordOf', () => {
  it('takes one English word, lowercase, without the punctuation around it', () => {
    expect(selectedWordOf('Serendipity')).toBe('serendipity');
    expect(selectedWordOf('"resilient,"')).toBe('resilient');
    expect(selectedWordOf("don't")).toBe("don't");
    expect(selectedWordOf('well-known.')).toBe('well-known');
  });

  it('is not a word: two words, digits, Chinese, a dangling hyphen', () => {
    expect(selectedWordOf('ice cream')).toBeUndefined();
    expect(selectedWordOf('0050')).toBeUndefined();
    expect(selectedWordOf('單字')).toBeUndefined();
    expect(selectedWordOf('pre-')).toBeUndefined();
    expect(selectedWordOf('a')).toBeUndefined();
  });
});

describe('isMostlyEnglish', () => {
  it('reads which language the selection is mostly in', () => {
    expect(isMostlyEnglish('The quick brown fox jumps over the lazy dog.')).toBe(true);
    expect(isMostlyEnglish('台積電今天上漲 2%，外資買超')).toBe(false);
    // An abbreviation in a Chinese sentence keeps it Chinese.
    expect(isMostlyEnglish('這檔 ETF 的 EPS 表現不錯')).toBe(false);
  });
});

describe('selectionActionsFor', () => {
  it('explains a word instead of saving it for an assistant without the repository tool', () => {
    expect(selectionActionsFor('ETF', { canSaveWord: false }).map((a) => a.kind)).toEqual([
      'explain',
      'dictionary',
      'ask',
    ]);
    expect(
      selectionActionsFor('外資連續三天買超', { canSaveWord: false }).map((a) => a.kind),
    ).toEqual(['explain', 'translate', 'ask']);
  });

  it('offers the dictionary and the repository for one English word', () => {
    expect(selectionActionsFor('resilient').map((a) => a.kind)).toEqual([
      'dictionary',
      'save-word',
      'ask',
    ]);
  });

  it('offers to explain, translate into the other language, or ask, for anything else', () => {
    expect(selectionActionsFor('The market rallied today.').map((a) => a.label)).toEqual([
      '解釋',
      '翻成中文',
      '問 AI',
    ]);
    expect(selectionActionsFor('外資連續三天買超').map((a) => a.label)).toEqual([
      '解釋',
      '翻成英文',
      '問 AI',
    ]);
  });
});

describe('selectionMessage', () => {
  it('quotes the selection in what it sends', () => {
    expect(selectionMessage('explain', '本益比 < 15')).toBe(
      '請解釋我在這一頁選取的這段：\n\n「本益比 < 15」',
    );
    expect(selectionMessage('translate', 'It never rains but it pours.')).toContain(
      '翻成自然的繁體中文',
    );
    expect(selectionMessage('translate', '外資連續三天買超')).toBe(
      '把這段翻成自然的英文：\n\n「外資連續三天買超」',
    );
  });

  it('asks for the word itself to be saved, and leaves a question for the reader to finish', () => {
    expect(selectionMessage('save-word', 'Resilient,')).toBe('把「resilient」加入我的單字庫');
    expect(selectionMessage('ask', '殖利率')).toBe('關於「殖利率」，');
  });
});

describe('dictionaryPathOf', () => {
  it("is the word's dictionary page, or nothing", () => {
    expect(dictionaryPathOf('Serendipity')).toBe('/english/dictionary/serendipity');
    expect(dictionaryPathOf("don't")).toBe("/english/dictionary/don't");
    expect(dictionaryPathOf('two words')).toBeUndefined();
  });
});

describe('selectionStepOf', () => {
  it("goes to a word's dictionary page, and does nothing for a dictionary press on anything else", () => {
    expect(selectionStepOf('dictionary', 'Resilient', false)).toEqual({
      do: 'go',
      path: '/english/dictionary/resilient',
    });
    expect(selectionStepOf('dictionary', 'two words', false)).toBeNull();
  });

  it('puts 問 AI in the box, as page text only from the page', () => {
    expect(selectionStepOf('ask', '本益比', false)).toEqual({
      do: 'draft',
      text: '關於「本益比」，',
      quotesPageText: true,
    });
    expect(selectionStepOf('ask', '本益比', true)).toMatchObject({ quotesPageText: false });
  });

  it("sends the rest at once, from the page or from the assistant's own answer", () => {
    expect(selectionStepOf('explain', '本益比', false)).toEqual({
      do: 'send',
      text: selectionMessage('explain', '本益比'),
      fromPage: true,
    });
    expect(selectionStepOf('save-word', 'resilient', true)).toEqual({
      do: 'send',
      text: '把「resilient」加入我的單字庫',
      fromPage: false,
    });
  });
});

describe('draftWithSelection', () => {
  it('comes after what was typed, never in place of it', () => {
    expect(draftWithSelection('', '關於「x」，')).toBe('關於「x」，');
    expect(draftWithSelection('2330  \n', '關於「x」，')).toBe('2330\n關於「x」，');
  });
});
