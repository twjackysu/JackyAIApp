/**
 * A conversation on screen as one Markdown document (D-147), to copy into a note or a mail or to keep as a file:
 * the questions and the answers as the reader saw them, with the tools each answer used and the tables and links
 * its cards showed. What only this session holds (the thinking, a waiting confirmation card) is left out, and so
 * are cards the chat draws as an app's own (a chart, a quiz), which have no Markdown form: a line names them.
 *
 * Everything is the reader's own screen, so nothing is checked again here; the cards were checked by
 * `parseToolCard` when they arrived. What could break the document is kept from doing so: table cells are on one line
 * with `|` escaped, a card's words cannot open a link or emphasis, a message's own headings go two levels under the
 * turns' (so an answer's 「## 你的持股」 is not taken for a turn), and a code block a message leaves open (an answer cut
 * at its length limit or stopped) is closed at its end, so the turns after it are not code (D-148).
 */
import { LinkCard, TableCard, ToolCard } from '../cards/toolCard';
import { Message } from '../types';

export interface ConversationMarkdownOptions {
  /** The conversation's title (the history column's), or none for one not yet stored. */
  title?: string;
  /** The assistant's name, for the answers' headings. */
  assistant: string;
  /** When it was exported, written in Taipei time. */
  exportedAt: Date;
  /** The site's origin, to make a card's link to a page of this site whole. */
  origin: string;
  /** The kinds of card this chat draws besides `table` and `link` (`AiChatConfig.cardRenderers`); others are not on screen. */
  drawnKinds?: readonly string[];
}

const taipei = new Intl.DateTimeFormat('sv-SE', {
  timeZone: 'Asia/Taipei',
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
});

/** `2026-10-27 05:55`, in Taipei. */
export const taipeiMinute = (at: Date) => taipei.format(at);

/** A table cell as the card shows it (`TableCardView`), on one line, its `|` escaped. */
const cellText = (value: string | number | null) =>
  value === null
    ? '—'
    : typeof value === 'number'
      ? value.toLocaleString('zh-TW')
      : value.replace(/\s+/g, ' ').trim().replace(/\\/g, '\\\\').replace(/\|/g, '\\|');

/** A card's own words, on one line, with nothing that would open a link, emphasis, code or markup, or close the
 *  brackets around them. */
