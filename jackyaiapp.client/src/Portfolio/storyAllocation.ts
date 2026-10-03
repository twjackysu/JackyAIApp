import { PositionDto } from '@/apis/portfolioApis/types';

export interface StoryItem {
  symbol: string;
  name: string;
  /** % of this market's stock value (each section sums to 100 on its own). */
  pct: number;
  /** True for the folded "其他" tail row. */
  isOther: boolean;
}

export interface StorySection {
  market: 'TW' | 'US';
  /** This market's % of combined TW+US stock value. */
  shareOfStocks: number;
  /** Number of distinct holdings, including those folded into "其他". */
  count: number;
  items: StoryItem[];
}

export interface StoryAllocation {
  tw: StorySection | null;
  us: StorySection | null;
}

/** Rows a section can list before the tail is folded into one "其他" row. */
export const STORY_MAX_ROWS = 8;

/**
 * Stock-only allocation for the IG story image. Each market is normalised to 100% on its own so
 * the image still reads correctly after the user crops the US half off the bottom. Only
 * positively-valued STOCK positions count; duplicate symbols (one per account) are merged.
 */
export function buildStoryAllocation(
  positions: PositionDto[],
  maxRows = STORY_MAX_ROWS,
): StoryAllocation {
  type Holding = StoryItem & { value: number };
  const byMarket = { TW: new Map<string, Holding>(), US: new Map<string, Holding>() };
  for (const p of positions) {
    if (p.assetType !== 'STOCK' || !(p.marketValueTWD > 0)) continue;
    const market = p.market === 'US' ? 'US' : 'TW';
    const existing = byMarket[market].get(p.symbol);
    if (existing) existing.value += p.marketValueTWD;
    else
      byMarket[market].set(p.symbol, {
        symbol: p.symbol,
        name: p.name,
        pct: 0,
        isOther: false,
        value: p.marketValueTWD,
      });
  }

  const sum = (m: Map<string, { value: number }>) =>
    [...m.values()].reduce((s, x) => s + x.value, 0);
  const twTotal = sum(byMarket.TW);
  const usTotal = sum(byMarket.US);
  const grand = twTotal + usTotal;

  const section = (market: 'TW' | 'US', total: number): StorySection | null => {
    if (total <= 0) return null;
    const sorted = [...byMarket[market].values()].sort((a, b) => b.value - a.value);
    const toItem = ({ symbol, name, value }: { symbol: string; name: string; value: number }) => ({
      symbol,
      name,
      pct: (value / total) * 100,
      isOther: false,
    });
    let items: StoryItem[];
    if (sorted.length <= maxRows) {
      items = sorted.map(toItem);
    } else {
      const head = sorted.slice(0, maxRows - 1);
      const tailValue = sorted.slice(maxRows - 1).reduce((s, x) => s + x.value, 0);
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
    return { market, shareOfStocks: (total / grand) * 100, count: sorted.length, items };
  };

  return { tw: section('TW', twTotal), us: section('US', usTotal) };
}
