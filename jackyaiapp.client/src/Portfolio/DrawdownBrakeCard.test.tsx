import { act } from 'react';
import { createRoot, Root } from 'react-dom/client';
import { renderToStaticMarkup } from 'react-dom/server';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { saveBrakeSettings } from './drawdownBrake';
import { DrawdownBrakeCard } from './DrawdownBrakeCard';
import { MASK, PrivacyContext, PrivacyMode } from './privacy';

import {
  DrawdownBrakeDto,
  DrawdownBrakeRefreshDto,
  PortfolioSummaryDto,
  PositionDto,
} from '@/apis/portfolioApis/types';

const mocks = vi.hoisted(() => ({
  userInfo: vi.fn(),
  brake: vi.fn(),
  lookup: vi.fn(),
  // 兩個 mutation 的 trigger：回傳 `{ unwrap }`，和 RTK Query 的 MutationActionCreatorResult 一樣。
  refreshSignal: vi.fn(),
  refreshPrice: vi.fn(),
}));

vi.mock('@/apis/accountApis', () => ({ useGetUserInfoQuery: () => mocks.userInfo() }));
vi.mock('@/apis/portfolioApis', () => ({
  useGetDrawdownBrakeQuery: (...args: unknown[]) => mocks.brake(...args),
  useLookupStockQuery: (...args: unknown[]) => mocks.lookup(...args),
  useRefreshDrawdownBrakeMutation: () => [(...args: unknown[]) => mocks.refreshSignal(...args)],
  useRefreshPositionPriceMutation: () => [(...args: unknown[]) => mocks.refreshPrice(...args)],
}));

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const DISCLAIMER_TEXT = '依歷史回測規則產生的訊號，過去績效不代表未來，不構成投資建議。';
const SIMULATED_TEXT =
  '2014 年以前的 0050 與 00631L 績效為依報酬指數模擬的數字；1997–2002 年的股利為估計值';

function makeSummary(
  positions: PositionDto[] = [],
  effectiveDate: string | null = '2026-10-09',
  overrides: Partial<PortfolioSummaryDto> = {},
): PortfolioSummaryDto {
  // 與後端同一口徑：流動資產不含不動產與負債；水位 ＝ Σ(市值 × 倍數) ÷ 流動總資產。
  const liquid = positions.filter(
    (p) => p.assetType !== 'REAL_ESTATE' && p.assetType !== 'LIABILITY',
  );
  const liquidGrossAssetsTWD = liquid.reduce((sum, p) => sum + p.marketValueTWD, 0);
  const marketExposureTWD = liquid.reduce((sum, p) => sum + p.marketValueTWD * p.exposureFactor, 0);
  return {
    effectiveDate,
    usdTwdRate: 32,
    totalNetWorthTWD: liquidGrossAssetsTWD,
    twStockTWD: 0,
    usStockTWD: 0,
    cashTWD: 0,
    fundTWD: 0,
    realEstateTWD: 0,
    liabilityTWD: 0,
    liquidGrossAssetsTWD,
    costBasisTWD: 0,
    unrealizedPnlTWD: 0,
    unrealizedPnlPct: 0,
    estimatedSellCostTWD: 0,
    totalDividendsTWD: 0,
    totalReturnPnlTWD: 0,
    totalReturnPct: 0,
    marketExposureTWD,
    exposureOfLiquidAssetsPct:
      liquidGrossAssetsTWD > 0
        ? Math.round((marketExposureTWD / liquidGrossAssetsTWD) * 1000) / 10
        : null,
    liquidExposureMultiple: null,
    drawdownBufferPct: null,
    positions,
    ...overrides,
  };
}

/** 台股持股；00631L、00685L 預設 2 倍、其餘 1 倍（與後端 `PortfolioExposure` 一致）。 */
function stock(
  symbol: string,
  quantity: number,
  currentPrice: number,
  overrides: Partial<PositionDto> = {},
): PositionDto {
  return {
    assetType: 'STOCK',
    symbol,
    name: symbol,
    market: 'TW',
    currency: 'TWD',
    quantity,
    currentPrice,
    marketValueLocal: quantity * currentPrice,
    marketValueTWD: quantity * currentPrice,
    exposureFactor: symbol === '00631L' || symbol === '00685L' ? 2 : 1,
    priceFetchFailed: false,
    dividendsTWD: 0,
    estimatedSellCostTWD: 0,
    tags: [],
    ...overrides,
  };
}

/** TWD 現金。 */
function cash(amount: number, overrides: Partial<PositionDto> = {}): PositionDto {
  return stock('TWD', amount, 1, {
    assetType: 'CASH',
    name: '現金',
    exposureFactor: 0,
    ...overrides,
  });
}

/** 00685L（台股正 2，2 倍）。 */
function p00685L(amount: number): PositionDto {
  return stock('00685L', 1, amount);
}

/** NTSD（美股，1.5 倍）：DECISION-021 起計入整體曝險、歸「其他倍數商品」，不給建議。 */
function ntsd(amount: number): PositionDto {
  return stock('NTSD', 1, amount, { market: 'US', currency: 'USD', exposureFactor: 1.5 });
}

function makeSignal(overrides: Partial<DrawdownBrakeDto> = {}): DrawdownBrakeDto {
  return {
    asOfDate: '2026-10-08',
    state: 'OFFENSE',
    prevState: 'OFFENSE',
    lastSwitchDate: '2026-05-12',
    close: 100,
    high252: 101.2,
    sma60: 95,
    drawdownPct: -1.2,
    distanceToTriggerPct: 6.8,
    isStale: false,
    isInitializing: false,
    estimate: null,
    estimateCorrected: false,
    lastCheckedAt: '2026-10-09T07:05:00Z',
    generatedAt: '2026-10-09T07:06:00Z',
    ...overrides,
  };
}

function enable(enabled: boolean | undefined) {
  mocks.userInfo.mockReturnValue({ data: { data: { drawdownBrakeEnabled: enabled } } });
}

function brakeResult(signal: DrawdownBrakeDto | undefined, extra: Record<string, unknown> = {}) {
  mocks.brake.mockReturnValue({
    data: signal ? { data: signal } : undefined,
    isLoading: false,
    isError: false,
    error: undefined,
    refetch: vi.fn(),
    ...extra,
  });
}

/** 0050 = 100 元、00631L = 50 元、00685L = 20 元的查價結果。 */
function lookupPrices(
  prices: Record<string, number | undefined> = { '0050': 100, '00631L': 50, '00685L': 20 },
) {
  mocks.lookup.mockImplementation((arg: { symbol: string }) => {
    const price = prices[arg.symbol];
    return {
      data: price == null ? undefined : { data: { symbol: arg.symbol, currentPrice: price } },
      isLoading: false,
      isError: price == null,
    };
  });
}

function refreshDto(overrides: Partial<DrawdownBrakeRefreshDto> = {}): DrawdownBrakeRefreshDto {
  return {
    result: 'Updated',
    retryAfterSeconds: null,
    signal: makeSignal({ asOfDate: '2026-10-09' }),
    ...overrides,
  };
}

/** 訊號 refresh 成功（HTTP 200）回應。 */
function signalRefreshResolves(dto: DrawdownBrakeRefreshDto) {
  mocks.refreshSignal.mockReturnValue({ unwrap: () => Promise.resolve({ data: dto }) });
}

/** 請求本身失敗（網路中斷、403、500…）。RTK 的 unwrap() 丟出的就是 FetchBaseQueryError。 */
function signalRefreshRejects(error: unknown) {
  mocks.refreshSignal.mockReturnValue({ unwrap: () => Promise.reject(error) });
}

function priceRefreshResolves() {
  mocks.refreshPrice.mockImplementation(() => ({ unwrap: () => Promise.resolve({ data: {} }) }));
}

function render(summary: PortfolioSummaryDto): string {
  return renderToStaticMarkup(<DrawdownBrakeCard summary={summary} />);
}

/** 與 PrivacyProvider 相同的三種模式對應（value / showRatio），不碰 localStorage。 */
function renderWithPrivacy(summary: PortfolioSummaryDto, mode: PrivacyMode): string {
  return renderToStaticMarkup(
    <PrivacyContext.Provider
      value={{
        mode,
        cycle: () => {},
        value: (s) => (mode === 'full' ? s : MASK),
        change: (s) => (mode === 'hidden' ? MASK : s),
        showRatio: mode !== 'changes',
      }}
    >
      <DrawdownBrakeCard summary={summary} />
    </PrivacyContext.Provider>,
  );
}

