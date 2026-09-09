import { describe, expect, it } from 'vitest';

import { highlightToHtml, resolveLanguage } from './syntaxHighlight';

describe('resolveLanguage', () => {
  it('讀出 react-markdown 給的 language-xxx class', () => {
    expect(resolveLanguage('language-json')).toBe('json');
  });

  it('接受 highlight.js 自己宣告的 alias', () => {
    expect(resolveLanguage('language-js')).toBe('js');
    expect(resolveLanguage('language-html')).toBe('html');
  });

  it('沒有 class 或沒標語言時回傳 null（行內 code 走這條）', () => {
    expect(resolveLanguage(undefined)).toBeNull();
    expect(resolveLanguage('')).toBeNull();
    expect(resolveLanguage('MuiBox-root')).toBeNull();
  });

  it('未註冊的語言回傳 null，不會丟例外', () => {
    expect(resolveLanguage('language-brainfuck')).toBeNull();
  });
});

describe('highlightToHtml', () => {
  it('已註冊的語言會被上色', () => {
    const html = highlightToHtml('const a = 1;', 'language-js');
    expect(html).toContain('hljs-keyword');
    expect(html).toContain('const');
  });

  it('輸出一定經過 HTML escape，才能安全地丟給 dangerouslySetInnerHTML', () => {
    const html = highlightToHtml('<img src=x onerror=alert(1)>', 'language-json');
    expect(html).not.toBeNull();
    expect(html).not.toContain('<img');
    expect(html).toContain('&lt;img');
  });

  it('未註冊的語言回傳 null，讓呼叫端改輸出純文字', () => {
    expect(highlightToHtml('fn main() {}', 'language-rust')).toBeNull();
  });
});
