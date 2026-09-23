import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';

import { ComparisonResult } from './ComparisonResult';

import type { ComparisonSlot } from '../../hooks/useFinanceAnalysis';
import type { CategoryScore, StockAnalysisResultData } from '@/apis/financeApis/types';

const categoryScore = (overrides: Partial<CategoryScore>): CategoryScore => ({
  category: 'Technical',
  score: 60,
  weight: 0.2,
  weightedScore: 12,
  direction: 'Neutral',
  summary: '',
  indicatorCount: 1,
  ...overrides,
});

const stockData = (overrides: Partial<StockAnalysisResultData>): StockAnalysisResultData => ({
  stockCode: '2330',
  companyName: '台積電',
  market: 'TW',
  latestClose: 600,
  indicators: [],
  scoring: null,
  risk: null,
  dataRange: '2024-01-01 ~ 2024-12-31',
  configuration: {
    includeTechnical: true,
    includeChip: true,
    includeFundamental: true,
    includeScoring: true,
    includeRisk: true,
    onlyIndicators: [],
    excludeIndicators: [],
  },
  generatedAt: '2024-12-31T00:00:00Z',
  ...overrides,
});

const successSlot = (code: string, data: Partial<StockAnalysisResultData>): ComparisonSlot => ({
  code,
  market: 'TW',
  status: 'success',
  data: stockData({ stockCode: code, ...data }),
  errorMessage: null,
});

const errorSlot = (code: string, errorMessage: string): ComparisonSlot => ({
  code,
  market: 'TW',
  status: 'error',
  data: null,
  errorMessage,
});

describe('ComparisonResult', () => {
  it('renders one category row per category present across the compared stocks', () => {
    const slots = [
      successSlot('2330', {
        companyName: '台積電',
        scoring: {
          stockCode: '2330',
          companyName: '台積電',
          latestClose: 600,
          overallScore: 80,
          overallDirection: 'Bullish',
          recommendation: '',
          categoryScores: [
            categoryScore({ category: 'Technical', score: 80 }),
            categoryScore({ category: 'Chip', score: 70 }),
          ],
          indicators: [],
          risk: { level: 'Low', factors: [], divergenceScore: 0 },
          dataRange: '',
          generatedAt: '',
        },
      }),
      successSlot('2317', {
        companyName: '鴻海',
        scoring: {
          stockCode: '2317',
          companyName: '鴻海',
          latestClose: 100,
          overallScore: 55,
          overallDirection: 'Neutral',
          recommendation: '',
          categoryScores: [categoryScore({ category: 'Technical', score: 55 })],
          indicators: [],
          risk: { level: 'Medium', factors: [], divergenceScore: 0 },
          dataRange: '',
          generatedAt: '',
        },
      }),
    ];

    const html = renderToStaticMarkup(<ComparisonResult slots={slots} />);

    // Technical is present for both stocks, Chip only for 2330, Fundamental for neither.
    expect(html).toContain('技術面');
    expect(html).toContain('籌碼面');
    expect(html).not.toContain('基本面');
    expect(html).toContain('台積電');
    expect(html).toContain('鴻海');
    expect(html).toContain('綜合評分: 80');
    expect(html).toContain('綜合評分: 55');
  });

  it('shows an inline error for a failed slot without hiding the successful ones', () => {
    const slots = [
      successSlot('2330', {
        companyName: '台積電',
        scoring: {
          stockCode: '2330',
          companyName: '台積電',
          latestClose: 600,
          overallScore: 80,
          overallDirection: 'Bullish',
          recommendation: '',
          categoryScores: [categoryScore({ category: 'Technical', score: 80 })],
          indicators: [],
          risk: { level: 'Low', factors: [], divergenceScore: 0 },
          dataRange: '',
          generatedAt: '',
        },
      }),
      errorSlot('BADCODE', '找不到股票代碼'),
    ];

    const html = renderToStaticMarkup(<ComparisonResult slots={slots} />);

    expect(html).toContain('找不到 BADCODE：找不到股票代碼');
    expect(html).toContain('台積電');
    expect(html).toContain('技術面');
  });
});
