import { PortfolioSummaryDto } from '@/apis/portfolioApis/types';

const TYPE_LABEL: Record<string, string> = { STOCK: '股票', CASH: '現金', FUND: '基金' };

/** Quote a CSV cell only when it contains a comma, quote or newline. */
const csvCell = (v: string | number | null | undefined) => {
  const s = v == null ? '' : String(v);
  return /[",\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
};

/**
 * Export the current holdings as a CSV that opens cleanly in Excel.
 * A UTF-8 BOM is prepended so Excel renders Chinese correctly; values are
 * raw (unmasked, unrounded) so the user can do their own analysis.
 */
export function exportPortfolioCsv(summary: PortfolioSummaryDto) {
  const headers = [
    '代號',
    '名稱',
    '類型',
    '市場',
    '計價幣別',
    '成本幣別',
    '數量',
    '現價/NAV',
    '市值(TWD)',
    '成本(TWD)',
    '未實現損益(TWD)',
    '損益%',
    '曝險倍數',
  ];

  const rows = summary.positions.map((p) => [
    p.symbol,
    p.name,
    TYPE_LABEL[p.assetType] ?? p.assetType,
    p.assetType === 'STOCK' ? p.market : '',
    p.currency,
    p.costCurrency ?? '',
    p.quantity,
    p.assetType === 'CASH' ? '' : p.currentPrice,
    p.marketValueTWD,
    p.costBasisTWD ?? '',
    p.unrealizedPnlTWD ?? '',
    p.unrealizedPnlPct ?? '',
    p.exposureFactor,
  ]);

  const bom = String.fromCharCode(0xfeff); // Excel needs a UTF-8 BOM to render Chinese
  const csv = bom + [headers, ...rows].map((r) => r.map(csvCell).join(',')).join('\r\n');

  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
  const url = URL.createObjectURL(blob);
  const date = summary.effectiveDate ?? new Date().toISOString().slice(0, 10);
  const a = document.createElement('a');
  a.href = url;
  a.download = `portfolio_${date}.csv`;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}
