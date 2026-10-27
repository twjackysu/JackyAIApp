/**
 * Sorting a table card by one of its columns (D-152): a press on a header sorts by it, the next press the other way,
 * a third puts the tool's order back. Only on screen: the model, the stored answer and 匯出對話 keep the tool's order;
 * 複製表格 and 下載 CSV take the order on screen.
 */
import type { CardCell, TableColumn } from './toolCard';

export type SortDirection = 'asc' | 'desc';
export interface TableSort {
  key: string;
  direction: SortDirection;
}

/**
 * A number column starts from the largest (the biggest position, the most bought), a text column from A or 一. Then the
 * other way, then none.
 */
export function nextSort(current: TableSort | null, column: TableColumn): TableSort | null {
  const first: SortDirection = column.numeric ? 'desc' : 'asc';
  if (current?.key !== column.key) return { key: column.key, direction: first };
  return current.direction === first
    ? { key: column.key, direction: first === 'asc' ? 'desc' : 'asc' }
    : null;
}

// What a tool writes as text for a number: "49.5%", "-1,234", "+3.2" — sorted as the number, not as text, or -18.0%
// would come before 8.9% and 10% before 9%.
const NUMBER_TEXT = /^[+-]?(\d{1,3}(,\d{3})+|\d+)?(\.\d+)?%?$/;

/** A cell as the number it shows, when it is one (also what 畫成圖 draws, `tableChart`). */
export const cellNumber = (value: CardCell): number | null => {
  if (typeof value === 'number') return value;
  if (value === null) return null;
  const text = value.trim();
  if (!/\d/.test(text) || !NUMBER_TEXT.test(text)) return null;
  return Number(text.replace(/[,%]/g, ''));
};

const collator = new Intl.Collator('zh-TW', { numeric: true, sensitivity: 'base' });

const isEmpty = (value: CardCell) =>
  value === null || (typeof value === 'string' && value.trim() === '');

/**
 * The rows in the order asked, a new array (the card's own stays as the tool sent it). Empty cells always last; a column
 * whose cells all read as numbers is compared as numbers, any other as text (`zh-TW`, digits by their value: 2330 after
 * 911). Rows that compare equal keep the tool's order.
 */
export function sortTableRows<T extends Record<string, CardCell>>(
  rows: readonly T[],
  sort: TableSort | null,
): readonly T[] {
  if (!sort) return rows;
  const { key, direction } = sort;
  const filled = rows.filter((r) => !isEmpty(r[key] ?? null));
  const empty = rows.filter((r) => isEmpty(r[key] ?? null));
  const asNumbers = filled.every((r) => cellNumber(r[key] ?? null) !== null);
  const sign = direction === 'asc' ? 1 : -1;
  const sorted = [...filled].sort((a, b) => {
    const av = a[key] ?? null;
    const bv = b[key] ?? null;
    const cmp = asNumbers
      ? (cellNumber(av) as number) - (cellNumber(bv) as number)
      : collator.compare(String(av), String(bv));
    return sign * cmp;
  });
  return [...sorted, ...empty];
}
