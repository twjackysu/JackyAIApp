import { describe, expect, it } from 'vitest';

import { readFencedCode } from './markdownCode';

/** 仿 react-markdown 傳進 `pre` renderer 的 hast 節點。 */
function preNode(codeText: string, className?: string[] | string) {
  return {
    type: 'element',
    tagName: 'pre',
    children: [
      {
        type: 'element',
        tagName: 'code',
        properties: className ? { className } : {},
        children: [{ type: 'text', value: codeText }],
      },
    ],
  };
}

describe('readFencedCode', () => {
  it('取出程式碼內容與語言 class', () => {
    const result = readFencedCode(preNode('SELECT 1;\n', ['language-sql']));
    expect(result).toEqual({ text: 'SELECT 1;\n', className: 'language-sql' });
  });

  it('沒有標語言時 className 是 undefined，不是空字串', () => {
    // 空字串會讓呼叫端輸出 class=""，也會讓 `filter(Boolean)` 之類的判斷失準。
    expect(readFencedCode(preNode('plain\n'))?.className).toBeUndefined();
    expect(readFencedCode(preNode('plain\n', []))?.className).toBeUndefined();
  });

  it('className 是字串型態時也接受', () => {
    // hast 通常給陣列，但不同 rehype 外掛組合下可能是字串。
    expect(readFencedCode(preNode('x', 'language-json'))?.className).toBe('language-json');
  });

  it('保留多行內容與行尾空白', () => {
    const code = 'line one\n  indented\n\nafter blank\n';
    expect(readFencedCode(preNode(code))?.text).toBe(code);
  });

  it('把被切成多段的 text 節點接回來，不是只取第一段', () => {
    const node = {
      type: 'element',
      tagName: 'pre',
      children: [
        {
          type: 'element',
          tagName: 'code',
          properties: {},
          children: [
            { type: 'text', value: 'first' },
            { type: 'text', value: '-second' },
          ],
        },
      ],
    };
    expect(readFencedCode(node)?.text).toBe('first-second');
  });

  it('不是 fenced block 的 pre 回傳 null，讓呼叫端能原樣輸出而不是顯示空白', () => {
    expect(readFencedCode(undefined)).toBeNull();
    expect(readFencedCode({ type: 'element', tagName: 'pre', children: [] })).toBeNull();
    expect(
      readFencedCode({
        type: 'element',
        tagName: 'pre',
        children: [{ type: 'text', value: 'bare text' }],
      }),
    ).toBeNull();
  });
});
