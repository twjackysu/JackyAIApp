/**
 * What a tool's result can show besides its text (docs/ai-native/design/04 §2.1): a card the chat
 * draws in the answer, generative UI. The server sends it on `tool_result.card`; it comes from a
 * tool, so from anywhere, and `parseToolCard` keeps only what the renderers draw safely: plain text
 * and numbers (React escapes them), a bounded size, and links only to this site or to https.
 *
 * The chat draws `table` and `link` itself; an app adds kinds of its own (a chart drawn with its own
 * renderer) through `AiChatConfig.cardRenderers`. A kind nobody draws is left out of the answer.
 */
import type { ComponentType } from 'react';

export type CardCell = string | number | null;

export interface TableCard {
  kind: 'table';
  title: string | null;
  columns: { key: string; label: string; numeric: boolean }[];
  rows: Record<string, CardCell>[];
  /** The rows the tool found, when more than it sent. */
  totalRows: number | null;
}

export interface LinkCard {
  kind: 'link';
  title: string;
  description: string | null;
  /** A path on this site (`/english/dictionary/serendipity`) or an https URL. */
  href: string;
  actionLabel: string | null;
}

/** A card of a kind an app draws itself; its fields are the app's to check. */
export interface CustomCard {
  kind: string;
  title: string | null;
  data: Record<string, unknown>;
}

export type ToolCard = TableCard | LinkCard | CustomCard;

export type ToolCardRenderer = ComponentType<{ card: CustomCard }>;

export const CARD_LIMITS = { columns: 12, rows: 200, text: 500 } as const;

const isRecord = (v: unknown): v is Record<string, unknown> =>
  typeof v === 'object' && v !== null && !Array.isArray(v);

const text = (v: unknown, max: number = CARD_LIMITS.text): string | null =>
  typeof v === 'string' && v.trim() ? v.trim().slice(0, max) : null;

const cell = (v: unknown): CardCell =>
  typeof v === 'number' && Number.isFinite(v)
    ? v
    : typeof v === 'string'
      ? v.slice(0, CARD_LIMITS.text)
      : typeof v === 'boolean'
        ? String(v)
        : null;

/** A link the card may follow: a path on this site, or https; never javascript:, data: or http. */
export function safeHref(raw: unknown): string | null {
  const href = text(raw, 2000);
  if (!href) return null;
  if (href.startsWith('/') && !href.startsWith('//')) return href;
  try {
    return new URL(href).protocol === 'https:' ? href : null;
  } catch {
    return null;
  }
}

function parseTable(raw: Record<string, unknown>): TableCard | null {
  if (!Array.isArray(raw.columns) || !Array.isArray(raw.rows)) return null;
  const columns = raw.columns
    .filter(isRecord)
    .map((c) => ({
      key: text(c.key, 100),
      label: text(c.label, 100) ?? text(c.key, 100),
      numeric: c.numeric === true,
    }))
    .filter((c): c is { key: string; label: string; numeric: boolean } => c.key !== null)
    .slice(0, CARD_LIMITS.columns);
  if (columns.length === 0) return null;
  const rows = raw.rows
    .filter(isRecord)
    .slice(0, CARD_LIMITS.rows)
    .map((r) => Object.fromEntries(columns.map((c) => [c.key, cell(r[c.key])])));
  const total =
    typeof raw.totalRows === 'number' && raw.totalRows > rows.length ? raw.totalRows : null;
  return { kind: 'table', title: text(raw.title, 200), columns, rows, totalRows: total };
}

function parseLink(raw: Record<string, unknown>): LinkCard | null {
  const title = text(raw.title, 200);
  const href = safeHref(raw.href);
  if (!title || !href) return null;
  return {
    kind: 'link',
    title,
    description: text(raw.description),
    href,
    actionLabel: text(raw.actionLabel, 40),
  };
}

/** The card a tool result carries, kept to what can be drawn safely; null when there is none. */
export function parseToolCard(raw: unknown): ToolCard | null {
  if (!isRecord(raw)) return null;
  const kind = text(raw.kind, 40);
  if (!kind) return null;
  if (kind === 'table') return parseTable(raw);
  if (kind === 'link') return parseLink(raw);
  if (!/^[a-z][a-z0-9-]*$/.test(kind) || !isRecord(raw.data)) return null;
  return { kind, title: text(raw.title, 200), data: raw.data };
}
