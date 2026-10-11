import { act } from 'react';
import { createRoot, Root } from 'react-dom/client';
import { renderToStaticMarkup } from 'react-dom/server';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { saveBrakeSettings } from './drawdownBrake';
import { DrawdownBrakeCard } from './DrawdownBrakeCard';
import { MASK, PrivacyContext, PrivacyMode } from './privacy';

import { DrawdownBrakeDto, PortfolioSummaryDto, PositionDto } from '@/apis/portfolioApis/types';

const mocks = vi.hoisted(() => ({
  userInfo: vi.fn(),
  brake: vi.fn(),
  lookup: vi.fn(),
}));

vi.mock('@/apis/accountApis', () => ({ useGetUserInfoQuery: () => mocks.userInfo() }));
vi.mock('@/apis/portfolioApis', () => ({
  useGetDrawdownBrakeQuery: (...args: unknown[]) => mocks.brake(...args),
  useLookupStockQuery: (...args: unknown[]) => mocks.lookup(...args),
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

/** 台股持股；00631L 預設 2 倍、其餘 1 倍（與後端 `PortfolioExposure` 一致）。 */
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
    exposureFactor: symbol === '00631L' ? 2 : 1,
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

/** 被排除的 NTSD（美股，1.5 倍）。 */
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

/** 0050 = 100 元、00631L = 50 元的查價結果。 */
function lookupPrices(prices: Record<string, number | undefined> = { '0050': 100, '00631L': 50 }) {
  mocks.lookup.mockImplementation((arg: { symbol: string }) => {
    const price = prices[arg.symbol];
    return {
      data: price == null ? undefined : { data: { symbol: arg.symbol, currentPrice: price } },
      isLoading: false,
      isError: price == null,
    };
  });
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

/** 只取分桶表那一段 markup，方便對「列」做斷言。 */
function renderRows(summary: PortfolioSummaryDto): string {
  return render(summary).match(/<ul[^>]*aria-label="分桶配置與建議"[\s\S]*?<\/ul>/)?.[0] ?? '';
}

describe('DrawdownBrakeCard', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-10-10T04:00:00Z'));
    enable(true);
    brakeResult(makeSignal());
    lookupPrices();
  });

  afterEach(() => {
    vi.useRealTimers();
    mocks.userInfo.mockReset();
    mocks.brake.mockReset();
    mocks.lookup.mockReset();
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

    it('只有現金、沒有 0050／00631L 持股（PM 假設 A4）：仍顯示目標配置與建議買進股數', () => {
      brakeResult(makeSignal({ state: 'OFFENSE', prevState: 'DEFENSE' }));
      const text = textOf(render(makeSummary([cash(1_000_000)])));
      expect(text).toContain('目標配置：00631L 60% / 0050 20% / 現金 20%');
      expect(text).toContain('建議買進 0050 2,000 股、00631L 12,000 股');
      expect(mocks.lookup).toHaveBeenCalledWith(
        { market: 'TW', symbol: '0050' },
        expect.objectContaining({ skip: false }),
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

      it('有 NTSD：出現對帳行，NTSD 不計入分子與分母', () => {
        const summary = makeSummary(s1());
        expect(summary.exposureOfLiquidAssetsPct).toBe(109.1); // 水位含 NTSD 的 1.5 倍
        const text = textOf(render(summary));
        expect(text).toContain(
          '流動總資產 NT$110,000（水位 109.1%）；不含 NTSD NT$10,000；策略範圍 NT$100,000 → 曝險 1.05 倍',
        );
        expect(text).toContain('有效曝險 1.05 倍');
      });

      it('改 NTSD 金額只改對帳行，不改 E、桶與建議', () => {
        const strip = (positions: PositionDto[]) =>
          textOf(render(makeSummary(positions)))
            // 拿掉對帳行，其餘文字必須逐字相同
            .replace(/流動總資產 [^→]*→ 曝險 [\d.]+ 倍/, '')
            .replace(/\s+/g, ' ');
        const base = s1().slice(0, 4);
        const small = strip([...base, ntsd(10_000)]);
        const large = strip([...base, ntsd(900_000)]);
        const none = strip(base);
        expect(small).toContain('建議');
        expect(large).toBe(small);
        expect(none).toBe(small);
      });

      it('NTSD 不出現在任何一列，也不產生建議（只在對帳行）', () => {
        const summary = makeSummary(s1());
        expect(textOf(renderRows(summary))).not.toContain('NTSD');
        expect(textOf(render(summary))).not.toMatch(/(買進|賣出) NTSD/);
      });
    });

    describe('12b 分桶表', () => {
      it('S1：賣 0050 350 股、買 00631L 700 股，現金類不動，其他一倍列不動', () => {
        const text = textOf(render(makeSummary(s1())));
        expect(text).toContain('建議賣出 0050 350 股；買進 00631L 700 股');
        const rows = textOf(renderRows(makeSummary(s1())));
        expect(rows).toContain('0050 目前市值 NT$40,000');
        expect(rows).toContain('減碼：賣出 350 股（約 NT$35,000）');
        expect(rows).toContain('加碼：買進 700 股（約 NT$35,000）');
        expect(rows).toContain('其他一倍商品');
        expect(rows).toContain('建議：不動（由您自行決定）');
        expect(rows).toContain('現金類');
      });

      it('列：0050、00631L、其他一倍商品、現金類；沒有的桶不顯示（其他倍數商品、空列）', () => {
        const rows = textOf(renderRows(makeSummary(s1())));
        for (const title of ['0050', '00631L', '其他一倍商品', '現金類']) {
          expect(rows).toContain(title);
        }
        expect(rows).not.toContain('其他倍數商品');
        expect(rows).not.toMatch(/NaN|Infinity|\b0 股/);
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
        expect(contributions.reduce((a, b) => a + b, 0)).toBeCloseTo(1.05, 2);
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
          '即使 0050／00631L 全部轉為現金，整體曝險仍有 0.4 倍，高於目標 0 倍；差額來自您自行決定的其他持股',
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

      it('價格失敗的是被排除的 NTSD → 不影響建議', () => {
        const failedNtsd = { ...ntsd(0), priceFetchFailed: true };
        const text = textOf(
          render(makeSummary([stock('0050', 400, 100), cash(20_000), failedNtsd])),
        );
        expect(text).not.toContain('價格取得失敗');
        expect(text).toContain('建議');
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
        expect(hidden).toContain('有效曝險 1.05 倍');
        expect(hidden).toContain('（水位 109.1%）');

        const changes = textOf(renderWithPrivacy(summary, 'changes'));
        expect(changes).not.toContain('水位 109.1%');
      });

      it('完整模式顯示金額', () => {
        const full = textOf(renderWithPrivacy(makeSummary(s1()), 'full'));
        expect(full).toContain('NT$40,000');
        expect(full).toContain('策略範圍資產 NT$100,000');
      });
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

    it('回測績效標明範圍：依 0050＋00631L＋現金回測，不是整個流動資產的績效（R8）', () => {
      const text = textOf(render(makeSummary()));
      expect(text).toContain('以上績效依 0050＋00631L＋現金回測，不是整個流動資產的績效。');
    });

    it('固定警語在有建議、有其他持股、有對帳行時仍常駐、一字不改', () => {
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

    it('警語在互動後仍然可見，沒有關閉鈕', async () => {
      await mount();
      await act(async () => {
        button('進階設定').click();
      });
      expect(container.textContent).toContain(DISCLAIMER_TEXT);
      expect(container.querySelector('[aria-label="Close"], [aria-label="關閉"]')).toBeNull();
    });
  });
});
