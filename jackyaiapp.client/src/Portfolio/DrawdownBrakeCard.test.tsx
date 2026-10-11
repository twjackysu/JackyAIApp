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
const SHARE_LABEL = '此策略部位占總資產';

function makeSummary(
  positions: PositionDto[] = [],
  effectiveDate: string | null = '2026-10-09',
  liquidGrossAssetsTWD = 0,
): PortfolioSummaryDto {
  return {
    effectiveDate,
    usdTwdRate: 32,
    totalNetWorthTWD: 0,
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
    marketExposureTWD: 0,
    exposureOfLiquidAssetsPct: null,
    liquidExposureMultiple: null,
    drawdownBufferPct: null,
    positions,
  };
}

function stock(symbol: string, quantity: number, currentPrice: number): PositionDto {
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
    exposureFactor: 1,
    priceFetchFailed: false,
    dividendsTWD: 0,
    estimatedSellCostTWD: 0,
    tags: [],
  };
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
      saveBrakeSettings({ profile: 'balanced', defenseE: 0, strategyCashTWD: 1_000_000 });
      const text = textOf(render(makeSummary()));
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
      // V = 1,000,000：0050 200k + 00631L 590k + 策略現金 210k → E = (200k + 2 × 590k) / 1M = 1.38
      saveBrakeSettings({ profile: 'balanced', defenseE: 0, strategyCashTWD: 210_000 });
      const text = textOf(
        render(makeSummary([stock('0050', 2000, 100), stock('00631L', 11_800, 50)])),
      );
      expect(text).toContain(
        '目前狀態：進攻（有效曝險 1.38 倍，目標 1.4 倍）。距離近一年高點 -1.2%，距離煞車觸發點還有 6.8%。',
      );
    });

    it('只有策略現金、沒有 0050／00631L 持股（PM 假設 A4）：仍顯示目標配置與建議買進股數', () => {
      brakeResult(makeSignal({ state: 'OFFENSE', prevState: 'DEFENSE' }));
      saveBrakeSettings({ profile: 'balanced', defenseE: 0, strategyCashTWD: 1_000_000 });
      const text = textOf(render(makeSummary([])));
      expect(text).toContain('目標配置：00631L 60% / 0050 20% / 現金 20%');
      expect(text).toContain('建議買進 0050 2,000 股、00631L 12,000 股');
      expect(mocks.lookup).toHaveBeenCalledWith(
        { market: 'TW', symbol: '0050' },
        expect.objectContaining({ skip: false }),
      );
    });

    it('已持有且快照有價格 → 不另外查價', () => {
      saveBrakeSettings({ profile: 'balanced', defenseE: 0, strategyCashTWD: 210_000 });
      render(makeSummary([stock('0050', 2000, 100), stock('00631L', 11_800, 50)]));
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
      saveBrakeSettings({ profile: 'balanced', defenseE: 0, strategyCashTWD: 200_000 });
      const text = textOf(
        render(makeSummary([stock('0050', 2000, 100), stock('00631L', 12_000, 50)])),
      );
      expect(text).toContain('觸發回落煞車，進入防守');
      expect(text).toContain('目標配置：現金 100%');
      expect(text).toContain('建議賣出 0050 2,000 股、00631L 12,000 股');
    });

    it('沒有任何策略資產（持股與策略現金都沒有）→ 提示先輸入策略現金，不給股數', () => {
      const text = textOf(render(makeSummary([])));
      expect(text).toContain('請先在下方輸入策略現金');
      expect(text).not.toContain('建議買進');
    });

    it('目標 E 不可行（k 太高）→ 顯示「現金比例過高，無法達到此曝險」', () => {
      saveBrakeSettings({
        profile: 'balanced',
        defenseE: 0,
        minCashRatio: 0.5,
        strategyCashTWD: 1_000_000,
      });
      expect(textOf(render(makeSummary([])))).toContain('現金比例過高，無法達到此曝險');
    });

    it('查不到價格 → 不猜，提示無法計算股數', () => {
      lookupPrices({ '0050': undefined, '00631L': 50 });
      saveBrakeSettings({ profile: 'balanced', defenseE: 0, strategyCashTWD: 1_000_000 });
      const text = textOf(render(makeSummary([])));
      expect(text).toContain('無法取得 0050 的最新價格');
      expect(text).not.toContain('建議買進');
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
      saveBrakeSettings({ profile: 'balanced', defenseE: 0, strategyCashTWD: 200_000 });
      const text = textOf(
        render(makeSummary([stock('0050', 2000, 100), stock('00631L', 12_000, 50)])),
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

  describe('策略部位占總資產（需求 11 / A9）', () => {
    // V = 0050 200k + 00631L 590k + 策略現金 210k = 1,000,000
    const heldPositions = () => [stock('0050', 2000, 100), stock('00631L', 11_800, 50)];
    const arrange = () =>
      saveBrakeSettings({ profile: 'balanced', defenseE: 0, strategyCashTWD: 210_000 });

    it('正常值：V ÷ 流動總資產，小數 1 位，附分母定義', () => {
      arrange();
      const text = textOf(render(makeSummary(heldPositions(), '2026-10-09', 3_000_000)));
      expect(text).toContain('此策略部位占總資產 33.3%（分母為流動總資產，不含不動產與負債）');

      const even = textOf(render(makeSummary(heldPositions(), '2026-10-09', 4_000_000)));
      expect(even).toContain('此策略部位占總資產 25.0%');
    });

    it('只有策略現金、沒有持股（A4）：V = 策略現金，仍顯示占比', () => {
      saveBrakeSettings({ profile: 'balanced', defenseE: 0, strategyCashTWD: 1_000_000 });
      const text = textOf(render(makeSummary([], '2026-10-09', 8_000_000)));
      expect(text).toContain('此策略部位占總資產 12.5%');
    });

    it('在目標配置那一行之後、固定警語之外，且警語仍常駐', () => {
      arrange();
      const text = textOf(render(makeSummary(heldPositions(), '2026-10-09', 4_000_000)));
      expect(text.indexOf('策略總資產 NT$1,000,000')).toBeGreaterThan(-1);
      expect(text.indexOf(SHARE_LABEL)).toBeGreaterThan(text.indexOf('策略總資產 NT$1,000,000'));
      expect(text.indexOf(SHARE_LABEL)).toBeGreaterThan(text.indexOf(DISCLAIMER_TEXT));
      expect(text).toContain(DISCLAIMER_TEXT);
      expect(text).toContain(SIMULATED_TEXT);
    });

    it.each([
      ['分母為 0', 0],
      ['分母為負數', -1],
      ['分母不是有限數', Number.NaN],
    ])('%s → 整行不顯示，也沒有 NaN／Infinity', (_name, denominator) => {
      arrange();
      const text = textOf(render(makeSummary(heldPositions(), '2026-10-09', denominator)));
      expect(text).not.toContain(SHARE_LABEL);
      expect(text).not.toMatch(/NaN|Infinity/);
      expect(text).toContain('策略總資產 NT$1,000,000'); // 建議本身不受影響
    });

    it('缺資料：舊版後端沒有 liquidGrossAssetsTWD → 不顯示', () => {
      arrange();
      const summary = makeSummary(heldPositions(), '2026-10-09', 4_000_000);
      delete (summary as Partial<PortfolioSummaryDto>).liquidGrossAssetsTWD;
      const text = textOf(render(summary));
      expect(text).not.toContain(SHARE_LABEL);
      expect(text).not.toMatch(/NaN|Infinity/);
    });

    it('缺價格（持有的標的快照與查價都沒有價格）→ V 未知，不顯示', () => {
      lookupPrices({ '0050': undefined, '00631L': 50 });
      arrange();
      const held0050 = { ...stock('0050', 2000, 100), currentPrice: 0, priceFetchFailed: true };
      const text = textOf(
        render(makeSummary([held0050, stock('00631L', 11_800, 50)], '2026-10-09', 4_000_000)),
      );
      expect(text).toContain('無法取得 0050 的最新價格');
      expect(text).not.toContain(SHARE_LABEL);
      expect(text).not.toMatch(/NaN|Infinity/);
    });

    it('缺價格（沒持有、查價失敗）→ 同樣不顯示', () => {
      lookupPrices({ '0050': undefined, '00631L': 50 });
      saveBrakeSettings({ profile: 'balanced', defenseE: 0, strategyCashTWD: 1_000_000 });
      const text = textOf(render(makeSummary([], '2026-10-09', 8_000_000)));
      expect(text).not.toContain(SHARE_LABEL);
    });

    it('價格查詢中 → V 未知，不顯示', () => {
      mocks.lookup.mockImplementation(() => ({ data: undefined, isLoading: true, isError: false }));
      saveBrakeSettings({ profile: 'balanced', defenseE: 0, strategyCashTWD: 1_000_000 });
      const text = textOf(render(makeSummary([], '2026-10-09', 8_000_000)));
      expect(text).not.toContain(SHARE_LABEL);
    });

    it('V 為 0（沒有持股、策略現金也是 0）→ 不顯示 0.0%，只提示先輸入策略現金', () => {
      const text = textOf(render(makeSummary([], '2026-10-09', 8_000_000)));
      expect(text).toContain('請先在下方輸入策略現金');
      expect(text).not.toContain(SHARE_LABEL);
      expect(text).not.toMatch(/NaN|Infinity/);
    });

    it('快照早於分割日（股數不可信）→ 不顯示', () => {
      saveBrakeSettings({ profile: 'balanced', defenseE: 0, strategyCashTWD: 500_000 });
      const text = textOf(render(makeSummary([stock('0050', 500, 100)], '2025-06-17', 4_000_000)));
      expect(text).toContain('分割');
      expect(text).not.toContain(SHARE_LABEL);
    });

    it('純資訊：流動總資產與其他持股金額都不改變訊號、目標配置與建議股數', () => {
      brakeResult(makeSignal({ state: 'OFFENSE', prevState: 'DEFENSE' }));
      saveBrakeSettings({ profile: 'balanced', defenseE: 0, strategyCashTWD: 800_000 });
      const advice = (summary: PortfolioSummaryDto) =>
        textOf(render(summary))
          // 拿掉占比那一行，其餘文字必須逐字相同
          .replace(/此策略部位占總資產 [\d.]+%（[^）]*）/, '')
          .replace(/\s+/g, ' ');
      const base = [stock('0050', 2000, 100)];
      const withOthers = [...base, stock('2330', 3000, 900), stock('0056', 5000, 35)];

      const small = advice(makeSummary(base, '2026-10-09', 2_000_000));
      const large = advice(makeSummary(base, '2026-10-09', 50_000_000));
      const noDenominator = advice(makeSummary(base, '2026-10-09', 0));
      const others = advice(makeSummary(withOthers, '2026-10-09', 9_000_000));

      expect(small).toContain('建議買進');
      expect(small).toContain('目標配置：00631L 60% / 0050 20% / 現金 20%');
      expect(large).toBe(small);
      expect(noDenominator).toBe(small);
      expect(others).toBe(small);
    });

    it('進入防守時也顯示（占比與狀態無關）', () => {
      brakeResult(
        makeSignal({
          state: 'DEFENSE',
          prevState: 'OFFENSE',
          drawdownPct: -8.4,
          distanceToTriggerPct: -0.4,
        }),
      );
      saveBrakeSettings({ profile: 'balanced', defenseE: 0, strategyCashTWD: 200_000 });
      const text = textOf(
        render(
          makeSummary([stock('0050', 2000, 100), stock('00631L', 12_000, 50)], '2026-10-09', 5e6),
        ),
      );
      expect(text).toContain('觸發回落煞車，進入防守');
      expect(text).toContain('此策略部位占總資產 20.0%'); // V = 200k + 600k + 200k = 1,000,000
    });

    it('隱私模式：占比是比例 → 「隱藏金額」仍顯示（金額被遮），「只看損益」模式隱藏', () => {
      arrange();
      const summary = makeSummary(heldPositions(), '2026-10-09', 4_000_000);

      const full = textOf(renderWithPrivacy(summary, 'full'));
      expect(full).toContain('此策略部位占總資產 25.0%');
      expect(full).toContain('策略總資產 NT$1,000,000');

      const hidden = textOf(renderWithPrivacy(summary, 'hidden'));
      expect(hidden).toContain('此策略部位占總資產 25.0%');
      expect(hidden).not.toContain('NT$1,000,000');

      const changes = textOf(renderWithPrivacy(summary, 'changes'));
      expect(changes).not.toContain(SHARE_LABEL);
    });
  });

  describe('持股資料防護（PM R2）', () => {
    it('快照早於 0050 分割日 → 不顯示任何買賣股數，改顯示分割提示', () => {
      saveBrakeSettings({ profile: 'balanced', defenseE: 0, strategyCashTWD: 500_000 });
      const html = render(makeSummary([stock('0050', 500, 100)], '2025-06-17'));
      const text = textOf(html);
      expect(text).toContain(
        '你的持股快照建立於 0050 分割（2025-06-18 1 拆 4）之前，股數可能未調整，請先更新持股',
      );
      expect(text).not.toContain('建議買進');
      expect(text).not.toContain('建議賣出');
      expect(html).not.toContain('建議買賣股數');
      expect(text).toContain(DISCLAIMER_TEXT);
    });

    it('快照早於 00631L 分割日 → 擋下，提示 00631L', () => {
      const text = textOf(render(makeSummary([stock('00631L', 500, 50)], '2026-03-30')));
      expect(text).toContain('00631L 分割（2026-03-31 1 拆 22）');
    });

    it('快照晚於分割日 → 正常顯示建議', () => {
      brakeResult(makeSignal({ state: 'OFFENSE', prevState: 'DEFENSE' }));
      saveBrakeSettings({ profile: 'balanced', defenseE: 0, strategyCashTWD: 800_000 });
      const text = textOf(render(makeSummary([stock('0050', 2000, 100)], '2026-10-09')));
      expect(text).not.toContain('分割');
      expect(text).toContain('建議');
    });

    it('沒持有分割標的（只有策略現金）→ 即使快照很舊也不因分割而擋', () => {
      brakeResult(makeSignal({ state: 'OFFENSE', prevState: 'DEFENSE' }));
      saveBrakeSettings({ profile: 'balanced', defenseE: 0, strategyCashTWD: 1_000_000 });
      const text = textOf(render(makeSummary([], '2025-01-01')));
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

    it('策略現金並列顯示 TWD 現金部位合計「供參考」，不自動採用', () => {
      const cash: PositionDto = {
        ...stock('TWD', 1, 1),
        assetType: 'CASH',
        marketValueTWD: 300_000,
      };
      const text = textOf(render(makeSummary([cash])));
      expect(text).toContain('TWD 現金部位合計 NT$300,000（供參考，不會自動採用）');
      expect(text).toContain('請先在下方輸入策略現金'); // 策略現金仍是 0，沒有被自動採用
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
