import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';

import { ComparisonResult } from './ComparisonResult';

import type { ComparisonSlot } from '../../hooks/useFinanceAnalysis';
import type { CategoryScore, StockAnalysisResultData } from '@/apis/financeApis/types';

function categoryScore(overrides: Partial<CategoryScore>): CategoryScore {
  return {
    category: 'Technical',
    score: 60,
    weight: 0.2,
    weightedScore: 12,
    direction: 'Bullish',
    summary: 'summary',
    indicatorCount: 3,
    ...overrides,
  };
}

function stockData(
  stockCode: string,
  categories: CategoryScore[],
  overrides: Partial<StockAnalysisResultData> = {},
): StockAnalysisResultData {
  return {
    stockCode,
    companyName: `Company ${stockCode}`,
    market: 'TW',
    latestClose: 100,
    indicators: [],
    scoring: {
      stockCode,
      companyName: `Company ${stockCode}`,
      latestClose: 100,
      overallScore: 65,
      overallDirection: 'Bullish',
      recommendation: 'hold',
      categoryScores: categories,
      indicators: [],
      risk: { level: 'Low', factors: [], divergenceScore: 0 },
      dataRange: '2026-01-01~2026-09-23',
      generatedAt: '2026-09-23T00:00:00Z',
    },
    risk: { level: 'Low', factors: [], divergenceScore: 0 },
    dataRange: '2026-01-01~2026-09-23',
    configuration: {
      includeTechnical: true,
      includeChip: true,
      includeFundamental: true,
      includeScoring: true,
      includeRisk: true,
      onlyIndicators: [],
      excludeIndicators: [],
    },
    generatedAt: '2026-09-23T00:00:00Z',
    ...overrides,
  };
}

function render(slots: ComparisonSlot[]) {
  return renderToStaticMarkup(<ComparisonResult slots={slots} />);
}

describe('ComparisonResult', () => {
  it('only renders category rows for categories present across the compared stocks', () => {
    const slots: ComparisonSlot[] = [
      {
        code: '2330',
        market: 'TW',
        data: stockData('2330', [categoryScore({ category: 'Technical' })]),
        error: null,
      },
      {
        code: 'AAPL',
        market: 'US',
        data: stockData('AAPL', [categoryScore({ category: 'Chip' })]),
        error: null,
      },
    ];
    const html = render(slots);
    expect(html).toContain('技術面');
    expect(html).toContain('籌碼面');
    expect(html).not.toContain('基本面');
  });

  it('shows an inline error for a failed slot without dropping the successful ones', () => {
    const slots: ComparisonSlot[] = [
      {
        code: '2330',
        market: 'TW',
        data: stockData('2330', [categoryScore({ category: 'Technical' })]),
        error: null,
      },
      { code: 'BADCODE', market: 'US', data: null, error: '找不到 BADCODE' },
    ];
    const html = render(slots);
    expect(html).toContain('Company 2330');
    expect(html).toContain('找不到 BADCODE');
    expect(html).toContain('技術面');
  });
});
