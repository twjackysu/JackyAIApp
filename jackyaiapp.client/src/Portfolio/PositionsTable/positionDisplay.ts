import { PositionDto } from '@/apis/portfolioApis/types';

export type ChipColor = 'default' | 'primary' | 'warning' | 'success' | 'info' | 'error';

export interface ChipSpec {
  label: string;
  color: ChipColor;
}

const TYPE_CHIP: Record<string, ChipSpec> = {
  STOCK: { label: '股票', color: 'primary' },
  CASH: { label: '現金', color: 'success' },
  FUND: { label: '基金', color: 'info' },
  LIABILITY: { label: '負債', color: 'error' },
  REAL_ESTATE: { label: '不動產', color: 'warning' },
};

/** Positions with no live price/quantity concept — manually valued, "quantity" IS the value. */
export function isFlatValued(p: PositionDto): boolean {
  return p.assetType === 'CASH' || p.assetType === 'LIABILITY' || p.assetType === 'REAL_ESTATE';
}

/** The kind of holding: a stock shows its market (TW / US); an unknown type reads as a stock. */
export function typeChipOf(p: PositionDto): ChipSpec {
  const chip = TYPE_CHIP[p.assetType] ?? TYPE_CHIP.STOCK;
  return p.assetType === 'STOCK' && p.market ? { ...chip, label: p.market } : chip;
}

/** A priced holding whose exposure isn't 1× says so (0曝險, 曝險×2, 曝險×-1); null otherwise. */
export function exposureChipOf(p: PositionDto): ChipSpec | null {
  if (isFlatValued(p) || p.exposureFactor === 1) return null;
  return p.exposureFactor === 0
    ? { label: '0曝險', color: 'default' }
    : { label: `曝險×${p.exposureFactor}`, color: 'warning' };
}

/** Stocks and funds (with a fundId) have a price chart to open; cash and the rest do not. */
export function opensDetail(p: PositionDto, hasHandler: boolean): boolean {
  return (p.assetType === 'STOCK' || (p.assetType === 'FUND' && !!p.symbol)) && hasHandler;
}

export type GroupKey =
  'TW_STOCK' | 'US_STOCK' | 'FUND' | 'CASH' | 'REAL_ESTATE' | 'LIABILITY' | 'OTHER';

const GROUP_LABEL: Record<GroupKey, string> = {
  TW_STOCK: '台股',
  US_STOCK: '美股',
  FUND: '基金',
  CASH: '現金',
  REAL_ESTATE: '不動產',
  LIABILITY: '負債',
  OTHER: '其他',
};

// Liability is listed last — it's debt, not a holding.
const GROUP_ORDER: GroupKey[] = [
  'TW_STOCK',
  'US_STOCK',
  'FUND',
  'CASH',
  'REAL_ESTATE',
  'LIABILITY',
  'OTHER',
];

function groupKeyOf(p: PositionDto): GroupKey {
  if (p.assetType === 'STOCK') return p.market === 'US' ? 'US_STOCK' : 'TW_STOCK';
  if (p.assetType === 'FUND') return 'FUND';
  if (p.assetType === 'CASH') return 'CASH';
  if (p.assetType === 'REAL_ESTATE') return 'REAL_ESTATE';
  if (p.assetType === 'LIABILITY') return 'LIABILITY';
  return 'OTHER';
}

export interface PositionGroup {
  key: GroupKey;
  label: string;
  positions: PositionDto[];
  /** Σ market value (TWD); negative for liability. */
  total: number;
}

/** The holdings by group, in display order, leaving out the empty groups. */
export function groupPositions(positions: PositionDto[]): PositionGroup[] {
  return GROUP_ORDER.map((key) => {
    const members = positions.filter((p) => groupKeyOf(p) === key);
    return {
      key,
      label: GROUP_LABEL[key],
      positions: members,
      total: members.reduce((s, p) => s + p.marketValueTWD, 0),
    };
  }).filter((g) => g.positions.length > 0);
}
