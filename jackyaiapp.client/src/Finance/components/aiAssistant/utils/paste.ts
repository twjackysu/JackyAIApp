import { gfm } from '@joplin/turndown-plugin-gfm';
import TurndownService from 'turndown';

/** Pasted text longer than this becomes an attachment instead of filling the message box. */
export const LONG_PASTE_CHARS = 4000;

/** What a paste into the message box turns into. */
export type PasteResult =
  /** Files to attach: a pasted picture, or pasted text too long for the box. */
  | { kind: 'files'; files: File[] }
  /** Text to put in the box in place of what the browser would paste. */
  | { kind: 'text'; text: string }
  /** Leave it to the browser: plain text it pastes as it is. */
  | { kind: 'default' };

let turndown: TurndownService | undefined;

/**
 * Formatted text (a web page, Word, an Excel range) as Markdown, which the model reads as well as
 * any reader does: tables stay tables, lists stay lists, links keep their address.
 */
export const htmlToMarkdown = (html: string): string => {
  if (!turndown) {
    turndown = new TurndownService({
      headingStyle: 'atx',
      codeBlockStyle: 'fenced',
      bulletListMarker: '-',
    });
    turndown.use(gfm);
    // Office pastes carry <style> and <meta> blocks; their text is not content.
    turndown.remove(['style', 'meta', 'script', 'title']);
  }
  const doc = new DOMParser().parseFromString(html, 'text/html');
  promoteHeaderRows(doc);
  return turndown.turndown(doc.body).trim();
};

/**
 * A table copied from Excel (or most web tables) has no header cells, and Markdown has no table
 * without a header row: left alone it gets an empty one above it. Its first row is the header in
 * all but name, so it becomes one.
 */
const promoteHeaderRows = (doc: Document) => {
  doc.querySelectorAll('table').forEach((table) => {
    if (table.querySelector('th')) return;
    const firstRow = table.querySelector('tr');
    firstRow?.querySelectorAll('td').forEach((td) => {
      const th = doc.createElement('th');
      th.innerHTML = td.innerHTML;
      td.replaceWith(th);
    });
  });
};

const stamp = (now: Date) =>
  [now.getHours(), now.getMinutes(), now.getSeconds()]
    .map((n) => String(n).padStart(2, '0'))
    .join('');

/** A pasted picture is named "image.png" by every browser; give it a name worth showing. */
const renamePastedFile = (file: File, now: Date, index: number): File => {
  if (!/^image\.\w+$/i.test(file.name)) return file;
  const extension = file.name.slice(file.name.lastIndexOf('.'));
  const suffix = index > 0 ? `-${index + 1}` : '';
  return new File([file], `貼上的圖片-${stamp(now)}${suffix}${extension}`, { type: file.type });
};

/**
 * Decides what a paste becomes.
 * - Text wins over files: Excel and Word put a picture of the selection on the clipboard as well
 *   as its text, and the text is what was meant. A screenshot has no text, so it is attached.
 * - `keepFormatting` turns formatted text into Markdown; off, the plain text is used.
 * - Text longer than {@link LONG_PASTE_CHARS} is attached as a file, as Claude and ChatGPT do,
 *   rather than burying the question under it.
 */
export const readPaste = (
  data: Pick<DataTransfer, 'getData' | 'files'>,
  keepFormatting: boolean,
  now: Date = new Date(),
): PasteResult => {
  const plain = data.getData('text/plain');
  const html = keepFormatting ? data.getData('text/html') : '';
  const files = Array.from(data.files ?? []);

  if (!plain.trim() && !html.trim()) {
    return files.length > 0
      ? { kind: 'files', files: files.map((f, i) => renamePastedFile(f, now, i)) }
      : { kind: 'default' };
  }

  const markdown = html ? htmlToMarkdown(html) : '';
  const text = markdown || plain;
  if (text.length > LONG_PASTE_CHARS) {
    const name = `貼上的內容-${stamp(now)}.${markdown ? 'md' : 'txt'}`;
    return { kind: 'files', files: [new File([text], name, { type: 'text/plain' })] };
  }
  return markdown ? { kind: 'text', text: markdown } : { kind: 'default' };
};