/** 把 markup 的標籤去掉、HTML 實體還原，方便比對整句文案。 */
function textOf(html: string): string {
  return html
    .replace(/<[^>]*>/g, ' ')
    .replace(/&#x27;/g, "'")
    .replace(/&amp;/g, '&')
    .replace(/\s+/g, ' ');
}

/** 只取分桶表那一段 markup，方便對「列」做斷言。台股正 2 那一列內有巢狀的 `<ul>`（各檔明細），所以
 * 依 `<ul>` 的開關配對找結尾，不能取第一個 `</ul>`。 */
function renderRows(summary: PortfolioSummaryDto): string {
  const html = render(summary);
  const start = html.search(/<ul[^>]*aria-label="分桶配置與建議"/);
  if (start < 0) return '';
  let depth = 0;
  for (const match of html.slice(start).matchAll(/<ul[\s>]|<\/ul>/g)) {
    depth += match[0] === '</ul>' ? -1 : 1;
    if (depth === 0) return html.slice(start, start + match.index + match[0].length);
  }
  return '';
}

describe('DrawdownBrakeCard', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-10-10T04:00:00Z'));
    enable(true);
    brakeResult(makeSignal());
    lookupPrices();
    signalRefreshResolves(refreshDto());
    priceRefreshResolves();
  });

  afterEach(() => {
    vi.useRealTimers();
    mocks.userInfo.mockReset();
    mocks.brake.mockReset();
    mocks.lookup.mockReset();
    mocks.refreshSignal.mockReset();
    mocks.refreshPrice.mockReset();
  });

  describe('功能開關', () => {
    it('drawdownBrakeEnabled=false → 不渲染，也不發任何請求', () => {
      enable(false);
      expect(render(makeSummary())).toBe('');
      expect(mocks.brake).not.toHaveBeenCalled();
      expect(mocks.lookup).not.toHaveBeenCalled();
    });

    it('欄位缺漏（舊版後端）或使用者資訊還沒回來 → 同樣不渲染、不發請求', () => {
      enable(undefined);
      expect(render(makeSummary())).toBe('');
      mocks.userInfo.mockReturnValue({ data: undefined });
      expect(render(makeSummary())).toBe('');
      expect(mocks.brake).not.toHaveBeenCalled();
      expect(mocks.lookup).not.toHaveBeenCalled();
    });

    it('drawdownBrakeEnabled=true → 掛載並請求大盤訊號', () => {
      const html = render(makeSummary());
      expect(html).toContain('回落煞車');
      expect(mocks.brake).toHaveBeenCalled();
    });
  });

  describe('固定警語常駐（不論卡片處於哪個狀態）', () => {
    const states: Array<[string, () => void]> = [
      ['載入中', () => brakeResult(undefined, { isLoading: true })],
      [
        '錯誤',
        () =>
          brakeResult(undefined, { isError: true, error: { status: 'FETCH_ERROR', error: 'x' } }),
      ],
      [
        '資料準備中',
        () =>
          brakeResult(
            makeSignal({
              isInitializing: true,
              state: null,
              prevState: null,
              asOfDate: null,
              lastSwitchDate: null,
              close: null,
              high252: null,
              sma60: null,
              drawdownPct: null,
              distanceToTriggerPct: null,
            }),
          ),
      ],
      ['資料未更新', () => brakeResult(makeSignal({ isStale: true }))],
      [
        '盤中預估',
        () =>
          brakeResult(
            makeSignal({
              estimate: {
                asOfTime: '2026-10-09T05:15:00Z',
                estimatedClose: 99,
                state: 'DEFENSE',
                prevState: 'OFFENSE',
                drawdownPct: -8.4,
              },
            }),
          ),
      ],
      ['訊號已更正', () => brakeResult(makeSignal({ estimateCorrected: true }))],
      ['正常', () => brakeResult(makeSignal())],
    ];

    it.each(states)('%s', (_name, arrange) => {
      arrange();
      const text = textOf(render(makeSummary()));
      expect(text).toContain(DISCLAIMER_TEXT);
      expect(text).toContain(SIMULATED_TEXT);
    });
  });

  describe('資料狀態', () => {
    it('載入中：顯示骨架，不顯示建議', () => {
      brakeResult(undefined, { isLoading: true });
      const html = render(makeSummary());
      expect(html).toContain('MuiSkeleton');
      const text = textOf(html);
      expect(text).not.toContain('目前狀態');
      expect(text).not.toContain('建議買進');
      expect(text).not.toContain('建議賣出');
    });

    it('錯誤：顯示錯誤訊息與重試', () => {
      brakeResult(undefined, { isError: true, error: { status: 'FETCH_ERROR', error: 'x' } });
      const text = textOf(render(makeSummary()));
      expect(text).toContain('Cannot reach the server');
      expect(text).toContain('重試');
    });

    it('isInitializing：顯示「資料準備中」，且不顯示任何建議', () => {
      brakeResult(
        makeSignal({
          isInitializing: true,
          state: null,
          prevState: null,
          asOfDate: null,
          lastSwitchDate: null,
          close: null,
          high252: null,
          sma60: null,
          drawdownPct: null,
          distanceToTriggerPct: null,
        }),
      );
      const text = textOf(render(makeSummary([cash(1_000_000)])));
      expect(text).toContain('資料準備中');
      expect(text).not.toContain('目前狀態');
      expect(text).not.toContain('建議買進');
      expect(text).not.toContain('建議賣出');
    });

    it('isInitializing 又 isStale：只說「資料準備中」，不說資料未更新或沒有訊號', () => {
      brakeResult(
        makeSignal({
          isInitializing: true,
          isStale: true,
          state: null,
          prevState: null,
          asOfDate: null,
          lastSwitchDate: null,
          close: null,
          high252: null,
          sma60: null,
          drawdownPct: null,
          distanceToTriggerPct: null,
        }),
      );
      const text = textOf(render(makeSummary([cash(1_000_000)])));
      expect(text).toContain('資料準備中');
      expect(text).not.toContain('資料未更新');
      expect(text).not.toContain('目前沒有可用的大盤訊號');
    });

    it('不是準備中、卻缺狀態欄位 → 「目前沒有可用的大盤訊號」，不顯示建議', () => {
      brakeResult(makeSignal({ state: null, drawdownPct: null, distanceToTriggerPct: null }));
      const text = textOf(render(makeSummary([cash(1_000_000)])));
      expect(text).toContain('目前沒有可用的大盤訊號');
      expect(text).not.toContain('目前狀態');
      expect(text).not.toContain('目標配置：');
    });

    it('重抓失敗但手上已有訊號 → 不顯示錯誤，照舊顯示狀態與建議', () => {
      brakeResult(makeSignal(), { isError: true, error: { status: 'FETCH_ERROR', error: 'x' } });
      const text = textOf(render(makeSummary([cash(1_000_000)])));
      expect(text).not.toContain('Cannot reach the server');
      expect(text).toContain('目前狀態');
      expect(text).toContain('目標配置：');
    });

    it('isStale：顯示「資料未更新」，仍沿用最近的狀態計算', () => {
      brakeResult(makeSignal({ isStale: true }));
      const text = textOf(render(makeSummary()));
      expect(text).toContain('資料未更新');
      expect(text).toContain('資料截止 2026-10-08');
      expect(text).toContain('目前狀態');
    });

    it('顯示狀態、資料截止日、距高點、距觸發點、最近切換日', () => {
      const text = textOf(render(makeSummary()));
      expect(text).toContain('進攻');
      expect(text).toContain('2026-10-08');
      expect(text).toContain('-1.2%');
      expect(text).toContain('6.8%');
      expect(text).toContain('2026-05-12');
    });
  });

  describe('建議', () => {
    it('不動：#431 §10 範例文案（有效曝險 1.38 倍、目標 1.4 倍）', () => {
      // A = 1,000,000：0050 200k + 00631L 590k + 現金 210k → E = (200k + 2 × 590k) / 1M = 1.38
      const text = textOf(
        render(makeSummary([stock('0050', 2000, 100), stock('00631L', 11_800, 50), cash(210_000)])),
      );
      expect(text).toContain(
        '目前狀態：進攻（有效曝險 1.38 倍，目標 1.4 倍）。距離近一年高點 -1.2%，距離煞車觸發點還有 6.8%。',
      );
    });

    it('只有現金、沒有 0050／台股正 2 持股（PM 假設 A4）：仍顯示目標配置與建議買進股數（預設買 00631L）', () => {
      brakeResult(makeSignal({ state: 'OFFENSE', prevState: 'DEFENSE' }));
      const text = textOf(render(makeSummary([cash(1_000_000)])));
      expect(text).toContain('目標配置：台股正 2 60% / 0050 20% / 現金 20%');
      expect(text).toContain('建議買進 0050 2,000 股、00631L 12,000 股');
      expect(mocks.lookup).toHaveBeenCalledWith(
        { market: 'TW', symbol: '0050' },
        expect.objectContaining({ skip: false }),
      );
      // 預設買進標的 00631L 要查價；沒持有的 00685L 用不到，不查
      expect(mocks.lookup).toHaveBeenCalledWith(
        { market: 'TW', symbol: '00631L' },
        expect.objectContaining({ skip: false }),
      );
      expect(mocks.lookup).toHaveBeenCalledWith(
        { market: 'TW', symbol: '00685L' },
        expect.objectContaining({ skip: true }),
      );
    });

    it('已持有且快照有價格 → 不另外查價', () => {
      render(makeSummary([stock('0050', 2000, 100), stock('00631L', 11_800, 50), cash(210_000)]));
      for (const call of mocks.lookup.mock.calls) {
        expect(call[1]).toEqual(expect.objectContaining({ skip: true }));
      }
    });

    it('進入防守：賣出建議，賣在買前', () => {
      brakeResult(
        makeSignal({
          state: 'DEFENSE',
          prevState: 'OFFENSE',
          drawdownPct: -8.4,
          distanceToTriggerPct: -0.4,
        }),
      );
      const text = textOf(
        render(makeSummary([stock('0050', 2000, 100), stock('00631L', 12_000, 50), cash(200_000)])),
      );
      expect(text).toContain('觸發回落煞車，進入防守');
      expect(text).toContain('目標配置：現金 100%');
      expect(text).toContain('建議賣出 0050 2,000 股、00631L 12,000 股');
    });

    it('沒有任何流動資產 → 不給建議與股數，只提示先新增資產', () => {
      const text = textOf(render(makeSummary([])));
      expect(text).toContain('目前沒有可納入的流動資產');
      expect(text).not.toContain('建議買進');
      expect(text).not.toContain('建議賣出');
      expect(text).not.toContain('目標配置：');
      expect(text).not.toMatch(/NaN|Infinity/);
    });

    it('目標 E 不可行（k 太高）且沒有其他持股 → 沿用「現金比例過高，無法達到此曝險」', () => {
      saveBrakeSettings({ profile: 'balanced', defenseE: 0, minCashRatio: 0.5 });
      const text = textOf(render(makeSummary([cash(1_000_000)])));
      expect(text).toContain('現金比例過高，無法達到此曝險');
      expect(text).not.toContain('受其他持股');
    });

    it('查不到價格 → 不猜，提示無法計算股數', () => {
      lookupPrices({ '0050': undefined, '00631L': 50 });
      const text = textOf(render(makeSummary([cash(1_000_000)])));
      expect(text).toContain('無法取得 0050 的最新價格');
      expect(text).not.toContain('建議買進');
    });

    it('價格查詢中 → 顯示骨架，不顯示建議', () => {
      mocks.lookup.mockImplementation(() => ({ data: undefined, isLoading: true, isError: false }));
      const html = render(makeSummary([cash(1_000_000)]));
      expect(html).toContain('MuiSkeleton');
      expect(textOf(html)).not.toContain('建議買進');
      expect(textOf(html)).not.toContain('目標配置：');
    });

    it('查到的價格是 0 → 當成查不到，不拿它算股數', () => {
      lookupPrices({ '0050': 0, '00631L': 50 });
      const text = textOf(render(makeSummary([cash(1_000_000)])));
      expect(text).toContain('無法取得 0050 的最新價格');
      expect(text).not.toContain('建議買進');
    });

    it('剛好在觸發點上（距離 0）→ 顯示 0.0%，不說「已低於」', () => {
      brakeResult(makeSignal({ drawdownPct: -8, distanceToTriggerPct: 0 }));
      const text = textOf(render(makeSummary()));
      expect(text).toContain('距煞車觸發點 0.0%');
      expect(text).not.toContain('已低於');
    });

    it('狀態與建議的標籤顏色：進攻綠、防守橘；加碼綠、減碼橘、不動中性', () => {
      const chipColor = (html: string, label: string) => {
        const dom = document.createElement('div');
        dom.innerHTML = html;
        const chip = Array.from(dom.querySelectorAll('.MuiChip-label')).find(
          (el) => el.textContent === label,
        )?.parentElement;
        return chip?.className.match(/MuiChip-color(\w+)/)?.[1];
      };

      brakeResult(makeSignal({ state: 'OFFENSE', prevState: 'DEFENSE' }));
      const buying = render(makeSummary([cash(1_000_000)]));
      expect(chipColor(buying, '進攻')).toBe('Success');
      expect(chipColor(buying, '加碼')).toBe('Success');

      brakeResult(
        makeSignal({
          state: 'DEFENSE',
          prevState: 'OFFENSE',
          drawdownPct: -8.4,
          distanceToTriggerPct: -0.4,
        }),
      );
      const selling = render(
        makeSummary([stock('0050', 2000, 100), stock('00631L', 12_000, 50), cash(200_000)]),
      );
      expect(chipColor(selling, '防守')).toBe('Warning');
      expect(chipColor(selling, '減碼')).toBe('Warning');

      brakeResult(makeSignal());
      const holding = render(
        makeSummary([stock('0050', 2000, 100), stock('00631L', 11_800, 50), cash(210_000)]),
      );
      expect(chipColor(holding, '不動')).toBe('Default');
    });
  });

  describe('整個流動資產（需求 12）', () => {
    // 價格 0050 = 100、00631L = 50（lookupPrices 預設）；金額為 ARCHITECTURE 情境的 1/100，股數整除。
    const s1 = () => [
      stock('0050', 400, 100), // 40,000
      stock('00631L', 500, 50), // 25,000
      cash(20_000),
      stock('2330', 10, 1_500), // 其他一倍 15,000
      ntsd(10_000),
    ];

    describe('12a 曝險與水位', () => {
      it('沒有被排除持股：卡片 E ＝ 頁面水位，且沒有對帳行（不出現空行）', () => {
        const summary = makeSummary([
          stock('0050', 2000, 100),
          stock('00631L', 11_800, 50),
          cash(210_000),
        ]);
        expect(summary.exposureOfLiquidAssetsPct).toBe(138);
        const text = textOf(render(summary));
        expect(text).toContain('有效曝險 1.38 倍');
        expect(text).not.toContain('流動總資產');
        expect(text).not.toContain('不含');
      });

      it('有 NTSD：計入分子與分母，卡片 E ＝ 頁面水位（含 NTSD），沒有對帳行', () => {
        const summary = makeSummary(s1());
        expect(summary.exposureOfLiquidAssetsPct).toBe(109.1); // 水位含 NTSD 的 1.5 倍
        const text = textOf(render(summary));
        expect(text).toContain('有效曝險 1.09 倍'); // ＝ 109.1% ÷ 100，差異僅限四捨五入
        expect(text).toContain('策略範圍資產 NT$110,000'); // ＝ 流動總資產
        expect(text).not.toContain('流動總資產');
        expect(text).not.toContain('不含');
        expect(text).not.toContain('→ 曝險');
      });

      it('改 NTSD 金額會改變 E 與核心的建議（與其他持股一致）', () => {
        const withNtsd = (amount: number) =>
          textOf(render(makeSummary([...s1().slice(0, 4), ntsd(amount)])));
        const small = withNtsd(10_000);
        const large = withNtsd(30_000);
        expect(small).toContain('有效曝險 1.09 倍');
        expect(large).toContain('有效曝險 1.15 倍');
        expect(large).not.toBe(small);
      });

      it('NTSD 歸「其他倍數商品」一列，一律不動，不產生任何買賣 NTSD 的建議', () => {
        const summary = makeSummary(s1());
        const rows = textOf(renderRows(summary));
        expect(rows).toMatch(/其他倍數商品.*建議：不動（由您自行決定）/);
        expect(textOf(render(summary))).not.toMatch(/(買進|賣出) NTSD/);
        // 防守與再平衡也一樣
        brakeResult(makeSignal({ state: 'DEFENSE', prevState: 'OFFENSE', drawdownPct: -9 }));
        expect(textOf(render(summary))).not.toMatch(/(買進|賣出) NTSD/);
      });

      it('沒有被排除持股，所以不論持股組合都沒有對帳行', () => {
        for (const positions of [s1(), [...s1(), p00685L(10_000)], [cash(1_000_000)]]) {
          const text = textOf(render(makeSummary(positions)));
          expect(text).not.toContain('不含');
          expect(text).not.toContain('流動總資產');
        }
      });
    });

    describe('12b 分桶表', () => {
      it('S1（退化對照，沒有 NTSD）：賣 0050 350 股、買 00631L 700 股，現金類不動，其他一倍列不動', () => {
        const summary = makeSummary(s1().slice(0, 4));
        const text = textOf(render(summary));
        expect(text).toContain('建議賣出 0050 350 股；買進 00631L 700 股');
        const rows = textOf(renderRows(summary));
        expect(rows).toContain('0050 目前市值 NT$40,000');
        expect(rows).toContain('減碼：賣出 350 股（約 NT$35,000）');
        expect(rows).toContain('加碼：買進 700 股（約 NT$35,000）');
        expect(rows).toContain('其他一倍商品');
        expect(rows).toContain('建議：不動（由您自行決定）');
        expect(rows).toContain('現金類');
      });

      it('S1 加上 NTSD（計入曝險、不給建議）：A ＝ 110,000，賣 0050 380 股、買 00631L 720 股', () => {
        const summary = makeSummary(s1());
        const text = textOf(render(summary));
        expect(text).toContain('建議賣出 0050 380 股；買進 00631L 720 股');
        expect(textOf(renderRows(summary))).toContain('現金增加 NT$2,000');
        expect(text).toContain('目標配置：台股正 2 55.5% / 0050 1.8% / 現金 20% / 其他持股 22.7%');
      });

      it('列：0050、台股正 2（00631L／00685L）、其他一倍商品、其他倍數商品（NTSD）、現金類', () => {
        const rows = textOf(renderRows(makeSummary(s1())));
        for (const title of [
          '0050',
          '台股正 2（00631L／00685L）',
          '其他一倍商品',
          '其他倍數商品',
          '現金類',
        ]) {
          expect(rows).toContain(title);
        }
        expect(rows).not.toMatch(/NaN|Infinity|\b0 股/);
      });

      it('沒有的桶不顯示：沒有 NTSD 與其他倍數商品 → 不列「其他倍數商品」', () => {
        const rows = textOf(renderRows(makeSummary(s1().slice(0, 4))));
        expect(rows).not.toContain('其他倍數商品');
        expect(rows).toContain('其他一倍商品');
      });

      it('有其他倍數商品（3 倍、反向）才另列「其他倍數商品」，同樣不給建議', () => {
        const rows = textOf(
          renderRows(
            makeSummary([
              stock('0050', 400, 100),
              cash(20_000),
              stock('TQQQ', 10, 100, { market: 'US', currency: 'USD', exposureFactor: 3 }),
              stock('00632R', 100, 10, { exposureFactor: -1 }),
            ]),
          ),
        );
        expect(rows).toContain('其他倍數商品');
        expect(rows.match(/不動（由您自行決定）/g)).toHaveLength(1);
        expect(rows).not.toContain('其他一倍商品');
      });

      it('各列「曝險貢獻」合計 ＝ 整體有效曝險', () => {
        const rows = textOf(renderRows(makeSummary(s1())));
        const contributions = [...rows.matchAll(/目前曝險貢獻 (-?[\d.]+) 倍/g)].map((m) =>
          Number(m[1]),
        );
        expect(contributions.reduce((a, b) => a + b, 0)).toBeCloseTo(1.09, 2);
      });

      it('沒有其他持股：不出現其他持股列與「未經回測」提醒', () => {
        const text = textOf(
          render(
            makeSummary([stock('0050', 2000, 100), stock('00631L', 11_800, 50), cash(210_000)]),
          ),
        );
        expect(text).not.toContain('其他一倍商品');
        expect(text).not.toContain('其他持股');
        expect(text).not.toContain('未經回測');
      });

      it('有其他持股：提醒「未經回測、僅計入整體曝險、不給建議」', () => {
        const text = textOf(render(makeSummary(s1())));
        expect(text).toContain('其他持股未經回測，僅計入整體曝險、不給建議，由您自行決定。');
      });

      it('只有其他倍數商品（NTSD）、沒有其他一倍商品：同樣提醒', () => {
        const text = textOf(
          render(makeSummary([stock('0050', 400, 100), cash(20_000), ntsd(10_000)])),
        );
        expect(text).not.toContain('其他一倍商品');
        expect(text).toContain('其他持股未經回測，僅計入整體曝險、不給建議，由您自行決定。');
      });

      it('沒持有現金、但目標配置有現金 → 仍列現金類與現金類的註記', () => {
        const text = textOf(
          render(makeSummary([stock('0050', 2000, 100), stock('00631L', 12_000, 50)])),
        );
        expect(text).toContain('現金類');
        expect(text).toContain('現金類包含外幣現金與債券／貨幣型商品');
      });

      it('其他持股在進攻、防守、再平衡都「不動」，沒有任何買賣其他持股的建議', () => {
        const positions = [
          stock('0050', 400, 100),
          stock('00631L', 500, 50),
          cash(20_000),
          stock('2330', 10, 1_500),
        ];
        const signals = [
          makeSignal(),
          makeSignal({ state: 'DEFENSE', prevState: 'OFFENSE', drawdownPct: -9 }),
          makeSignal({ state: 'OFFENSE', prevState: 'DEFENSE' }),
        ];
        for (const signal of signals) {
          brakeResult(signal);
          const text = textOf(render(makeSummary(positions)));
          expect(text).not.toMatch(/(買進|賣出) 2330/);
          const rows = textOf(renderRows(makeSummary(positions)));
          expect(rows).toMatch(/其他一倍商品.*建議：不動（由您自行決定）/);
        }
      });

      it('改變其他一倍商品的金額，會改變 E 與核心的建議', () => {
        const withOther = (price: number) =>
          textOf(
            render(
              makeSummary([
                stock('0050', 400, 100),
                stock('00631L', 500, 50),
                cash(20_000),
                stock('2330', 10, price),
              ]),
            ),
          );
        expect(withOther(1_500)).not.toBe(withOther(3_000));
      });
    });

    describe('12c 標示', () => {
      it('S2 切換到防守、其他持股單獨超過目標：減碼到最低，並標示高於目標', () => {
        brakeResult(
          makeSignal({
            state: 'DEFENSE',
            prevState: 'OFFENSE',
            drawdownPct: -9,
            distanceToTriggerPct: -1,
          }),
        );
        const text = textOf(
          render(
            makeSummary([
              stock('0050', 250, 100), // 25,000
              stock('00631L', 500, 50), // 25,000
              cash(10_000),
              stock('2330', 40, 1_000), // 其他一倍 40,000
            ]),
          ),
        );
        expect(text).toContain('建議賣出 0050 250 股、00631L 500 股');
        expect(text).toContain(
          '即使 0050 與台股正 2 全部轉為現金，整體曝險仍有 0.4 倍，高於目標 0 倍；差額來自您自行決定的其他持股',
        );
      });

      it('S2 執行後：「不動」＋同一標示，沒有無法執行的建議', () => {
        brakeResult(
          makeSignal({
            state: 'DEFENSE',
            prevState: 'OFFENSE',
            drawdownPct: -9,
            distanceToTriggerPct: -1,
          }),
        );
        const text = textOf(render(makeSummary([cash(60_000), stock('2330', 40, 1_000)])));
        expect(text).toContain('高於目標 0 倍');
        expect(text).not.toContain('建議賣出');
        expect(text).not.toContain('建議買進');
      });

      it('S3 進攻、其他持股太多：「不動」＋capped 標示，不出現無法執行的建議', () => {
        const text = textOf(
          render(
            makeSummary([
              stock('00631L', 200, 50), // 10,000
              cash(20_000),
              stock('2330', 70, 1_000), // 其他一倍 70,000
            ]),
          ),
        );
        expect(text).toContain(
          '受其他持股與最低現金限制，整體曝險最高只能到 0.9 倍（目標 1.4 倍）',
        );
        expect(text).not.toContain('現金比例過高');
        expect(text).not.toContain('建議買進');
        expect(text).not.toContain('建議賣出');
      });

      it('S3b 同一人 00631L 尚未買、現金 30,000：加碼買 00631L', () => {
        const text = textOf(render(makeSummary([cash(30_000), stock('2330', 70, 1_000)])));
        expect(text).toContain('建議買進 00631L 200 股');
        expect(text).toContain('受其他持股與最低現金限制');
      });
    });

    describe('資料防護', () => {
      it('範圍內有價格取得失敗的持股 → 列出標的，不給建議', () => {
        const text = textOf(
          render(
            makeSummary([
              stock('0050', 400, 100),
              cash(20_000),
              stock('AAPL', 5, 0, {
                market: 'US',
                currency: 'USD',
                priceFetchFailed: true,
                marketValueTWD: 0,
              }),
            ]),
          ),
        );
        expect(text).toContain('1 檔持股價格取得失敗，暫時無法算出建議');
        expect(text).toContain('AAPL');
        expect(text).not.toContain('建議買進');
        expect(text).not.toContain('建議賣出');
        expect(text).not.toContain('目前狀態：進攻（有效曝險');
      });

      it('NTSD 價格取得失敗（現在計入範圍）→ 列出 NTSD，不給建議', () => {
        const failedNtsd = { ...ntsd(0), priceFetchFailed: true };
        const text = textOf(
          render(makeSummary([stock('0050', 400, 100), cash(20_000), failedNtsd])),
        );
        expect(text).toContain('1 檔持股價格取得失敗，暫時無法算出建議');
        expect(text).toContain('NTSD');
        expect(text).not.toContain('建議買進');
        expect(text).not.toContain('建議賣出');
      });

      it('00685L 的 exposureFactor 不是 2 → 不給建議並提示', () => {
        const text = textOf(
          render(makeSummary([stock('00685L', 500, 20, { exposureFactor: 1 }), cash(20_000)])),
        );
        expect(text).toContain('00685L 的曝險倍數與策略定義（2 倍）不符');
        expect(text).not.toContain('建議買進');
        expect(text).not.toContain('建議賣出');
      });

      it('核心 exposureFactor 異常（00631L 不是 2 倍）→ 不給建議並提示', () => {
        const text = textOf(
          render(makeSummary([stock('00631L', 500, 50, { exposureFactor: 1 }), cash(20_000)])),
        );
        expect(text).toContain('00631L 的曝險倍數與策略定義（2 倍）不符');
        expect(text).not.toContain('建議買進');
        expect(text).not.toContain('建議賣出');
      });
    });

    describe('移除策略現金（A19）', () => {
      it('UI 找不到策略現金輸入與相關提示', () => {
        const text = textOf(render(makeSummary([cash(1_000_000)])));
        expect(text).not.toContain('策略現金');
        expect(text).not.toContain('供參考，不會自動採用');
        expect(text).not.toContain('此策略部位占總資產');
      });

      it('舊 localStorage 含 strategyCashTWD → 忽略、不報錯，且不影響建議', () => {
        localStorage.setItem(
          'portfolio.drawdownBrake',
          JSON.stringify({ profile: 'aggressive', defenseE: 0, strategyCashTWD: 5_000_000 }),
        );
        const text = textOf(
          render(
            makeSummary([stock('0050', 2000, 100), stock('00631L', 11_800, 50), cash(210_000)]),
          ),
        );
        expect(text).toContain('依『積極』等級');
        expect(text).toContain('有效曝險 1.38 倍');
        expect(text).not.toContain('策略現金');
      });
    });

    describe('隱私模式', () => {
      it('「隱藏金額」：所有金額被遮，倍數與比例仍在；「只看損益」不顯示水位', () => {
        const summary = makeSummary(s1());
        const hidden = textOf(renderWithPrivacy(summary, 'hidden'));
        expect(hidden).not.toMatch(/NT\$[\d,]{5,}/);
        expect(hidden).toContain('有效曝險 1.09 倍');

        // 「只看損益」不顯示比例／水位；金額也不洩漏
        const changes = textOf(renderWithPrivacy(summary, 'changes'));
        expect(changes).not.toMatch(/NT\$[\d,]{5,}/);
      });

      it('完整模式顯示金額', () => {
        const full = textOf(renderWithPrivacy(makeSummary(s1()), 'full'));
        expect(full).toContain('NT$40,000');
        expect(full).toContain('策略範圍資產 NT$110,000');
      });
    });
  });

  describe('台股正 2 類（00631L＋00685L，DECISION-021）', () => {
    // 價格 0050 = 100、00631L = 50、00685L = 20（lookupPrices 預設）；金額為 ARCHITECTURE 情境的縮小版。
    const lev2Section = (summary: PortfolioSummaryDto) => {
      const rows = textOf(renderRows(summary));
      return rows.slice(rows.indexOf('台股正 2（00631L／00685L）'), rows.indexOf('現金類'));
    };

    it('S4 兩檔都持有、過度曝險：類別一個「減碼」結論與金額，兩檔各自的股數，先賣後買', () => {
      const summary = makeSummary([
        stock('0050', 500, 100), // 50,000
        stock('00631L', 6_000, 50), // 300,000
        stock('00685L', 10_000, 20), // 200,000
        cash(100_000),
      ]);
      const text = textOf(render(summary));
      expect(text).toContain('建議賣出 00631L 1,320 股、00685L 2,200 股；買進 0050 800 股');
      expect(text).toContain('目標配置：台股正 2 60% / 0050 20% / 現金 20%');

      const section = lev2Section(summary);
      expect(section).toContain('目前市值 NT$500,000');
      expect(section).toContain('目標市值 NT$390,000');
      expect(section).toContain('減碼：賣出約 NT$110,000');
      expect(section).toContain(
        '00631L 目前市值 NT$300,000 建議：減碼：賣出 1,320 股（約 NT$66,000）',
      );
      expect(section).toContain(
        '00685L 目前市值 NT$200,000 建議：減碼：賣出 2,200 股（約 NT$44,000）',
      );
      // 兩檔金額相加 ＝ 類別的目前市值；不出現一檔加碼、另一檔減碼
      expect(section).not.toContain('加碼');
      expect(textOf(renderRows(summary))).toContain('現金增加 NT$30,000');
    });

    it('S5 沒持有任何正 2：加碼預設買 00631L（查價），只列 00631L 一檔，不查 00685L', () => {
      const summary = makeSummary([stock('0050', 1_000, 100), cash(500_000)]);
      const text = textOf(render(summary));
      expect(text).toContain('建議買進 0050 200 股、00631L 7,200 股');
      const section = lev2Section(summary);
      expect(section).toContain('加碼：買進約 NT$360,000');
      expect(section).toContain('00631L 目前市值 NT$0 建議：加碼：買進 7,200 股（約 NT$360,000）');
      expect(section).not.toContain('00685L 目前市值');
      expect(mocks.lookup).toHaveBeenCalledWith(
        { market: 'TW', symbol: '00631L' },
        expect.objectContaining({ skip: false }),
      );
      expect(mocks.lookup).toHaveBeenCalledWith(
        { market: 'TW', symbol: '00685L' },
        expect.objectContaining({ skip: true }),
      );
    });

    it('S6 只持有 00685L：全給那一檔，只列 00685L；00631L 用不到、不查價', () => {
      const summary = makeSummary([
        stock('0050', 500, 100), // 50,000
        stock('00685L', 12_500, 20), // 250,000
        cash(50_000),
      ]);
      const text = textOf(render(summary));
      expect(text).toContain('建議賣出 00685L 2,000 股；買進 0050 200 股');
      const section = lev2Section(summary);
      expect(section).toContain('00685L 目前市值 NT$250,000 建議：減碼：賣出 2,000 股');
      expect(section).not.toContain('00631L 目前市值');
      for (const call of mocks.lookup.mock.calls) {
        expect(call[1]).toEqual(expect.objectContaining({ skip: true })); // 全部持有且有快照價
      }
    });

    it('只持有 00685L、沒持有 00631L：00631L 不查價，也不擋建議', () => {
      lookupPrices({ '0050': 100, '00631L': undefined, '00685L': 20 });
      const text = textOf(
        render(makeSummary([stock('0050', 500, 100), stock('00685L', 12_500, 20), cash(50_000)])),
      );
      expect(text).toContain('建議賣出 00685L 2,000 股；買進 0050 200 股');
      expect(text).not.toContain('無法取得');
    });

    it('持有 00685L 但價格無效、查不到 → 不猜，提示無法計算股數', () => {
      lookupPrices({ '0050': 100, '00631L': 50, '00685L': undefined });
      const text = textOf(render(makeSummary([stock('00685L', 500, 0), cash(1_000_000)])));
      expect(text).toContain('無法取得 00685L 的最新價格');
      expect(text).not.toContain('建議買進');
      expect(text).not.toContain('建議賣出');
    });

    it('00685L 沒有分割條目：快照再舊也不因它而擋；有條目的 0050 仍照舊判斷', () => {
      const old = textOf(
        render(makeSummary([stock('00685L', 500, 20), cash(500_000)], '2025-01-01')),
      );
      expect(old).not.toContain('分割');
      expect(old).toContain('建議');
      const split = textOf(
        render(
          makeSummary(
            [stock('0050', 500, 100), stock('00685L', 500, 20), cash(500_000)],
            '2025-06-17',
          ),
        ),
      );
      expect(split).toContain('0050 分割（2025-06-18 1 拆 4）');
    });

    it('持股資料區顯示三檔核心標的的股數／未持有', () => {
      const text = textOf(
        render(makeSummary([stock('0050', 2_000, 100), stock('00685L', 300, 20)])),
      );
      expect(text).toContain('0050 2,000 股、00631L 未持有、00685L 300 股');
    });

    it('其他倍數 2 的台股商品（例如 00675L）不屬於正 2 類：列在其他倍數商品，不給建議', () => {
      const summary = makeSummary([
        stock('0050', 400, 100),
        cash(20_000),
        stock('00675L', 100, 40, { exposureFactor: 2 }),
      ]);
      const rows = textOf(renderRows(summary));
      expect(rows).toMatch(/其他倍數商品.*建議：不動（由您自行決定）/);
      expect(textOf(render(summary))).not.toMatch(/(買進|賣出) 00675L/);
    });
  });

  describe('預估訊號（第二階段）', () => {
    const estimate = {
      asOfTime: '2026-10-09T05:15:00Z',
      estimatedClose: 99,
      state: 'DEFENSE' as const,
      prevState: 'OFFENSE' as const,
      drawdownPct: -8.4,
    };

    it('estimate 非 null → 標示「預估訊號（盤中）」並以預估狀態計算', () => {
      brakeResult(makeSignal({ estimate }));
      const text = textOf(
        render(makeSummary([stock('0050', 2000, 100), stock('00631L', 12_000, 50), cash(200_000)])),
      );
      expect(text).toContain('預估訊號（盤中）');
      expect(text).toContain('觸發回落煞車，進入防守');
      expect(text).toContain('-8.4%');
      // 預估沒有自己的距離欄位：-8.4% 距 -8% 觸發線，已低於 0.4%。
      expect(text).toContain('距煞車觸發點 已低於 0.4%');
    });

    it('estimateCorrected → 顯示「訊號已更正」', () => {
      brakeResult(makeSignal({ estimateCorrected: true }));
      expect(textOf(render(makeSummary()))).toContain('訊號已更正');
    });

    it('沒有 estimate 時不出現預估標示', () => {
      const text = textOf(render(makeSummary()));
      expect(text).not.toContain('預估訊號（盤中）');
      expect(text).not.toContain('訊號已更正');
    });
  });

  describe('持股資料防護（PM R2）', () => {
    it('快照早於 0050 分割日 → 不顯示任何買賣股數，改顯示分割提示', () => {
      const html = render(makeSummary([stock('0050', 500, 100), cash(500_000)], '2025-06-17'));
      const text = textOf(html);
      expect(text).toContain(
        '你的持股快照建立於 0050 分割（2025-06-18 1 拆 4）之前，股數可能未調整，請先更新持股',
      );
      expect(text).not.toContain('建議買進');
      expect(text).not.toContain('建議賣出');
      expect(html).not.toContain('分桶配置與建議');
      expect(text).toContain(DISCLAIMER_TEXT);
    });

    it('快照早於 00631L 分割日 → 擋下，提示 00631L', () => {
      const text = textOf(render(makeSummary([stock('00631L', 500, 50)], '2026-03-30')));
      expect(text).toContain('00631L 分割（2026-03-31 1 拆 22）');
    });

    it('快照晚於分割日 → 正常顯示建議', () => {
      brakeResult(makeSignal({ state: 'OFFENSE', prevState: 'DEFENSE' }));
      const text = textOf(
        render(makeSummary([stock('0050', 2000, 100), cash(800_000)], '2026-10-09')),
      );
      expect(text).not.toContain('分割');
      expect(text).toContain('建議');
    });

    it('沒持有分割標的（只有現金）→ 即使快照很舊也不因分割而擋', () => {
      brakeResult(makeSignal({ state: 'OFFENSE', prevState: 'DEFENSE' }));
      const text = textOf(render(makeSummary([cash(1_000_000)], '2025-01-01')));
      expect(text).not.toContain('分割');
      expect(text).toContain('建議買進');
    });

    it('快照距今 > 7 天 → 顯示「持股資料可能已過期，請先更新持股」', () => {
      const stale = textOf(render(makeSummary([stock('0050', 2000, 100)], '2026-10-02')));
      expect(stale).toContain('持股資料可能已過期，請先更新持股');
      const fresh = textOf(render(makeSummary([stock('0050', 2000, 100)], '2026-10-03')));
      expect(fresh).not.toContain('持股資料可能已過期');
    });

    it('顯示持股資料日期', () => {
      expect(textOf(render(makeSummary([], '2026-10-09')))).toContain('持股資料日期：2026-10-09');
    });

    it('分割提示旁的目標曝險跟著狀態：進攻 1.4 倍、防守 0 倍', () => {
      const summary = makeSummary([stock('0050', 500, 100), cash(500_000)], '2025-06-17');
      expect(textOf(render(summary))).toContain('整體有效曝險的目標是 1.4 倍');

      brakeResult(
        makeSignal({
          state: 'DEFENSE',
          prevState: 'DEFENSE',
          drawdownPct: -9,
          distanceToTriggerPct: -1,
        }),
      );
      expect(textOf(render(summary))).toContain('整體有效曝險的目標是 0 倍');
    });
  });

  describe('同時有好幾個原因擋下建議：分割疑慮蓋過其他提示，查價的狀態等前面都沒問題才顯示', () => {
    // 分割疑慮 → 沒有流動資產 → 範圍內價格取得失敗、核心倍數異常（兩者都列）→ 查價中 → 查不到價。
    const failedAapl = () =>
      stock('AAPL', 5, 0, {
        market: 'US',
        currency: 'USD',
        priceFetchFailed: true,
        marketValueTWD: 0,
      });
    const lookupLoading = () =>
      mocks.lookup.mockImplementation(() => ({ data: undefined, isLoading: true, isError: false }));

    it('分割疑慮＋價格取得失敗 → 只有分割提示', () => {
      const text = textOf(
        render(makeSummary([stock('0050', 500, 100), cash(500_000), failedAapl()], '2025-06-17')),
      );
      expect(text).toContain('0050 分割（2025-06-18 1 拆 4）');
      expect(text).not.toContain('價格取得失敗');
    });

    it('分割疑慮＋核心倍數異常 → 只有分割提示', () => {
      const text = textOf(
        render(
          makeSummary(
            [stock('0050', 500, 100), stock('00685L', 500, 20, { exposureFactor: 1 })],
            '2025-06-17',
          ),
        ),
      );
      expect(text).toContain('0050 分割（2025-06-18 1 拆 4）');
      expect(text).not.toContain('曝險倍數與策略定義');
    });

    it('價格取得失敗＋核心倍數異常 → 兩個提示都列，各自說明', () => {
      const text = textOf(
        render(
          makeSummary([
            stock('00685L', 500, 20, { exposureFactor: 1 }),
            cash(20_000),
            failedAapl(),
          ]),
        ),
      );
      expect(text).toContain('1 檔持股價格取得失敗');
      expect(text).toContain('00685L 的曝險倍數與策略定義（2 倍）不符');
    });

    it('價格取得失敗＋查價中 → 只有價格取得失敗，沒有骨架', () => {
      lookupLoading();
      const html = render(makeSummary([cash(1_000_000), failedAapl()]));
      expect(textOf(html)).toContain('1 檔持股價格取得失敗');
      expect(html).not.toContain('MuiSkeleton');
    });

    it('核心倍數異常＋查價中 → 只有倍數提示，沒有骨架', () => {
      lookupLoading();
      const html = render(
        makeSummary([stock('00685L', 500, 20, { exposureFactor: 1 }), cash(20_000)]),
      );
      expect(textOf(html)).toContain('00685L 的曝險倍數與策略定義（2 倍）不符');
      expect(html).not.toContain('MuiSkeleton');
    });

    it('價格取得失敗＋查不到價 → 只有價格取得失敗', () => {
      lookupPrices({ '0050': undefined, '00631L': 50 });
      const text = textOf(render(makeSummary([cash(1_000_000), failedAapl()])));
      expect(text).toContain('1 檔持股價格取得失敗');
      expect(text).not.toContain('無法取得 0050 的最新價格');
    });

    it('核心倍數異常＋查不到價 → 只有倍數提示', () => {
      lookupPrices({ '0050': undefined, '00631L': 50 });
      const text = textOf(
        render(makeSummary([stock('00685L', 500, 20, { exposureFactor: 1 }), cash(20_000)])),
      );
      expect(text).toContain('00685L 的曝險倍數與策略定義（2 倍）不符');
      expect(text).not.toContain('無法取得 0050 的最新價格');
    });

    it('一檔查價中、另一檔查不到 → 先等查價（骨架），還不說查不到', () => {
      mocks.lookup.mockImplementation((arg: { symbol: string }) =>
        arg.symbol === '00631L'
          ? { data: undefined, isLoading: true, isError: false }
          : { data: undefined, isLoading: false, isError: true },
      );
      const html = render(makeSummary([cash(1_000_000)]));
      expect(html).toContain('MuiSkeleton');
      expect(textOf(html)).not.toContain('無法取得');
    });
  });

  describe('設定與風險說明', () => {
    it('風險等級預設「平衡」', () => {
      const html = render(makeSummary());
      const pressed = [...html.matchAll(/<button[^>]*aria-pressed="true"[^>]*>([^<]*)</g)].map(
        (m) => m[1],
      );
      expect(pressed).toEqual(['平衡']);
    });

    it('風險等級只有保守／平衡／積極，沒有固定槓桿或防守 E ≥ 0.5 的選項', () => {
      const text = textOf(render(makeSummary()));
      for (const label of ['保守', '平衡', '積極']) expect(text).toContain(label);
      expect(text).not.toContain('固定槓桿');
      expect(text).not.toContain('不煞車');
      expect(text).not.toMatch(/防守[^。]{0,10}0\.[5-9]/);
    });

    it('風險等級說明含 #431 §10 三點，績效同時顯示期間與最大回撤', () => {
      const text = textOf(render(makeSummary()));
      expect(text).toContain('槓桿版本在盤整年可能比 0050 虧更多');
      expect(text).toContain('00631L 曾經單日跌 20%，煞車是收盤才判斷，擋不住盤中或單日的急跌');
      expect(text).toContain('平均每年約觸發 3 次進出，約 29% 的時間處於防守');
      expect(text).toContain('1997–2026');
      expect(text).toContain('隔日成交');
      // 對照 reproduce_spec.txt 的 lag2 欄（Boss 更正值）：每一列年化與最大回撤一起出現
      expect(text).toContain('保守 12.1% -27%');
      expect(text).toContain('平衡 15.3% -37%');
      expect(text).toContain('積極 18.4% -45%');
      expect(text).toContain('最大回撤');
    });

    it('回測績效標明範圍：依 0050＋00631L＋現金回測；00685L 視同 00631L、未單獨回測；不是整個流動資產的績效（R8）', () => {
      const text = textOf(render(makeSummary()));
      expect(text).toContain(
        '以上績效依 0050＋00631L＋現金回測；00685L 視同 00631L（您的判斷），未單獨回測，也不是整個流動資產的績效。',
      );
    });

    it('固定警語在有建議、有其他持股（含 NTSD）時仍常駐、一字不改', () => {
      const text = textOf(
        render(makeSummary([stock('0050', 400, 100), cash(20_000), ntsd(10_000)])),
      );
      expect(text).toContain(DISCLAIMER_TEXT);
      expect(text).toContain(SIMULATED_TEXT);
    });

    it('2011 年平衡等級 -28%（0050 -18%），不再出現更正前的數字', () => {
      const text = textOf(render(makeSummary()));
      expect(text).toContain('2011 年平衡等級 -28%，0050 是 -18%');
      for (const stale of ['-29%', '15.4%', '11.9%', '18.1%']) {
        expect(text).not.toContain(stale);
      }
    });

    it('資料限制註記涵蓋 0050 與 00631L 的模擬值、早年股利估計值（回測區塊內也有）', () => {
      const text = textOf(render(makeSummary()));
      expect(text.split(SIMULATED_TEXT).length - 1).toBe(2); // 警語旁一次、回測說明一次
    });

    it('UI 沒有 credit／點數／付費字樣', () => {
      const text = textOf(render(makeSummary()));
      expect(text).not.toMatch(/credit|點數|付費/i);
    });
  });

  describe('資料時間（TASK-043／需求 8）', () => {
    // 當地時間 15:05（不論測試主機在哪個時區），預期畫面也是當地時間。
    const checkedAtLocal1505 = () => new Date(2026, 9, 9, 15, 5).toISOString();

    it('顯示「訊號資料日期」與「伺服器最近檢查」兩段（當地時間）', () => {
      brakeResult(makeSignal({ lastCheckedAt: checkedAtLocal1505() }));
      const text = textOf(render(makeSummary()));
      expect(text).toContain('訊號資料日期：2026-10-08');
      expect(text).toContain('伺服器最近檢查：10/09 15:05');
    });

    it.each([
      ['null', null],
      ['空字串', ''],
      ['無法解析', 'not-a-date'],
      ['epoch（等同沒有時間）', '1970-01-01T00:00:00Z'],
    ])(
      'lastCheckedAt 為%s → 省略「伺服器最近檢查」，不顯示 1970 或 Invalid Date',
      (_name, value) => {
        brakeResult(makeSignal({ lastCheckedAt: value }));
        const text = textOf(render(makeSummary()));
        expect(text).toContain('訊號資料日期：2026-10-08');
        expect(text).not.toContain('伺服器最近檢查');
        expect(text).not.toContain('1970');
        expect(text).not.toContain('Invalid Date');
      },
    );

    it('asOfDate 為 null（資料準備中）→ 日期顯示「—」，仍有重新整理按鈕', () => {
      brakeResult(
        makeSignal({
          isInitializing: true,
          state: null,
          prevState: null,
          asOfDate: null,
          lastSwitchDate: null,
          close: null,
          high252: null,
          sma60: null,
          drawdownPct: null,
          distanceToTriggerPct: null,
        }),
      );
      const text = textOf(render(makeSummary()));
      expect(text).toContain('訊號資料日期：—');
      expect(text).toContain('重新整理資料');
    });

    it.each([
      ['資料未更新', () => makeSignal({ isStale: true })],
      [
        '盤中預估',
        () =>
          makeSignal({
            estimate: {
              asOfTime: '2026-10-09T05:15:00Z',
              estimatedClose: 99,
              state: 'DEFENSE',
              prevState: 'OFFENSE',
              drawdownPct: -8.4,
            },
          }),
      ],
      ['訊號已更正', () => makeSignal({ estimateCorrected: true })],
      ['正常', () => makeSignal()],
    ])('%s：資料時間與按鈕都在，警語與模擬值註記不受影響', (_name, signal) => {
      brakeResult(signal());
      const text = textOf(render(makeSummary()));
      expect(text).toContain('訊號資料日期：2026-10-08');
      expect(text).toContain('重新整理資料');
      expect(text).toContain(DISCLAIMER_TEXT);
      expect(text).toContain(SIMULATED_TEXT);
    });

    it('載入中、或錯誤且沒有任何訊號資料時，沒有資料時間也沒有重新整理按鈕', () => {
      brakeResult(undefined, { isLoading: true });
      expect(textOf(render(makeSummary()))).not.toContain('重新整理資料');

      brakeResult(undefined, { isError: true, error: { status: 'FETCH_ERROR', error: 'x' } });
      const text = textOf(render(makeSummary()));
      expect(text).not.toContain('訊號資料日期');
      expect(text).not.toContain('重新整理資料');
      expect(text).toContain('重試'); // 錯誤狀態的復原路徑仍是原本的重試鈕
    });

    it('剛載入時按鈕可按、沒有任何結果訊息；功能關閉時什麼都不渲染', () => {
      const html = render(makeSummary());
      const dom = document.createElement('div');
      dom.innerHTML = html;
      const button = Array.from(dom.querySelectorAll('button')).find((b) =>
        b.textContent?.includes('重新整理資料'),
      );
      expect(button).toBeDefined();
      expect(button?.disabled).toBe(false);
      // 回饋區（常駐的 live region）一開始是空的。
      expect(dom.querySelector('[aria-live="polite"]')?.textContent).toBe('');
      const text = textOf(html);
      expect(text).not.toContain('已更新到');
      expect(text).not.toContain('目前沒有更新的資料');
      expect(text).not.toContain('後可再次重新整理');
      enable(false);
      expect(render(makeSummary())).toBe('');
    });
  });

  describe('互動', () => {
    let container: HTMLDivElement;
    let root: Root;

    beforeEach(() => {
      container = document.createElement('div');
      document.body.appendChild(container);
      root = createRoot(container);
    });

    afterEach(() => {
      act(() => root.unmount());
      container.remove();
    });

    async function mount() {
      await act(async () => {
        root.render(<DrawdownBrakeCard summary={makeSummary([stock('0050', 2000, 100)])} />);
      });
    }

    function button(label: string): HTMLButtonElement {
      const found = Array.from(container.querySelectorAll('button')).find((b) =>
        b.textContent?.includes(label),
      );
      if (!found) throw new Error(`找不到按鈕：${label}`);
      return found as HTMLButtonElement;
    }

    it('切換風險等級會更新選取並存進 localStorage', async () => {
      await mount();
      expect(button('平衡').getAttribute('aria-pressed')).toBe('true');
      await act(async () => {
        button('積極').click();
      });
      expect(button('積極').getAttribute('aria-pressed')).toBe('true');
      expect(button('平衡').getAttribute('aria-pressed')).toBe('false');
      expect(JSON.parse(localStorage.getItem('portfolio.drawdownBrake') ?? '{}').profile).toBe(
        'aggressive',
      );
    });

    it('進階設定：防守 E 只有 0 與 0.3 兩個選項，沒有 ≥ 0.5', async () => {
      await mount();
      expect(container.textContent).not.toContain('防守時的有效曝險 E');
      await act(async () => {
        button('進階設定').click();
      });
      const group = container.querySelector('[aria-label="防守時的有效曝險 E"]');
      expect(group).not.toBeNull();
      const options = Array.from(group!.querySelectorAll('button')).map((b) => b.textContent);
      expect(options).toEqual(['0（全現金）', '0.3（0050 30% / 現金 70%）']);
      expect(container.textContent).not.toContain('固定槓桿');

      await act(async () => {
        button('0.3').click();
      });
      expect(JSON.parse(localStorage.getItem('portfolio.drawdownBrake') ?? '{}').defenseE).toBe(
        0.3,
      );
    });

    it('最低現金比例 k：超出 0～0.5 標示錯誤且不存；合法值才存；清空回到依風險等級', async () => {
      await mount();
      await act(async () => {
        button('進階設定').click();
      });
      const input = () => container.querySelector('input[type="number"]') as HTMLInputElement;
      const typeK = async (text: string) => {
        await act(async () => {
          const setValue = Object.getOwnPropertyDescriptor(
            HTMLInputElement.prototype,
            'value',
          )!.set!;
          setValue.call(input(), text);
          input().dispatchEvent(new Event('input', { bubbles: true }));
        });
      };
      const savedK = () =>
        JSON.parse(localStorage.getItem('portfolio.drawdownBrake') ?? '{}').minCashRatio;

      await typeK('0.8');
      expect(container.textContent).toContain('請輸入 0 到 0.5 之間的數值');
      expect(savedK()).toBeUndefined();

      await typeK('0.3');
      expect(container.textContent).not.toContain('請輸入 0 到 0.5 之間的數值');
      expect(savedK()).toBe(0.3);

      await typeK('');
      expect(savedK()).toBeUndefined();
      expect(container.textContent).toContain('留空則依風險等級（目前 0.2）');
    });

    it('警語在互動後仍然可見，沒有關閉鈕', async () => {
      await mount();
      await act(async () => {
        button('進階設定').click();
      });
      expect(container.textContent).toContain(DISCLAIMER_TEXT);
      expect(container.querySelector('[aria-label="Close"], [aria-label="關閉"]')).toBeNull();
    });
  });

  describe('重新整理資料（TASK-043／需求 8）', () => {
    let container: HTMLDivElement;
    let root: Root;

    beforeEach(() => {
      // 倒數用 setInterval：只假造 Date 與 interval，讓 React 的 act 仍用真實的 microtask／timeout。
      vi.useFakeTimers({ toFake: ['Date', 'setInterval', 'clearInterval'] });
      vi.setSystemTime(new Date('2026-10-10T04:00:00Z'));
      container = document.createElement('div');
      document.body.appendChild(container);
      root = createRoot(container);
    });

    afterEach(() => {
      act(() => root.unmount());
      container.remove();
    });

    const BOTH = [stock('0050', 2000, 100), stock('00631L', 1000, 50)];

    async function mount(positions: PositionDto[] = BOTH) {
      await act(async () => {
        root.render(<DrawdownBrakeCard summary={makeSummary(positions)} />);
      });
    }

    function refreshButton(): HTMLButtonElement {
      const found = Array.from(container.querySelectorAll('button')).find(
        (b) => b.textContent?.includes('重新整理資料') || b.textContent?.includes('更新中'),
      );
      if (!found) throw new Error('找不到重新整理按鈕');
      return found as HTMLButtonElement;
    }

    async function press() {
      await act(async () => {
        refreshButton().click();
      });
    }

    async function advance(ms: number) {
      await act(async () => {
        vi.advanceTimersByTime(ms);
      });
    }

    const text = () => (container.textContent ?? '').replace(/\s+/g, ' ');

    it('按下：訊號 refresh 一次；持有的 0050、00631L 各重抓一次報價（帶手續費折扣）', async () => {
      await mount();
      await press();
      expect(mocks.refreshSignal).toHaveBeenCalledTimes(1);
      expect(mocks.refreshPrice).toHaveBeenCalledTimes(2);
      expect(mocks.refreshPrice).toHaveBeenCalledWith({ symbol: '0050', feeDiscountRate: 0.28 });
      expect(mocks.refreshPrice).toHaveBeenCalledWith({ symbol: '00631L', feeDiscountRate: 0.28 });
    });

    it('只持有 00631L → 只重抓 00631L', async () => {
      await mount([stock('00631L', 1000, 50)]);
      await press();
      expect(mocks.refreshPrice).toHaveBeenCalledTimes(1);
      expect(mocks.refreshPrice).toHaveBeenCalledWith({ symbol: '00631L', feeDiscountRate: 0.28 });
    });

    it('持有 00685L → 一併重抓 00685L 的報價', async () => {
      await mount([stock('0050', 2000, 100), stock('00685L', 1000, 20)]);
      await press();
      expect(mocks.refreshPrice).toHaveBeenCalledTimes(2);
      expect(mocks.refreshPrice).toHaveBeenCalledWith({ symbol: '0050', feeDiscountRate: 0.28 });
      expect(mocks.refreshPrice).toHaveBeenCalledWith({ symbol: '00685L', feeDiscountRate: 0.28 });
    });

    // 範圍內任一持股（含 NTSD）價格失敗就暫停整張卡片的建議；重新整理是使用者唯一的補救手段。
    const failedStock = (symbol: string) =>
      stock(symbol, 1, 0, { market: 'US', currency: 'USD', priceFetchFailed: true });

    it('價格取得失敗的範圍內股票（NTSD）也重抓，核心標的不重複', async () => {
      await mount([
        stock('0050', 2000, 100),
        { ...ntsd(0), priceFetchFailed: true },
        { ...stock('00631L', 1000, 50), priceFetchFailed: true },
      ]);
      await press();
      expect(mocks.refreshPrice).toHaveBeenCalledTimes(3);
      expect(mocks.refreshPrice).toHaveBeenCalledWith({ symbol: 'NTSD', feeDiscountRate: 0.28 });
    });

    it('價格失敗的股票最多多重抓 5 檔；基金失敗不呼叫 refresh-price', async () => {
      await mount([
        stock('0050', 2000, 100),
        ...Array.from({ length: 8 }, (_, i) => failedStock(`S${i}`)),
        stock('FUND1', 1, 0, { assetType: 'FUND', priceFetchFailed: true }),
      ]);
      await press();
      expect(mocks.refreshPrice).toHaveBeenCalledTimes(1 + 5);
      expect(mocks.refreshPrice).not.toHaveBeenCalledWith(
        expect.objectContaining({ symbol: 'FUND1' }),
      );
    });

    it('價格沒有失敗的其他持股不會被重抓', async () => {
      await mount([stock('0050', 2000, 100), ntsd(10_000), stock('2330', 10, 900)]);
      await press();
      expect(mocks.refreshPrice).toHaveBeenCalledTimes(1);
    });

    it('沒有持有 0050／00631L（只有現金或其他持股）→ 不呼叫 refresh-price，訊號照常 refresh', async () => {
      await mount([cash(1_000_000), stock('2330', 1000, 900)]);
      await press();
      expect(mocks.refreshPrice).not.toHaveBeenCalled();
      expect(mocks.refreshSignal).toHaveBeenCalledTimes(1);
      expect(text()).toContain('已更新到 2026-10-09');
    });

    it('沒按之前不發任何 refresh 請求（不輪詢）', async () => {
      await mount();
      await advance(120_000);
      expect(mocks.refreshSignal).not.toHaveBeenCalled();
      expect(mocks.refreshPrice).not.toHaveBeenCalled();
    });

    it('Updated：顯示「已更新到 {asOfDate}」', async () => {
      await mount();
      await press();
      expect(text()).toContain('已更新到 2026-10-09');
      expect(container.querySelector('[role="status"]')?.textContent).toContain(
        '已更新到 2026-10-09',
      );
    });

    it('NoNewData：顯示「目前沒有更新的資料」，不說已更新', async () => {
      signalRefreshResolves(refreshDto({ result: 'NoNewData' }));
      await mount();
      await press();
      expect(text()).toContain('目前沒有更新的資料');
      expect(text()).not.toContain('已更新到');
    });

    it('SourceUnavailable：顯示「資料未更新…」警告、保留原資料、不顯示成功', async () => {
      signalRefreshResolves(refreshDto({ result: 'SourceUnavailable' }));
      await mount();
      await press();
      expect(text()).toContain('資料未更新：證交所暫時取不到，稍後再試');
      expect(text()).not.toContain('已更新到');
      expect(container.querySelector('[role="alert"]')?.textContent).toContain('資料未更新');
      // 原本的訊號資料仍在畫面上。
      expect(text()).toContain('訊號資料日期：2026-10-08');
      expect(text()).toContain('目前狀態');
    });

    it('Throttled：顯示剩餘秒數、按鈕停用並倒數；歸零後訊息收起、按鈕恢復', async () => {
      signalRefreshResolves(refreshDto({ result: 'Throttled', retryAfterSeconds: 45 }));
      await mount();
      await press();
      expect(text()).toContain('剛剛已有人更新過，請 45 秒後再試');
      expect(text()).not.toContain('已更新到');
      expect(refreshButton().disabled).toBe(true);

      await advance(1000);
      expect(text()).toContain('請 44 秒後再試');

      // 客戶端 30 秒冷卻已過，但全站冷卻還沒：按鈕仍停用。
      await advance(30_000);
      expect(text()).toContain('請 14 秒後再試');
      expect(refreshButton().disabled).toBe(true);

      await advance(14_000);
      expect(text()).not.toContain('剛剛已有人更新過');
      expect(refreshButton().disabled).toBe(false);
    });

    it('Throttled 的秒數比客戶端 30 秒冷卻短：訊息用後端的秒數，之後改顯示客戶端冷卻的原因', async () => {
      signalRefreshResolves(refreshDto({ result: 'Throttled', retryAfterSeconds: 10 }));
      await mount();
      await press();
      expect(text()).toContain('剛剛已有人更新過，請 10 秒後再試');
      expect(text()).not.toContain('後可再次重新整理');

      await advance(10_000);
      expect(text()).not.toContain('剛剛已有人更新過');
      expect(text()).toContain('20 秒後可再次重新整理');
      expect(refreshButton().disabled).toBe(true);

      await advance(20_000);
      expect(refreshButton().disabled).toBe(false);
    });

    it('Throttled 沒給可用秒數 → 用 60 秒後備值，不顯示 NaN／0', async () => {
      signalRefreshResolves(refreshDto({ result: 'Throttled', retryAfterSeconds: null }));
      await mount();
      await press();
      expect(text()).toContain('請 60 秒後再試');
      expect(text()).not.toContain('NaN');
    });

    it('按下後客戶端冷卻 30 秒：按鈕停用、畫面寫出原因與倒數，過了才能再按', async () => {
      await mount();
      await press();
      expect(refreshButton().disabled).toBe(true);
      expect(text()).toContain('30 秒後可再次重新整理');

      await advance(10_000);
      expect(text()).toContain('20 秒後可再次重新整理');
      expect(refreshButton().disabled).toBe(true);

      await advance(20_000);
      expect(refreshButton().disabled).toBe(false);
      expect(text()).not.toContain('後可再次重新整理');

      await press();
      expect(mocks.refreshSignal).toHaveBeenCalledTimes(2);
      expect(mocks.refreshPrice).toHaveBeenCalledTimes(4);
    });

    it('冷卻中按停用的按鈕不會發請求', async () => {
      await mount();
      await press();
      await press();
      await press();
      expect(mocks.refreshSignal).toHaveBeenCalledTimes(1);
      expect(mocks.refreshPrice).toHaveBeenCalledTimes(2);
    });

    it('同一瞬間連點兩次只發一次請求', async () => {
      await mount();
      await act(async () => {
        refreshButton().click();
        refreshButton().click();
      });
      expect(mocks.refreshSignal).toHaveBeenCalledTimes(1);
    });

    it('請求進行中：按鈕停用、標示「更新中…」並有 aria-busy；結束後恢復標籤', async () => {
      let finish: (dto: { data: DrawdownBrakeRefreshDto }) => void = () => {};
      mocks.refreshSignal.mockReturnValue({
        unwrap: () => new Promise((resolve) => (finish = resolve)),
      });
      await mount();
      await press();
      const busy = refreshButton();
      expect(busy.disabled).toBe(true);
      expect(busy.textContent).toContain('更新中…');
      expect(busy.getAttribute('aria-busy')).toBe('true');

      await act(async () => {
        finish({ data: refreshDto() });
      });
      expect(refreshButton().textContent).toContain('重新整理資料');
      expect(refreshButton().getAttribute('aria-busy')).toBe('false');
      expect(text()).toContain('已更新到 2026-10-09');
    });

    it('請求失敗（網路）：顯示錯誤、舊資料仍在、不顯示「已更新」，且冷卻後可再試（不卡死）', async () => {
      signalRefreshRejects({ status: 'FETCH_ERROR', error: 'x' });
      await mount();
      await press();
      expect(text()).toContain('重新整理失敗：Cannot reach the server');
      expect(text()).not.toContain('已更新到');
      expect(text()).toContain('訊號資料日期：2026-10-08');
      expect(text()).toContain('目前狀態');

      await advance(30_000);
      expect(refreshButton().disabled).toBe(false);
      signalRefreshResolves(refreshDto());
      await press();
      expect(text()).toContain('已更新到 2026-10-09');
      expect(text()).not.toContain('重新整理失敗');
    });

    it('DrawdownBrakeNotEnabled（403）：顯示伺服器訊息，按鈕不卡死', async () => {
      signalRefreshRejects({
        status: 403,
        data: { error: { code: 1040301, message: 'This feature is not available.' } },
      });
      await mount();
      await press();
      expect(text()).toContain('重新整理失敗：This feature is not available.');
      await advance(30_000);
      expect(refreshButton().disabled).toBe(false);
    });

    it('報價重抓失敗不影響訊號結果，另外提示報價沒更新', async () => {
      mocks.refreshPrice.mockImplementation(() => ({
        unwrap: () => Promise.reject({ status: 500, data: 'boom' }),
      }));
      await mount();
      await press();
      expect(text()).toContain('已更新到 2026-10-09');
      expect(text()).toContain('持股報價重新取得失敗，仍顯示原本的價格。');
    });

    it('報價成功時不出現報價失敗的提示', async () => {
      await mount();
      await press();
      expect(text()).not.toContain('持股報價重新取得失敗');
    });

    it('後端日後新增不認得的 result → 不顯示成功、也不顯示錯誤', async () => {
      signalRefreshResolves(refreshDto({ result: 'Estimated' as never }));
      await mount();
      await press();
      expect(text()).not.toContain('已更新到');
      expect(text()).not.toContain('重新整理失敗');
      expect(container.querySelector('[role="status"], [role="alert"]')).toBeNull();
    });

    it('按下之後警語與模擬值註記仍常駐', async () => {
      signalRefreshResolves(refreshDto({ result: 'SourceUnavailable' }));
      await mount();
      await press();
      expect(text()).toContain(DISCLAIMER_TEXT);
      expect(text()).toContain(SIMULATED_TEXT);
    });
  });
});
