import { describe, expect, it } from 'vitest';

import { CARD_LIMITS, parseToolCard, safeHref } from './toolCard';

describe('parseToolCard: a table', () => {
  const raw = {
    kind: 'table',
    title: '各地區營收',
    columns: [
      { key: 'region', label: '地區' },
      { key: 'revenue', label: '營收', numeric: true },
    ],
    rows: [
      { region: '北區', revenue: 1200, extra: 'dropped' },
      { region: '南區', revenue: '900' },
    ],
    totalRows: 40,
  };

  it('keeps the columns it names, and only their cells', () => {
    expect(parseToolCard(raw)).toEqual({
      kind: 'table',
      title: '各地區營收',
      columns: [
        { key: 'region', label: '地區', numeric: false },
        { key: 'revenue', label: '營收', numeric: true },
      ],
      rows: [
        { region: '北區', revenue: 1200 },
        { region: '南區', revenue: '900' },
      ],
      totalRows: 40,
    });
  });

  it('turns anything but text and numbers into an empty cell', () => {
    const card = parseToolCard({
      ...raw,
      rows: [{ region: { html: '<b>x</b>' }, revenue: Number.NaN }],
    });
    expect(card && 'rows' in card && card.rows[0]).toEqual({ region: null, revenue: null });
  });

  it('caps the rows and columns a tool can push into the answer', () => {
    const many = parseToolCard({
      kind: 'table',
      columns: Array.from({ length: 30 }, (_, i) => ({ key: `c${i}` })),
      rows: Array.from({ length: 500 }, () => ({ c0: 1 })),
    });
    expect(many && 'columns' in many && many.columns).toHaveLength(CARD_LIMITS.columns);
    expect(many && 'rows' in many && many.rows).toHaveLength(CARD_LIMITS.rows);
  });

  it('is no card without columns', () => {
    expect(parseToolCard({ kind: 'table', columns: [], rows: [] })).toBeNull();
  });
});

describe('parseToolCard: a link', () => {
  it('keeps a link to this site', () => {
    expect(
      parseToolCard({
        kind: 'link',
        title: 'serendipity',
        href: '/english/dictionary/serendipity',
      }),
    ).toEqual({
      kind: 'link',
      title: 'serendipity',
      description: null,
      href: '/english/dictionary/serendipity',
      actionLabel: null,
    });
  });

  it('drops a link that could run script or leave https', () => {
    expect(parseToolCard({ kind: 'link', title: 'x', href: 'javascript:alert(1)' })).toBeNull();
    expect(parseToolCard({ kind: 'link', title: 'x', href: 'http://example.com' })).toBeNull();
  });
});

describe('parseToolCard: a kind an app draws', () => {
  it('passes its data on', () => {
    expect(parseToolCard({ kind: 'chart', title: '趨勢', data: { type: 'line' } })).toEqual({
      kind: 'chart',
      title: '趨勢',
      data: { type: 'line' },
    });
  });

  it('is no card without a kind or data', () => {
    expect(parseToolCard(null)).toBeNull();
    expect(parseToolCard({ title: 'x' })).toBeNull();
    expect(parseToolCard({ kind: 'chart' })).toBeNull();
    expect(parseToolCard({ kind: 'Not A Kind', data: {} })).toBeNull();
  });
});

describe('safeHref', () => {
  it('takes a path on this site and https', () => {
    expect(safeHref('/portfolio')).toBe('/portfolio');
    expect(safeHref('https://example.com/a')).toBe('https://example.com/a');
  });
  it('refuses a protocol-relative URL, which leaves the site', () => {
    expect(safeHref('//evil.example')).toBeNull();
  });
  it('refuses data: and other schemes', () => {
    expect(safeHref('data:text/html,hi')).toBeNull();
    expect(safeHref('mailto:a@b.c')).toBeNull();
  });
});
