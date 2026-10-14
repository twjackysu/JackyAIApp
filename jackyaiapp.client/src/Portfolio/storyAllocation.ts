import { PositionDto } from '@/apis/portfolioApis/types';

export interface StoryItem {
  symbol: string;
  name: string;
  /** % of this section's value (each section sums to 100 on its own). */
  pct: number;
  /** True for the folded "其他" tail row. */
  isOther: boolean;
}

export type StoryMarket = 'TW' | 'US' | 'FUND';

export interface StorySection {
  market: StoryMarket;
  /** This section's % of everything on the image: TW stocks + US stocks + funds + cash. */
  share: number;
  /** Number of distinct holdings, including those folded into "其他". */
  count: number;
  items: StoryItem[];
}

export interface StoryAllocation {
  tw: StorySection | null;
  us: StorySection | null;
  fund: StorySection | null;
  /** Cash's % of TW stocks + US stocks + funds + cash; null without cash. One figure, no breakdown. */
  cashPct: number | null;
}

/** Rows a section can list before the tail is folded into one "其他" row. */
export const STORY_MAX_ROWS = 8;
/** The same when three sections share the image and each gets a third of the height. */
export const STORY_MAX_ROWS_COMPACT = 5;

/**
 * Allocation for the IG story image: TW stocks, US stocks and funds as sections, plus cash as a
 * single figure. Each section's holdings are normalised to 100% on their own, so the image still
 * reads correctly after the user crops the lower sections off; the sections' and cash's shares are
 * of all four combined. Only positively-valued STOCK, FUND and CASH positions count; duplicate
 * symbols (one per account) are merged. Without an explicit `maxRows`, three sections list fewer
 * rows each, since they share the height.
 */
export function buildStoryAllocation(positions: PositionDto[], maxRows?: number): StoryAllocation {
  type Holding = StoryItem & { value: number };
  const bySection: Record<StoryMarket, Map<string, Holding>> = {
    TW: new Map(),
    US: new Map(),
    FUND: new Map(),
  };
  let cashTotal = 0;
  for (const p of positions) {
    if (!(p.marketValueTWD > 0)) continue;
    if (p.assetType === 'CASH') {
      cashTotal += p.marketValueTWD;
      continue;
    }
    if (p.assetType !== 'STOCK' && p.assetType !== 'FUND') continue;
    const market: StoryMarket = p.assetType === 'FUND' ? 'FUND' : p.market === 'US' ? 'US' : 'TW';
    const existing = bySection[market].get(p.symbol);
    if (existing) existing.value += p.marketValueTWD;
    else
      bySection[market].set(p.symbol, {
        // A fund's code means nothing to a reader; its name is what identifies it.
        symbol: market === 'FUND' ? p.name || p.symbol : p.symbol,
        name: market === 'FUND' ? '' : p.name,
        pct: 0,
        isOther: false,
        value: p.marketValueTWD,
      });
  }

  const sum = (m: Map<string, { value: number }>) =>
    [...m.values()].reduce((s, x) => s + x.value, 0);
  const totals: Record<StoryMarket, number> = {
    TW: sum(bySection.TW),
    US: sum(bySection.US),
    FUND: sum(bySection.FUND),
  };
  const grand = totals.TW + totals.US + totals.FUND + cashTotal;
  const sectionCount = Object.values(totals).filter((t) => t > 0).length;
  const rows = maxRows ?? (sectionCount >= 3 ? STORY_MAX_ROWS_COMPACT : STORY_MAX_ROWS);

  const section = (market: StoryMarket): StorySection | null => {
    const total = totals[market];
    if (total <= 0) return null;
    const sorted = [...bySection[market].values()].sort((a, b) => b.value - a.value);
    const toItem = ({ symbol, name, value }: { symbol: string; name: string; value: number }) => ({
      symbol,
      name,
      pct: (value / total) * 100,
      isOther: false,
    });
    let items: StoryItem[];
    if (sorted.length <= rows) {
      items = sorted.map(toItem);
    } else {
      const head = sorted.slice(0, rows - 1);
      const tailValue = sorted.slice(rows - 1).reduce((s, x) => s + x.value, 0);
      items = [
        ...head.map(toItem),
        {
          symbol: '其他',
          name: `${sorted.length - head.length} 檔`,
          pct: (tailValue / total) * 100,
          isOther: true,
        },
      ];
    }
    return { market, share: (total / grand) * 100, count: sorted.length, items };
  };

  return {
    tw: section('TW'),
    us: section('US'),
    fund: section('FUND'),
    cashPct: cashTotal > 0 ? (cashTotal / grand) * 100 : null,
  };
}
