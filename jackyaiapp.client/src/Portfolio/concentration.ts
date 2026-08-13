import { PositionDto } from '@/apis/portfolioApis/types';

export interface ConcentrationLevel {
  label: string;
  color: string;
}

export interface ConcentrationResult {
  sorted: PositionDto[];
  shares: number[]; // index-aligned with `sorted`, each a % of total holdings market value
  hhi: number;
  top1Pct: number;
  top3Pct: number;
  top5Pct: number;
  level: ConcentrationLevel;
}

// Standard HHI (Herfindahl-Hirschman Index) bands, computed on a 0-10000 scale (Σ share%²) — the
// same thresholds antitrust regulators use for market concentration, repurposed here for "how
// much of your invested capital rides on how few names."
export function concentrationLevel(hhi: number): ConcentrationLevel {
  if (hhi >= 2500) return { label: '高度集中', color: '#ef4444' };
  if (hhi >= 1500) return { label: '中度集中', color: '#f59e0b' };
  return { label: '分散', color: '#22c55e' };
}

/** Pure arithmetic over PortfolioSummaryDto.positions — position-level concentration risk only:
 * cash/liability/real estate are excluded, since a house or a debt isn't "stock-picking risk" the
 * way an oversized single security is. Returns null when there's nothing to compute over. */
export function computeConcentration(positions: PositionDto[]): ConcentrationResult | null {
  const holdings = positions.filter(
    (p) => (p.assetType === 'STOCK' || p.assetType === 'FUND') && p.marketValueTWD > 0,
  );
  const total = holdings.reduce((sum, p) => sum + p.marketValueTWD, 0);
  if (holdings.length === 0 || total <= 0) return null;

  const sorted = [...holdings].sort((a, b) => b.marketValueTWD - a.marketValueTWD);
  const shares = sorted.map((p) => (p.marketValueTWD / total) * 100);
  const hhi = shares.reduce((sum, s) => sum + s * s, 0);

  return {
    sorted,
    shares,
    hhi,
    top1Pct: shares[0] ?? 0,
    top3Pct: shares.slice(0, 3).reduce((a, b) => a + b, 0),
    top5Pct: shares.slice(0, 5).reduce((a, b) => a + b, 0),
    level: concentrationLevel(hhi),
  };
}
