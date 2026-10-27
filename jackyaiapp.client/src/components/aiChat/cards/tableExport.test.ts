import { describe, expect, it } from 'vitest';

import { exportedRowsNotice, guardFormula, tableCsv, tableFileName, tableTsv } from './tableExport';

import type { TableCard } from './toolCard';

const card = (patch: Partial<TableCard> = {}): TableCard => ({
  kind: 'table',
  title: null,
  columns: [
    { key: 'a', label: '名稱', numeric: false },
    { key: 'b', label: '數量', numeric: true },
  ],
  rows: [],
  totalRows: null,
  ...patch,
});

describe('guardFormula', () => {
  it('defuses text a spreadsheet would evaluate', () => {
    for (const s of [
      '=1+1',
      '+SUM(A1)',
      '-2+3',
      '@cmd',
      '\tx',
      '\rx',
      '-',
      '-%',
      '-1,2',
      '-5%%',
      '+1e3+cmd',
    ])
      expect(guardFormula(s)).toBe(`'${s}`);
  });

  it('leaves other text, and text that is a number, alone', () => {
    for (const s of [
      '台積電',
      '-5',
      '+3.2',
      '-1e3',
      '.5',
      '-18.0%',
      '-1,234,567.5',
      '+0.5%',
      'a=b',
      '',
    ])
      expect(guardFormula(s)).toBe(s);
  });
});

describe('tableCsv', () => {
  it('writes RFC 4180 with CRLF: quotes, commas and line breaks quoted, numbers plain, empty cells empty', () => {
    const csv = tableCsv(
      card({
        rows: [
          { a: 'say "hi", ok', b: 1234567.5 },
          { a: 'two\nlines', b: null },
          { a: null, b: -3 },
        ],
      }),
    );
    expect(csv).toBe('名稱,數量\r\n"say ""hi"", ok",1234567.5\r\n"two\nlines",\r\n,-3\r\n');
  });

  it('defuses a header too: a tool names its columns', () => {
    expect(tableCsv(card({ columns: [{ key: 'a', label: '=cmd', numeric: false }] }))).toBe(
      "'=cmd\r\n",
    );
  });
});

describe('tableTsv', () => {
  it('keeps every cell in its cell: tabs and line breaks inside one become spaces', () => {
    expect(tableTsv(card({ rows: [{ a: 'x\ty\r\nz', b: 2 }] }))).toBe('名稱\t數量\nx y z\t2');
  });

  it('defuses a formula left after the tabs are taken out', () => {
    expect(tableTsv(card({ rows: [{ a: '=1', b: null }] }))).toBe("名稱\t數量\n'=1\t");
  });
});

describe('a code with leading zeros (a TW ETF)', () => {
  it('keeps its zeros in both: a spreadsheet would read 0050 as 50', () => {
    const c = card({
      rows: [
        { a: '0050', b: 50 },
        { a: '0.5', b: null },
      ],
    });
    expect(tableCsv(c)).toBe("名稱,數量\r\n'0050,50\r\n0.5,\r\n");
    expect(tableTsv(c)).toBe("名稱\t數量\n'0050\t50\n0.5\t");
  });
});

describe('tableTsv and quotes', () => {
  it('quotes a cell with a quote in it, so a quoted formula lands as text and a lone quote takes no tabs', () => {
    const c = card({
      rows: [
        { a: '"=HYPERLINK(""x"")"', b: 1 },
        { a: '"abc', b: 2 },
      ],
    });
    expect(tableTsv(c)).toBe('名稱\t數量\n"""=HYPERLINK(""""x"""")"""\t1\n"""abc"\t2');
  });

  it('quotes a header with a quote in it too', () => {
    expect(tableTsv(card({ columns: [{ key: 'a', label: '"x', numeric: false }] }))).toBe('"""x"');
  });
});

describe('tableFileName', () => {
  it('is the title and the day in Taipei, without characters a file system refuses', () => {
    // 16:30 UTC is already the next day in Taipei.
    expect(tableFileName('A/B: "外資"', new Date('2026-10-26T16:30:00Z'))).toBe(
      'A B 外資 2026-10-27.csv',
    );
  });

  it('drops bidi controls, which could make the name read otherwise than it is', () => {
    expect(tableFileName('a‮fdp.exe', new Date('2026-10-27T01:00:00Z'))).toBe(
      'a fdp.exe 2026-10-27.csv',
    );
  });

  it('is 表格 when the card has no title', () => {
    expect(tableFileName(null, new Date('2026-10-27T01:00:00Z'))).toBe('表格 2026-10-27.csv');
  });

  it('cuts a long title by characters, never half an emoji', () => {
    const name = tableFileName('😀'.repeat(70), new Date('2026-10-27T01:00:00Z'));
    expect([...name.replace(' 2026-10-27.csv', '')]).toHaveLength(60);
  });
});

describe('exportedRowsNotice', () => {
  it('counts the rows, and says the tool found more when it did', () => {
    const rows = [{ a: 'x', b: 1 }];
    expect(exportedRowsNotice(card({ rows }), '已複製')).toBe('已複製 1 筆');
    expect(exportedRowsNotice(card({ rows, totalRows: 4000 }), '已下載')).toBe(
      '已下載畫面上的 1 筆（共找到 4,000 筆）',
    );
  });
});