const inline = (text: string) =>
  text
    .replace(/\s+/g, ' ')
    .trim()
    .replace(/[\\[\]*_`<>#]/g, (c) => `\\${c}`);

const FENCE = /^ {0,3}(`{3,}|~{3,})/;

/**
 * A message's own Markdown inside the document: its headings two levels down (at most six), outside code blocks, and a
 * code block it leaves open closed at its end.
 */
export const messageBody = (content: string) => {
  let open: string | null = null;
  const lines = content.split('\n').map((line) => {
    const fence = FENCE.exec(line)?.[1];
    if (open) {
      // Closed by a fence of the same character at least as long.
      if (
        fence &&
        fence[0] === open[0] &&
        fence.length >= open.length &&
        !line.trim().slice(fence.length).trim()
      )
        open = null;
      return line;
    }
    if (fence) {
      open = fence;
      return line;
    }
    return line.replace(
      /^( {0,3})(#{1,6})(?=\s|$)/,
      (_, indent: string, hashes: string) =>
        `${indent}${'#'.repeat(Math.min(hashes.length + 2, 6))}`,
    );
  });
  if (open) lines.push(open);
  return lines.join('\n');
};

const tableMarkdown = (card: TableCard) => {
  const lines: string[] = [];
  if (card.title) lines.push(`**${inline(card.title)}**`, '');
  lines.push(`| ${card.columns.map((c) => cellText(c.label)).join(' | ')} |`);
  lines.push(`| ${card.columns.map((c) => (c.numeric ? '---:' : '---')).join(' | ')} |`);
  for (const row of card.rows)
    lines.push(`| ${card.columns.map((c) => cellText(row[c.key] ?? null)).join(' | ')} |`);
  if (card.totalRows !== null && card.totalRows > card.rows.length)
    lines.push(
      '',
      `（共 ${card.totalRows.toLocaleString('zh-TW')} 筆，這裡列出 ${card.rows.length} 筆）`,
    );
  return lines.join('\n');
};

const linkMarkdown = (card: LinkCard, origin: string) => {
  const href = card.href.startsWith('/') ? `${origin}${card.href}` : card.href;
  // `<…>` keeps an address with spaces or brackets in one piece; a `\\` in it would escape what follows.
  const link = `[${inline(card.title)}](<${href.replace(/[<>\\]/g, encodeURIComponent)}>)`;
  return card.description ? `${link}：${inline(card.description)}` : link;
};

const cardMarkdown = (card: ToolCard, origin: string, drawnKinds: readonly string[]) =>
  card.kind === 'table'
    ? tableMarkdown(card as TableCard)
    : card.kind === 'link'
      ? linkMarkdown(card as LinkCard, origin)
      : // A kind this chat does not draw is not on screen either (`ToolCards` leaves it out).
        drawnKinds.includes(card.kind)
        ? `（${card.title ? `「${inline(card.title)}」` : '一張卡片'}在對話裡顯示，這裡沒有匯出）`
        : null;

/** The cards an answer showed: its tools' in this session, else the ones stored with it. */
const cardsOf = (message: Message): ToolCard[] =>
  message.toolSteps
    ? message.toolSteps.flatMap((step) => (step.card ? [step.card] : []))
    : (message.cards ?? []);

/** Whether there is anything to export: a question asked. */
export const canExportConversation = (messages: readonly Message[]) =>
  messages.some((m) => m.isUser);

export function conversationMarkdown(
  messages: readonly Message[],
  { title, assistant, exportedAt, origin, drawnKinds = [] }: ConversationMarkdownOptions,
): string {
  const parts: string[] = [
    `# ${inline(title?.trim() || '對話')}`,
    `> 匯出自 ${inline(assistant)}，${taipeiMinute(exportedAt)}（台北時間）`,
  ];
  for (const message of messages) {
    if (message.isUser) {
      const body = [`## 你`, messageBody(message.content.trim())];
      if (message.attachments?.length)
        body.push(`附檔：${message.attachments.map((a) => inline(a.fileName)).join('、')}`);
      parts.push(body.join('\n\n'));
      continue;
    }
    const body = [`## ${inline(assistant)}`];
    // Only the tools that did their work: a failed one is not what the answer rests on.
    const used = [
      ...new Set((message.toolSteps ?? []).filter((s) => s.status === 'done').map((s) => s.label)),
    ];
    if (used.length) body.push(`*用了：${used.map(inline).join('、')}*`);
    for (const card of cardsOf(message)) {
      const markdown = cardMarkdown(card, origin, drawnKinds);
      if (markdown) body.push(markdown);
    }
    const text = message.content.trim();
    if (text) body.push(messageBody(text));
    if (message.truncated) body.push('*（回答太長，在這裡被截斷）*');
    // An answer that has nothing yet (the greeting's placeholder, one still starting) is left out.
    if (body.length > 1) parts.push(body.join('\n\n'));
  }
  return `${parts.join('\n\n')}\n`;
}

/** A file name for the download: the title without what a file system refuses, at most 60 characters. */
export const conversationFileName = (title: string | undefined, exportedAt: Date) => {
  const name = (title ?? '')
    // eslint-disable-next-line no-control-regex
    .replace(/[\\/:*?"<>|\u0000-\u001f]/g, ' ')
    .replace(/\s+/g, ' ')
    .trim();
  // Cut by characters, not UTF-16 units: half an emoji is no character at all.
  const cut = [...name].slice(0, 60).join('').trim();
  return `${cut || `對話 ${taipeiMinute(exportedAt).slice(0, 10)}`}.md`;
};
