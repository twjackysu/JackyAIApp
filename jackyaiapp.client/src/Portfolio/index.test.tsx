import { act } from 'react';
import { createRoot, Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import Portfolio from './index';

import { PortfolioSummaryDto, PositionDto } from '@/apis/portfolioApis/types';

const mocks = vi.hoisted(() => ({
  summary: vi.fn(),
  fetchHistory: vi.fn(),
  tour: vi.fn(),
  startTour: vi.fn(),
  exportPortfolioCsv: vi.fn(),
  exportNetWorthCsv: vi.fn(),
  drawer: vi.fn(),
  story: vi.fn(),
  historyFetching: false,
}));

vi.mock('@/apis/portfolioApis', () => ({
  useGetPortfolioSummaryQuery: (...args: unknown[]) => mocks.summary(...args),
  useLazyGetNetWorthHistoryQuery: () => [mocks.fetchHistory, { isFetching: mocks.historyFetching }],
}));
vi.mock('./onboardingTour', () => ({
  usePortfolioTour: (...args: unknown[]) => {
    mocks.tour(...args);
    return { startTour: mocks.startTour };
  },
}));
vi.mock('./portfolioExport', () => ({
  exportPortfolioCsv: (...args: unknown[]) => mocks.exportPortfolioCsv(...args),
  exportNetWorthCsv: (...args: unknown[]) => mocks.exportNetWorthCsv(...args),
}));

// The page's own sections are what this file pins; every child with its own data, tests or
// dialogs is a stub that only shows it was rendered (and, where the page wires it, with what).
vi.mock('./NetWorthTrendChart', () => ({ NetWorthTrendChart: () => '[NetWorthTrendChart]' }));
vi.mock('./NetWorthChart', () => ({ NetWorthChart: () => '[NetWorthChart]' }));
vi.mock('./FinancialChangesChart', () => ({
  FinancialChangesChart: () => '[FinancialChangesChart]',
}));
vi.mock('./BenchmarkChart', () => ({ BenchmarkChart: () => '[BenchmarkChart]' }));
vi.mock('./DrawdownChart', () => ({ DrawdownChart: () => '[DrawdownChart]' }));
vi.mock('./ContributionWaterfallChart', () => ({
  ContributionWaterfallChart: ({ positions }: { positions: PositionDto[] }) =>
    `[ContributionWaterfallChart ${positions.length}]`,
}));
vi.mock('./ConcentrationCard', () => ({
  ConcentrationCard: ({ positions }: { positions: PositionDto[] }) =>
    `[ConcentrationCard ${positions.length}]`,
}));
vi.mock('./RebalancingCard', () => ({ RebalancingCard: () => '[RebalancingCard]' }));
vi.mock('./DrawdownBrakeCard', () => ({ DrawdownBrakeCard: () => '[DrawdownBrakeCard]' }));
vi.mock('./FireCard', () => ({ FireCard: () => '[FireCard]' }));
vi.mock('./HoldingsEditor', () => ({ HoldingsEditor: () => '[HoldingsEditor]' }));
vi.mock('./HoldingDetailDrawer', () => ({
  HoldingDetailDrawer: (props: unknown) => {
    mocks.drawer(props);
    return null;
  },
}));
vi.mock('./StoryImageDialog', () => ({
  StoryImageDialog: (props: unknown) => {
    mocks.story(props);
    return null;
  },
}));
vi.mock('./PositionsTable', async () => {
  const { createElement } = await import('react');
  return {
    PositionsTable: (p: {
      positions: PositionDto[];
      liquidGrossAssetsTWD: number;
      onStockClick: (position: PositionDto) => void;
    }) =>
      createElement(
        'button',
        { onClick: () => p.onStockClick(p.positions[0]) },
        `[PositionsTable ${p.positions.length} ${p.liquidGrossAssetsTWD}]`,
      ),
  };
});
vi.mock('./FeeSettingsDialog', async () => {
  const { createElement } = await import('react');
  return {
    FeeSettingsDialog: (p: { feeDiscountRate: number; onSave: (rate: number) => void }) =>
      createElement('button', { onClick: () => p.onSave(0.6) }, `[Fee ${p.feeDiscountRate}]`),
  };
});

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

function position(overrides: Partial<PositionDto> = {}): PositionDto {
  return {
    assetType: 'STOCK',
    symbol: '2330',
    name: '台積電',
    market: 'TW',
    currency: 'TWD',
    quantity: 1000,
    currentPrice: 600,
    marketValueLocal: 600_000,
    marketValueTWD: 600_000,
    exposureFactor: 1,
    priceFetchFailed: false,
    dividendsTWD: 0,
    estimatedSellCostTWD: 0,
    tags: [],
    ...overrides,
  };
}

const cashPosition = () =>
  position({
    assetType: 'CASH',
    symbol: 'TWD',
    name: '現金',
    quantity: 200_000,
    currentPrice: 1,
    marketValueLocal: 200_000,
    marketValueTWD: 200_000,
    exposureFactor: 0,
  });

/** 台股 600,000 + 現金 200,000，沒有成本、不動產、負債。 */
function makeSummary(overrides: Partial<PortfolioSummaryDto> = {}): PortfolioSummaryDto {
  return {
    effectiveDate: '2026-10-20',
    usdTwdRate: 32.456,
    totalNetWorthTWD: 800_000,
    twStockTWD: 600_000,
    usStockTWD: 0,
    cashTWD: 200_000,
    fundTWD: 0,
    realEstateTWD: 0,
    liabilityTWD: 0,
    liquidGrossAssetsTWD: 800_000,
    costBasisTWD: 0,
    unrealizedPnlTWD: 0,
    unrealizedPnlPct: 0,
    estimatedSellCostTWD: 0,
    totalDividendsTWD: 0,
    totalReturnPnlTWD: 0,
    totalReturnPct: 0,
    annualizedReturnPct: null,
    marketExposureTWD: 600_000,
    exposureOfLiquidAssetsPct: 75,
    liquidExposureMultiple: 0.75,
    drawdownBufferPct: 133.3,
    dayChangeTWD: null,
    dayChangePct: null,
    positions: [position(), cashPosition()],
    ...overrides,
  };
}

/** 成本 500,000、未實現 +100,000（+20%）、股息 30,000、年化 12.3%、單日 -5,000（-0.62%）。 */
const withPnl = (overrides: Partial<PortfolioSummaryDto> = {}) =>
  makeSummary({
    costBasisTWD: 500_000,
    unrealizedPnlTWD: 100_000,
    unrealizedPnlPct: 20,
    totalDividendsTWD: 30_000,
    totalReturnPnlTWD: 130_000,
    totalReturnPct: 26,
    annualizedReturnPct: 12.3,
    dayChangeTWD: -5_000,
    dayChangePct: -0.62,
    ...overrides,
  });

function summaryResult(
  summary: PortfolioSummaryDto | undefined,
  extra: Record<string, unknown> = {},
) {
  mocks.summary.mockReturnValue({
    data: summary ? { data: summary } : undefined,
    isLoading: false,
    isFetching: false,
    isError: false,
    ...extra,
  });
}

describe('Portfolio page', () => {
  let container: HTMLDivElement;
  let root: Root;

  beforeEach(() => {
    localStorage.clear();
    localStorage.setItem('portfolio_privacy_mode', 'full');
    summaryResult(makeSummary());
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
  });

  afterEach(() => {
    act(() => root.unmount());
    container.remove();
    vi.clearAllMocks();
    mocks.historyFetching = false;
  });

  async function mount() {
    await act(async () => {
      root.render(<Portfolio />);
    });
  }

  const text = () => container.textContent ?? '';
  const iconButton = (testId: string): HTMLButtonElement | null =>
    container.querySelector(`[data-testid="${testId}"]`)?.closest('button') ?? null;
  const buttonWith = (label: string) =>
    Array.from(container.querySelectorAll('button')).find((b) =>
      b.textContent?.includes(label),
    ) as HTMLButtonElement;
  const click = async (el: Element | null | undefined) => {
    if (!el) throw new Error('nothing to click');
    await act(async () => {
      (el as HTMLElement).click();
    });
  };

  describe('頁面狀態', () => {
    it('載入中：轉圈，沒有卡片也沒有空狀態，也不跑導覽', async () => {
      summaryResult(undefined, { isLoading: true, isFetching: true });
      await mount();
      expect(container.querySelector('.MuiCircularProgress-root')).not.toBeNull();
      expect(container.querySelector('.MuiLinearProgress-root')).toBeNull();
      expect(text()).not.toContain('流動資產');
      expect(text()).not.toContain('還沒有持倉紀錄');
      expect(mocks.tour).toHaveBeenLastCalledWith(false, false);
    });

    it('錯誤、沒有資料：只說載入失敗', async () => {
      summaryResult(undefined, { isError: true });
      await mount();
      expect(text()).toContain('載入失敗，請重新整理');
      expect(text()).not.toContain('還沒有持倉紀錄');
      expect(text()).not.toContain('流動資產');
      expect(mocks.tour).toHaveBeenLastCalledWith(false, false);
    });

    it('錯誤但手上有上次的資料：載入失敗與卡片都在', async () => {
      summaryResult(makeSummary(), { isError: true });
      await mount();
      expect(text()).toContain('載入失敗，請重新整理');
      expect(text()).toContain('流動資產');
      expect(mocks.tour).toHaveBeenLastCalledWith(false, true);
    });

    it('錯誤、上次的資料沒有持倉：不顯示空狀態', async () => {
      summaryResult(makeSummary({ positions: [] }), { isError: true });
      await mount();
      expect(text()).not.toContain('還沒有持倉紀錄');
    });

    it('沒有持倉：空狀態，沒有卡片與匯出鈕，導覽用空狀態版', async () => {
      summaryResult(makeSummary({ positions: [] }));
      await mount();
      expect(text()).toContain('還沒有持倉紀錄');
      expect(text()).toContain('點右上「建立持倉」填入你的股票、現金和基金');
      expect(text()).not.toContain('流動資產');
      expect(iconButton('FileDownloadOutlinedIcon')).toBeNull();
      expect(mocks.tour).toHaveBeenLastCalledWith(true, false);
    });

    it('有持倉：依序是兩張卡、各圖表區、回落煞車、FIRE、持股表，導覽用完整版', async () => {
      await mount();
      const order = [
        '流動資產',
        '資產配置',
        '淨值走勢',
        '[NetWorthTrendChart]',
        '資產與負債組成',
        '[NetWorthChart]',
        '淨值變化量',
        '[FinancialChangesChart]',
        '大盤對照',
        '[BenchmarkChart]',
        '報酬貢獻度',
        '[ContributionWaterfallChart 2]',
        '回撤分析',
        '[DrawdownChart]',
        '持股集中度',
        '[ConcentrationCard 2]',
        '再平衡建議',
        '[RebalancingCard]',
        '[DrawdownBrakeCard]',
        '財務目標 (FIRE)',
        '[FireCard]',
        '[PositionsTable 2 800000]',
      ];
      const positions = order.map((s) => text().indexOf(s));
      expect(positions.every((p) => p >= 0)).toBe(true);
      expect([...positions].sort((a, b) => a - b)).toEqual(positions);
      expect(text()).not.toContain('還沒有持倉紀錄');
      expect(iconButton('FileDownloadOutlinedIcon')).not.toBeNull();
      expect(mocks.tour).toHaveBeenLastCalledWith(true, true);
    });

    it('背景重抓：頂端進度條與「更新中…」；首次載入沒有', async () => {
      summaryResult(makeSummary(), { isFetching: true });
      await mount();
      expect(container.querySelector('.MuiLinearProgress-root')).not.toBeNull();
      expect(text()).toContain('· 更新中…');
    });

    it('副標題帶持倉生效日；沒有就不帶', async () => {
      await mount();
      expect(text()).toContain(
        '台股 · 美股 · 現金 · 基金 統一以台幣淨值追蹤 · 持倉生效日 2026-10-20',
      );

      summaryResult(makeSummary({ effectiveDate: null }));
      await mount();
      expect(text()).not.toContain('持倉生效日');
    });
  });

  describe('頁首動作', () => {
    it('手續費折數：從 localStorage 讀（預設 0.28）並帶進查詢；儲存後寫回並以新折數重查', async () => {
      await mount();
      expect(mocks.summary).toHaveBeenLastCalledWith(0.28);
      expect(text()).toContain('[Fee 0.28]');

      await click(buttonWith('[Fee'));
      expect(localStorage.getItem('portfolio.feeDiscountRate')).toBe('0.6');
      expect(mocks.summary).toHaveBeenLastCalledWith(0.6);
      expect(text()).toContain('[Fee 0.6]');
    });

    it('已存過的折數會被讀出來', async () => {
      localStorage.setItem('portfolio.feeDiscountRate', '0.5');
      await mount();
      expect(mocks.summary).toHaveBeenLastCalledWith(0.5);
    });

    it('導覽鈕重新開始導覽', async () => {
      await mount();
      await click(iconButton('HelpOutlineOutlinedIcon'));
      expect(mocks.startTour).toHaveBeenCalledTimes(1);
    });

    it('隱私鈕依序切換：完整 → 只看損益 → 隱藏金額 → 完整，並記住', async () => {
      await mount();
      expect(text()).toContain('NT$800,000');
      const toggle = () =>
        iconButton('VisibilityIcon') ??
        iconButton('ShowChartIcon') ??
        iconButton('VisibilityOffIcon');

      await click(toggle());
      expect(toggle()?.getAttribute('aria-label')).toBe('只看損益（點擊：隱藏金額）');
      expect(localStorage.getItem('portfolio_privacy_mode')).toBe('changes');
      expect(text()).not.toContain('NT$800,000');

      await click(toggle());
      expect(toggle()?.getAttribute('aria-label')).toBe('隱藏金額（點擊：完整顯示）');

      await click(toggle());
      expect(toggle()?.getAttribute('aria-label')).toBe('完整顯示（點擊：只看損益）');
      expect(text()).toContain('NT$800,000');
    });

    it('沒存過隱私設定：預設隱藏金額', async () => {
      localStorage.removeItem('portfolio_privacy_mode');
      await mount();
      expect(text()).not.toContain('NT$800,000');
      expect(iconButton('VisibilityOffIcon')).not.toBeNull();
    });
  });

  describe('匯出選單', () => {
    const menuItem = (label: string) =>
      Array.from(document.body.querySelectorAll('[role="menuitem"]')).find(
        (el) => el.textContent === label,
      ) as HTMLElement | undefined;

    it('目前持倉 → 以目前的摘要匯出 CSV', async () => {
      const summary = makeSummary();
      summaryResult(summary);
      await mount();
      await click(iconButton('FileDownloadOutlinedIcon'));
      await click(menuItem('目前持倉'));
      expect(mocks.exportPortfolioCsv).toHaveBeenCalledWith(summary);
    });

    it('淨值歷史 → 抓 180 天再匯出 CSV', async () => {
      const points = [{ date: '2026-10-01' }];
      mocks.fetchHistory.mockReturnValue({ unwrap: () => Promise.resolve({ data: points }) });
      await mount();
      await click(iconButton('FileDownloadOutlinedIcon'));
      await click(menuItem('淨值歷史 (時間序列)'));
      expect(mocks.fetchHistory).toHaveBeenCalledWith({ days: 180 });
      expect(mocks.exportNetWorthCsv).toHaveBeenCalledWith(points);
    });

    it('抓淨值歷史期間，匯出鈕停用', async () => {
      mocks.historyFetching = true;
      await mount();
      expect(iconButton('FileDownloadOutlinedIcon')?.disabled).toBe(true);
    });

    it('IG 限動圖：只有美股或只有基金也能開；日期帶持倉生效日', async () => {
      summaryResult(
        makeSummary({ positions: [position({ symbol: 'VOO', market: 'US', currency: 'USD' })] }),
      );
      await mount();
      expect(mocks.story).toHaveBeenLastCalledWith(
        expect.objectContaining({ open: false, date: '2026-10-20' }),
      );
      await click(iconButton('FileDownloadOutlinedIcon'));
      expect(menuItem('IG 限動圖 (持股比例)')?.getAttribute('aria-disabled')).toBeNull();

      act(() => root.unmount());
      root = createRoot(container);
      summaryResult(
        makeSummary({ positions: [position({ assetType: 'FUND', symbol: 'F1', market: 'FUND' })] }),
      );
      await mount();
      await click(iconButton('FileDownloadOutlinedIcon'));
      expect(menuItem('IG 限動圖 (持股比例)')?.getAttribute('aria-disabled')).toBeNull();
    });

    it('IG 限動圖：只有現金時停用；有台股時開啟，日期為持倉生效日（沒有就今天）', async () => {
      summaryResult(makeSummary({ positions: [cashPosition()] }));
      await mount();
      await click(iconButton('FileDownloadOutlinedIcon'));
      expect(menuItem('IG 限動圖 (持股比例)')?.getAttribute('aria-disabled')).toBe('true');

      act(() => root.unmount());
      root = createRoot(container);
      summaryResult(makeSummary({ effectiveDate: null }));
      await mount();
      expect(mocks.story).toHaveBeenLastCalledWith(expect.objectContaining({ open: false }));
      await click(iconButton('FileDownloadOutlinedIcon'));
      await click(menuItem('IG 限動圖 (持股比例)'));
      const today = new Date();
      const iso = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`;
      expect(mocks.story).toHaveBeenLastCalledWith(
        expect.objectContaining({ open: true, date: iso }),
      );
    });
  });

  it('點持股表的股票 → 開明細抽屜；關掉後清空', async () => {
    await mount();
    expect(mocks.drawer).toHaveBeenLastCalledWith(
      expect.objectContaining({ position: null, open: false }),
    );
    await click(buttonWith('[PositionsTable'));
    const opened = mocks.drawer.mock.lastCall![0] as {
      position: PositionDto;
      open: boolean;
      onClose: () => void;
    };
    expect(opened.open).toBe(true);
    expect(opened.position.symbol).toBe('2330');
    await act(async () => opened.onClose());
    expect(mocks.drawer).toHaveBeenLastCalledWith(
      expect.objectContaining({ position: null, open: false }),
    );
  });

  describe('流動資產卡', () => {
    const card = () =>
      container.querySelector('[data-tour="portfolio-tour-networth"]')?.textContent ?? '';

    it('大字是流動總資產；總資產淨值、流動資產淨值只在和它不同時才列', async () => {
      await mount();
      expect(card()).toContain('NT$800,000');
      expect(card()).not.toContain('總資產淨值');
      expect(card()).not.toContain('流動資產淨值');

      // 不動產 3,000,000、負債 500,000：總淨值 3,300,000，流動淨值 300,000
      summaryResult(
        makeSummary({
          realEstateTWD: 3_000_000,
          liabilityTWD: 500_000,
          totalNetWorthTWD: 3_300_000,
        }),
      );
      await mount();
      expect(card()).toContain('總資產淨值 NT$3,300,000');
      expect(card()).toContain('流動資產淨值 NT$300,000');
    });

    it('沒有成本、沒有單日資料：只有大字，沒有任何損益列', async () => {
      await mount();
      expect(card()).not.toContain('單日異動');
      expect(card()).not.toContain('未實現損益');
      expect(card()).not.toContain('含息損益');
      expect(card()).not.toContain('年化');
    });

    it('有成本：單日異動、未實現、含息、年化各一列，含金額、百分比與說明', async () => {
      summaryResult(withPnl({ estimatedSellCostTWD: 1_234 }));
      await mount();
      expect(card()).toContain('▼ NT$5,000-0.62%單日異動 (與前一交易日比)');
      expect(card()).toContain(
        '▲ NT$100,000+20.00%未實現損益(已填成本部分)，已扣估計台股賣出費用 NT$1,234',
      );
      expect(card()).toContain('▲ NT$130,000+26.00%含息損益(含累計股息 NT$30,000，稅前)');
      expect(card()).toContain('年化 +12.30%年化報酬率 (XIRR·含息·估)');
      const chip = (label: string) =>
        Array.from(container.querySelectorAll('.MuiChip-label')).find(
          (el) => el.textContent === label,
        )?.parentElement;
      expect(chip('年化 +12.30%')?.className).toContain('MuiChip-outlined');
      expect(chip('+20.00%')?.className).toContain('MuiChip-filled');
    });

    it('沒有估計賣出費用就不提；股息為 0 不列含息；年化未知不列', async () => {
      summaryResult(
        withPnl({ estimatedSellCostTWD: 0, totalDividendsTWD: 0, annualizedReturnPct: null }),
      );
      await mount();
      expect(card()).toContain('未實現損益(已填成本部分)');
      expect(card()).not.toContain('已扣估計台股賣出費用');
      expect(card()).not.toContain('含息損益');
      expect(card()).not.toContain('年化');
    });

    it('沒有成本時，就算有股息、年化也不列；單日異動照列', async () => {
      summaryResult(withPnl({ costBasisTWD: 0 }));
      await mount();
      expect(card()).toContain('單日異動');
      expect(card()).not.toContain('未實現損益');
      expect(card()).not.toContain('含息損益');
      expect(card()).not.toContain('年化');
    });

    it('單日異動為 0：▲，百分比 +0.00% 用上漲色', async () => {
      summaryResult(makeSummary({ dayChangeTWD: 0, dayChangePct: 0 }));
      await mount();
      expect(card()).toContain('▲ NT$0+0.00%單日異動');
      const chip = Array.from(container.querySelectorAll('.MuiChip-label')).find(
        (el) => el.textContent === '+0.00%',
      )?.parentElement as HTMLElement;
      expect(getComputedStyle(chip).color).toBe('rgb(34, 197, 94)');
    });

    it('單日百分比未知：只列金額', async () => {
      summaryResult(makeSummary({ dayChangeTWD: 2_000, dayChangePct: null }));
      await mount();
      expect(card()).toContain('▲ NT$2,000單日異動');
    });

    it('只看損益：金額大字被遮，損益金額仍在，百分比與年化不顯示', async () => {
      localStorage.setItem('portfolio_privacy_mode', 'changes');
      summaryResult(withPnl());
      await mount();
      expect(card()).not.toContain('NT$800,000');
      expect(card()).toContain('▲ NT$100,000未實現損益');
      expect(card()).not.toContain('%');
      expect(card()).not.toContain('年化');
    });

    it('隱藏金額：所有金額被遮，百分比與年化仍在', async () => {
      localStorage.setItem('portfolio_privacy_mode', 'hidden');
      summaryResult(withPnl());
      await mount();
      expect(card()).not.toMatch(/NT\$\d/);
      expect(card()).toContain('+20.00%');
      expect(card()).toContain('年化 +12.30%');
    });
  });

  describe('資產配置卡', () => {
    const card = () =>
      container.querySelector('[data-tour="portfolio-tour-allocation"]')?.textContent ?? '';

    it('列出有金額的類別，比例以總資產（含不動產、不扣負債）為分母；0 的類別不列', async () => {
      summaryResult(
        makeSummary({
          usStockTWD: 0,
          fundTWD: 0,
          realEstateTWD: 200_000,
          liabilityTWD: 100_000,
        }),
      );
      await mount();
      // 分母 600k + 200k + 200k = 1,000,000
      expect(card()).toContain('台股NT$600,000 (60.0%)');
      expect(card()).toContain('現金NT$200,000 (20.0%)');
      expect(card()).toContain('不動產NT$200,000 (20.0%)');
      expect(card()).toContain('負債-NT$100,000 (-10.0%)');
      expect(card()).not.toContain('美股');
      expect(card()).not.toContain('基金');
    });

    it('沒有不動產、沒有負債：兩列都不出現', async () => {
      await mount();
      expect(card()).not.toContain('不動產');
      expect(card()).not.toContain('負債');
    });

    it('市場曝險、水位、槓桿、抗跌空間與匯率', async () => {
      await mount();
      expect(card()).toContain('市場曝險');
      expect(card()).toContain('NT$600,000');
      expect(card()).toContain('水位 75.0%');
      expect(card()).toContain('槓桿 0.75×');
      expect(card()).toContain('抗跌空間再跌 133.3% 歸零');
      expect(card()).toContain('USD/TWD = 32.46');
    });

    it('槓桿為 null 是資不抵債；抗跌空間 ≤ 0 是淨值已為負；水位、抗跌空間未知就不列', async () => {
      summaryResult(
        makeSummary({
          liquidExposureMultiple: null,
          drawdownBufferPct: -5,
          exposureOfLiquidAssetsPct: null,
        }),
      );
      await mount();
      expect(card()).toContain('槓桿 資不抵債');
      expect(card()).toContain('淨值已為負');
      expect(card()).not.toContain('水位');

      summaryResult(makeSummary({ drawdownBufferPct: null }));
      await mount();
      expect(card()).not.toContain('抗跌空間');
    });

    it('隱藏金額：類別、負債與曝險金額都被遮，比例仍在', async () => {
      localStorage.setItem('portfolio_privacy_mode', 'hidden');
      summaryResult(makeSummary({ liabilityTWD: 100_000 }));
      await mount();
      expect(card()).not.toMatch(/NT\$\d/);
      expect(card()).toContain('(75.0%)');
      expect(card()).toContain('(-12.5%)');
    });

    it('抗跌空間剛好 0：淨值已為負', async () => {
      summaryResult(makeSummary({ drawdownBufferPct: 0 }));
      await mount();
      expect(card()).toContain('抗跌空間淨值已為負');
    });

    it('顏色：資不抵債用虧損色；抗跌空間依距離（<10% 紅、<25% 橘、其餘綠）', async () => {
      const colourOf = (label: string) => {
        const el = Array.from(
          container.querySelectorAll('[data-tour="portfolio-tour-allocation"] span'),
        ).find((e) => e.textContent === label) as HTMLElement;
        return getComputedStyle(el).color;
      };
      summaryResult(makeSummary({ liquidExposureMultiple: null, drawdownBufferPct: 5 }));
      await mount();
      expect(colourOf('資不抵債')).toBe('rgb(239, 68, 68)');
      expect(colourOf('再跌 5.0% 歸零')).toBe('rgb(239, 68, 68)');

      summaryResult(makeSummary());
      await mount();
      expect(colourOf('0.75×')).not.toBe('rgb(239, 68, 68)');
      expect(colourOf('再跌 133.3% 歸零')).toBe('rgb(34, 197, 94)');
    });

    it('只看損益：比例、水位、槓桿、抗跌空間都不顯示', async () => {
      localStorage.setItem('portfolio_privacy_mode', 'changes');
      summaryResult(makeSummary({ liabilityTWD: 100_000 }));
      await mount();
      expect(card()).not.toContain('%');
      expect(card()).not.toContain('水位');
      expect(card()).not.toContain('槓桿');
      expect(card()).not.toContain('抗跌空間');
      expect(card()).toContain('USD/TWD = 32.46');
    });
  });
});
