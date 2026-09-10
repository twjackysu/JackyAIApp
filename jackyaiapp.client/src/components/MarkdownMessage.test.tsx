import { createTheme } from '@mui/material/styles';
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';

import MarkdownMessage from './MarkdownMessage';

const theme = createTheme();

function render(content: string) {
  return renderToStaticMarkup(
    <MarkdownMessage content={content} isOwnMessage={false} theme={theme} />,
  );
}

/** 抓出 markup 中第一個 <pre ...> 的開標籤，用來看它有沒有被套上樣式。 */
function preTag(html: string): string | null {
  return /<pre[^>]*>/.exec(html)?.[0] ?? null;
}

describe('MarkdownMessage 的程式碼呈現', () => {
  it('fenced block 的捲動容器是被套過樣式的 pre', () => {
    // 迴歸測試：先前 `node.tagName !== 'pre'` 恆為 true，區塊分支從未執行，
    // 外層 pre 是 react-markdown 產生的裸元素（沒有任何 class），
    // overflow:auto 掛在內層 code 上因而失效，長行無法橫向捲動。
    const html = render('```sql\nSELECT 1;\n```');
    const tag = preTag(html);
    expect(tag).not.toBeNull();
    expect(tag).toMatch(/class="/);
  });

  it('有註冊的語言會上色，並保留 hljs 與 language class 供主題 CSS 套用', () => {
    const html = render('```sql\nSELECT 1;\n```');
    expect(html).toContain('hljs');
    expect(html).toContain('language-sql');
    expect(html).toContain('hljs-keyword');
  });

  it('未註冊的語言原樣輸出，不會報錯也不會有 hljs class', () => {
    const html = render('```rust\nfn main() {}\n```');
    expect(html).toContain('fn main()');
    expect(html).not.toContain('hljs');
  });

  it('沒有標語言的 fenced block 仍然顯示內容', () => {
    const html = render('```\njust text\n```');
    expect(html).toContain('just text');
    expect(preTag(html)).toMatch(/class="/);
  });

  it('保留區塊內的換行', () => {
    const html = render('```\nfirst\nsecond\n```');
    expect(html).toContain('first\nsecond');
  });

  it('行內 code 不會被包進 pre', () => {
    const html = render('這是 `inline` 程式碼');
    expect(html).toContain('inline');
    expect(preTag(html)).toBeNull();
    expect(html).not.toContain('hljs');
  });

  it('未上色的內容交給 React escape，不會注入標記', () => {
    // 上色路徑走 dangerouslySetInnerHTML，所以未上色路徑的 escape 是安全性前提。
    const html = render('```\n<img src=x onerror=alert(1)>\n```');
    expect(html).not.toContain('<img');
    expect(html).toContain('&lt;img');
  });

  it('上色路徑的內容同樣是 escape 過的', () => {
    const html = render('```xml\n<img src="x">\n```');
    // highlight.js 會把角括號 escape 後再插入自己的 span，所以 `&lt;` 與 `img` 之間
    // 隔著標籤、不是連續字串 —— 這裡驗的是「原始標記沒有被還原成真的元素」。
    expect(html).not.toContain('<img');
    expect(html).toContain('&lt;');
    expect(html).toContain('hljs-tag');
  });
});
