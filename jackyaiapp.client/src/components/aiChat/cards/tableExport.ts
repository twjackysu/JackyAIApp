/**
 * A table card taken out of the chat (D-150): copied for a spreadsheet (tab-separated, which Excel, Numbers and Google
 * Sheets paste as cells) or downloaded as CSV. The cells came from a tool, so from anywhere: a text cell that a
 * spreadsheet would read as a formula (=, +, -, @, a tab or a CR first) gets a leading apostrophe, as the server's
 * Details export does (`CsvExportWriter.GuardFormula`), unless the text is itself a number ("-5", "-18.0%", "-1,234"). A code
 * with leading zeros (0050, 00878) gets one too, or the spreadsheet would read it as the number 50. Numbers go as numbers
 * (no thousands separators), an empty cell as nothing.
 */
import type { CardCell, TableCard } from './toolCard';

// A signed number as a tool writes one ("-5", "+3.2", "-1,234", "-18.0%", "-1e3"): digits, one sign first, nothing a
// spreadsheet could run. Kept as typed, so a loss reads -18.0%, not '-18.0%.
const NUMBER = /^[+-]?(\d{1,3}(,\d{3})+|\d+)?(\.\d+)?(e[+-]?\d+)?%?$/i;
const isNumber = (text: string) => /\d/.test(text) && NUMBER.test(text);

/** A text cell a spreadsheet would never evaluate. */
export const guardFormula = (text: string) =>
  /^[=+\-@\t\r]/.test(text) && !isNumber(text) ? `'${text}` : text;

/** A text cell as the spreadsheet should keep it: no formula, and a code's leading zeros kept (`'0050`). */
const guardText = (text: string) => (/^0\d/.test(text) ? `'${text}` : guardFormula(text));

const raw = (value: CardCell) =>
  value === null ? '' : typeof value === 'number' ? String(value) : guardText(value);

const quote = (text: string) => (/[",\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text);

/** RFC 4180 with CRLF line ends; the caller writes it with a BOM (`csvBlob`) so Excel reads Chinese as UTF-8. */
export const tableCsv = (card: TableCard) =>
  [
    card.columns.map((c) => quote(guardFormula(c.label))).join(','),
    ...card.rows.map((row) => card.columns.map((c) => quote(raw(row[c.key]))).join(',')),
  ].join('\r\n') + '\r\n';

// A tab or a line break inside a cell would start another cell or row when pasted.
const flat = (text: string) => text.replace(/[\t\r\n]+/g, ' ');

// A pasted cell starting with `"` is read as quoted: the quotes come off and `""` becomes `"`, so `"=HYPERLINK(""…"")"`
// would land as a live formula, and a lone `"` would swallow the tabs after it. A cell with a quote in it is quoted
// here, so it lands as the text it is.
const pasteCell = (text: string) => (text.includes('"') ? `"${text.replace(/"/g, '""')}"` : text);

/** Tab-separated, one line a row: what a spreadsheet pastes as cells. */
export const tableTsv = (card: TableCard) =>
  [
    card.columns.map((c) => pasteCell(guardFormula(flat(c.label)))).join('\t'),
    ...card.rows.map((row) =>
      card.columns
        .map((c) => {
          const v = row[c.key];
          return typeof v === 'string' ? pasteCell(guardText(flat(v))) : raw(v);
        })
        .join('\t'),
    ),
  ].join('\n');

/** The download: UTF-8 with a BOM, which Excel needs to show Chinese rather than mojibake. */
export const csvBlob = (card: TableCard) =>
  new Blob(['\uFEFF', tableCsv(card)], { type: 'text/csv;charset=utf-8' });

const taipeiDay = new Intl.DateTimeFormat('en-CA', {
  timeZone: 'Asia/Taipei',
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
});

/** The card's title as a file name (`外資買賣超 2026-10-27.csv`), or 表格 when it has none. */
export const tableFileName = (title: string | null, at: Date) => {
  const name = (title ?? '')
    // Bidi controls could make the name read otherwise than it is ("…fdp.exe" shown as "…exe.pdf").
    // eslint-disable-next-line no-control-regex
    .replace(/[\\/:*?"<>|\u0000-\u001f\u007f\u200e\u200f\u202a-\u202e\u2066-\u2069]/g, ' ')
    .replace(/\s+/g, ' ')
    .trim();
  // Cut by characters, not UTF-16 units: half an emoji is no character at all.
  const cut = [...name].slice(0, 60).join('').trim();
  return `${cut || '表格'} ${taipeiDay.format(at)}.csv`;
};

/** What the reader is told once copied or downloaded: how many rows, and that the tool found more when it did. */
export const exportedRowsNotice = (card: TableCard, done: string) =>
  card.totalRows !== null
    ? `${done}畫面上的 ${card.rows.length} 筆（共找到 ${card.totalRows.toLocaleString('zh-TW')} 筆）`
    : `${done} ${card.rows.length} 筆`;
