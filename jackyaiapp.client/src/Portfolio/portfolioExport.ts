import { NetWorthDataPoint, PortfolioSummaryDto } from '@/apis/portfolioApis/types';

const TYPE_LABEL: Record<string, string> = {
  STOCK: '股票',
  CASH: '現金',
  FUND: '基金',
  LIABILITY: '負債',
};

/** Quote a CSV cell only when it contains a comma, quote or newline. */
const csvCell = (v: string | number | null | undefined) => {
  const s = v == null ? '' : String(v);
  return /[",\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
};

/** Build a UTF-8-BOM CSV string and trigger a download. */
function downloadCsv(rows: (string | number | null | undefined)[][], filename: string) {
  const bom = String.fromCharCode(0xfeff); // Excel needs a UTF-8 BOM to render Chinese
  const csv = bom + rows.map((r) => r.map(csvCell).join(',')).join('\r\n');
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}

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
    '累計股息(TWD)',
    '含息損益(TWD)',
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
    p.assetType === 'CASH' || p.assetType === 'LIABILITY' ? '' : p.currentPrice,
    // Negative for LIABILITY (amount owed) — exported as-is, unmasked/unrounded.
    p.marketValueTWD,
    p.costBasisTWD ?? '',
    p.unrealizedPnlTWD ?? '',
    p.unrealizedPnlPct ?? '',
    p.dividendsTWD || '',
    p.unrealizedPnlTWD != null || p.dividendsTWD ? (p.unrealizedPnlTWD ?? 0) + p.dividendsTWD : '',
    p.exposureFactor,
  ]);

  const date = summary.effectiveDate ?? new Date().toISOString().slice(0, 10);
  downloadCsv([headers, ...rows], `portfolio_${date}.csv`);
}

/**
 * Export the daily net-worth history (time series) as a CSV for trend analysis.
 */
export function exportNetWorthCsv(points: NetWorthDataPoint[]) {
  const headers = [
    '日期',
    '總淨值(TWD)',
    '台股(TWD)',
    '美股(TWD)',
    '現金(TWD)',
    '基金(TWD)',
    '負債(TWD)',
  ];
  const rows = points.map((p) => [
    p.date,
    p.totalTWD,
    p.twStockTWD,
    p.usStockTWD,
    p.cashTWD,
    p.fundTWD,
    p.liabilityTWD,
  ]);
  downloadCsv([headers, ...rows], `portfolio_networth_history.csv`);
}
