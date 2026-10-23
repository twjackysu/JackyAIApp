import { describe, expect, it } from 'vitest';

import { htmlToMarkdown, insertPastedText, LONG_PASTE_CHARS, readPaste } from './paste';

const clipboard = (data: { text?: string; html?: string; files?: File[] }) => ({
  getData: (type: string) =>
    (type === 'text/plain' ? data.text : type === 'text/html' ? data.html : '') ?? '',
  files: (data.files ?? []) as unknown as FileList,
});

const at = new Date(2026, 9, 21, 9, 5, 7);

/** Markdown with runs of spaces (column padding, list indents) made single, to compare meaning. */
const squeeze = (md: string) => md.replace(/ {2,}/g, ' ');

describe('htmlToMarkdown', () => {
  it('keeps a table without a header row (as Excel copies one) as a table, its first row the header', () => {
    expect(
      squeeze(
        htmlToMarkdown(
          '<table><tr><td>代號</td><td>收盤</td></tr><tr><td>2330</td><td>1,050</td></tr></table>',
        ),
      ),
    ).toBe('| 代號 | 收盤 |\n| --- | --- |\n| 2330 | 1,050 |');
  });

  it('leaves a table that has its header cells as it is', () => {
    expect(
      squeeze(
        htmlToMarkdown(
          '<table><thead><tr><th>代號</th></tr></thead><tbody><tr><td>2330</td></tr></tbody></table>',
        ),
      ),
    ).toBe('| 代號 |\n| --- |\n| 2330 |');
  });

  it('keeps lists, emphasis and links, and drops the style blocks Office pastes', () => {
    const md = htmlToMarkdown(
      '<style>p{color:red}</style><p><b>重點</b>：<a href="https://twse.com.tw">證交所</a></p><ul><li>營收</li><li>毛利</li></ul>',
    );
    expect(squeeze(md)).toBe('**重點**：[證交所](https://twse.com.tw)\n\n- 營收\n- 毛利');
  });
});

describe('readPaste', () => {
  it('leaves plain text to the browser', () => {
    expect(readPaste(clipboard({ text: 'hi', html: '<b>hi</b>' }), false)).toEqual({
      kind: 'default',
    });
  });

  it('turns formatted text into Markdown when formatting is kept', () => {
    expect(readPaste(clipboard({ text: 'hi', html: '<b>hi</b>' }), true, at)).toEqual({
      kind: 'text',
      text: '**hi**',
    });
  });

  it('prefers the text over the picture Excel and Word also put on the clipboard', () => {
    const picture = new File(['png'], 'image.png', { type: 'image/png' });
    expect(readPaste(clipboard({ text: '2330\t1050', files: [picture] }), false).kind).toBe(
      'default',
    );
  });

  it('attaches a pasted picture under a name saying when it was pasted', () => {
    const result = readPaste(
      clipboard({ files: [new File(['a'], 'image.png'), new File(['b'], 'image.png')] }),
      false,
      at,
    );
    expect(result.kind === 'files' && result.files.map((f) => f.name)).toEqual([
      '貼上的圖片-090507.png',
      '貼上的圖片-090507-2.png',
    ]);
  });

  it('keeps the name of a pasted file that has one', () => {
    const result = readPaste(clipboard({ files: [new File(['a'], '年報.pdf')] }), false, at);
    expect(result.kind === 'files' && result.files[0].name).toBe('年報.pdf');
  });

  it('attaches text past the limit as a file, Markdown when it was formatted', async () => {
    const long = 'x'.repeat(LONG_PASTE_CHARS + 1);

    const plain = readPaste(clipboard({ text: long }), false, at);
    expect(plain.kind === 'files' && plain.files[0].name).toBe('貼上的內容-090507.txt');
    expect(plain.kind === 'files' && (await plain.files[0].text())).toBe(long);

    const formatted = readPaste(clipboard({ text: long, html: `<p>${long}</p>` }), true, at);
    expect(formatted.kind === 'files' && formatted.files[0].name).toBe('貼上的內容-090507.md');
  });

  it('text right at the limit still goes in the box', () => {
    expect(readPaste(clipboard({ text: 'x'.repeat(LONG_PASTE_CHARS) }), false).kind).toBe(
      'default',
    );
  });
});

describe('insertPastedText', () => {
  it('puts a line of text in place of the selection, the caret after it', () => {
    expect(insertPastedText('分析 XXXX 的走勢', 3, 7, '2330')).toEqual({
      value: '分析 2330 的走勢',
      caret: 7,
    });
  });

  it('puts a block on lines of its own, after what was typed before it', () => {
    expect(insertPastedText('看這張表', 4, 4, '| a |\n| b |')).toEqual({
      value: '看這張表\n| a |\n| b |\n',
      caret: 17,
    });
  });

  it('a block at the start of the box, or after a line break, needs no line break before it', () => {
    expect(insertPastedText('', 0, 0, '| a |\n| b |').value).toBe('| a |\n| b |\n');
    expect(insertPastedText('看這張表\n', 5, 5, '| a |\n| b |').value).toBe(
      '看這張表\n| a |\n| b |\n',
    );
  });
});
