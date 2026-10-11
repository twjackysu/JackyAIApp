import { describe, expect, it } from 'vitest';

import { Advice } from './drawdownBrake';
import {
  adviceMessage,
  BACKTEST_NEXT_DAY,
  BACKTEST_PERIOD,
  describeComposition,
  describeOrders,
  DISCLAIMER,
  fmtMultiple,
  RISK_POINTS,
  SIMULATED_NOTE,
} from './drawdownBrakeCopy';

function makeAdvice(overrides: Partial<Advice>): Advice {
  return {
    action: '不動',
    switched: false,
    totalValue: 1_000_000,
    eActual: 1.4,
    eTarget: 1.4,
    weights: { '0050': 0.2, '00631L': 0.6, cash: 0.2 },
    truncated: false,
    orders: [],
    ...overrides,
  };
}

describe('固定揭露文字（警語逐字對照 #431；數字對照 Boss 更正值）', () => {
  it('警語逐字不變（DECISION-013）', () => {
    expect(DISCLAIMER).toBe('依歷史回測規則產生的訊號，過去績效不代表未來，不構成投資建議。');
  });

  it('資料限制註記涵蓋 2014 年前 0050 與 00631L 的模擬值、1997–2002 年股利估計值（A7）', () => {
    expect(SIMULATED_NOTE).toBe(
      '2014 年以前的 0050 與 00631L 績效為依報酬指數模擬的數字；1997–2002 年的股利為估計值。',
    );
  });

  it('風險等級說明的三點（#431 §10）', () => {
    expect(RISK_POINTS).toEqual([
      '槓桿版本在盤整年可能比 0050 虧更多（例如 2011 年平衡等級 -28%，0050 是 -18%）。',
      '00631L 曾經單日跌 20%，煞車是收盤才判斷，擋不住盤中或單日的急跌。',
      '平均每年約觸發 3 次進出，約 29% 的時間處於防守。',
    ]);
  });

  it('回測績效用隔日成交版本（reproduce_spec.txt 的 lag2 欄），期間 1997–2026', () => {
    expect(BACKTEST_PERIOD).toBe('1997–2026');
    expect(BACKTEST_NEXT_DAY).toEqual({
      conservative: { annualPct: 12.1, maxDrawdownPct: -27 },
      balanced: { annualPct: 15.3, maxDrawdownPct: -37 },
      aggressive: { annualPct: 18.4, maxDrawdownPct: -45 },
    });
  });
});

describe('格式化', () => {
  it('目標配置：非 0 項目、00631L 在前', () => {
    expect(describeComposition({ '0050': 0.2, '00631L': 0.6, cash: 0.2 })).toBe(
      '00631L 60% / 0050 20% / 現金 20%',
    );
    expect(describeComposition({ '0050': 0, '00631L': 0, cash: 1 })).toBe('現金 100%');
    expect(describeComposition({ '0050': 0.3, '00631L': 0, cash: 0.7 })).toBe(
      '0050 30% / 現金 70%',
    );
    // 浮點誤差不會漏成 20.000000000000018%
    expect(describeComposition({ '0050': 0.20000000000000018, '00631L': 0.6, cash: 0.2 })).toBe(
      '00631L 60% / 0050 20% / 現金 20%',
    );
  });

  it('有效曝險倍數：最多兩位小數、去掉多餘的 0', () => {
    expect(fmtMultiple(1.38)).toBe('1.38');
    expect(fmtMultiple(1.4)).toBe('1.4');
    expect(fmtMultiple(0)).toBe('0');
    expect(fmtMultiple(1.5300000000000002)).toBe('1.53');
  });

  it('買賣股數：先賣後買，千分位', () => {
    expect(
      describeOrders([
        { symbol: '00631L', shares: -1_250 },
        { symbol: '0050', shares: 10 },
      ]),
    ).toBe('建議賣出 00631L 1,250 股；買進 0050 10 股');
    expect(describeOrders([{ symbol: '0050', shares: 5 }])).toBe('建議買進 0050 5 股');
  });
});

describe('adviceMessage（#431 §10 四種文案）', () => {
  it('減碼：進入防守', () => {
    const text = adviceMessage({
      advice: makeAdvice({
        action: '減碼',
        switched: true,
        eActual: 1.4,
        eTarget: 0,
        weights: { '0050': 0, '00631L': 0, cash: 1 },
        orders: [
          { symbol: '0050', shares: -120 },
          { symbol: '00631L', shares: -850 },
        ],
      }),
      state: 'DEFENSE',
      drawdownPct: -8.4,
      distanceToTriggerPct: -0.4,
      profileLabel: '平衡',
    });
    expect(text).toBe(
      '大盤報酬指數已從近一年高點回落 8.4%，觸發回落煞車，進入防守。依『平衡』等級，目標配置：現金 100%。建議賣出 0050 120 股、00631L 850 股。',
    );
  });

  it('加碼：回到進攻', () => {
    const text = adviceMessage({
      advice: makeAdvice({
        action: '加碼',
        switched: true,
        eActual: 0,
        orders: [
          { symbol: '00631L', shares: 840 },
          { symbol: '0050', shares: 115 },
        ],
      }),
      state: 'OFFENSE',
      drawdownPct: -5,
      distanceToTriggerPct: 3,
      profileLabel: '平衡',
    });
    expect(text).toBe(
      '大盤收盤站回 60 日均線，解除煞車，回到進攻。依『平衡』等級，目標配置：00631L 60% / 0050 20% / 現金 20%（有效曝險 1.4 倍）。建議買進 00631L 840 股、0050 115 股。',
    );
  });

  it('不動（進攻）', () => {
    const text = adviceMessage({
      advice: makeAdvice({ eActual: 1.38 }),
      state: 'OFFENSE',
      drawdownPct: -1.2,
      distanceToTriggerPct: 6.8,
      profileLabel: '平衡',
    });
    expect(text).toBe(
      '目前狀態：進攻（有效曝險 1.38 倍，目標 1.4 倍）。距離近一年高點 -1.2%，距離煞車觸發點還有 6.8%。',
    );
  });

  it('不動（防守）：不談「距離觸發點」，改說明解除條件', () => {
    const text = adviceMessage({
      advice: makeAdvice({ eActual: 0, eTarget: 0, weights: { '0050': 0, '00631L': 0, cash: 1 } }),
      state: 'DEFENSE',
      drawdownPct: -12.3,
      distanceToTriggerPct: -4.3,
      profileLabel: '平衡',
    });
    expect(text).toBe(
      '目前狀態：防守（有效曝險 0 倍，目標 0 倍）。距離近一年高點 -12.3%；收盤站回 60 日均線才會解除煞車。',
    );
  });

  it('再平衡：曝險高於目標 → 賣出；低於目標 → 買進', () => {
    const high = adviceMessage({
      advice: makeAdvice({
        action: '減碼',
        eActual: 1.53,
        orders: [{ symbol: '00631L', shares: -60 }],
      }),
      state: 'OFFENSE',
      drawdownPct: -1,
      distanceToTriggerPct: 7,
      profileLabel: '平衡',
    });
    expect(high).toBe(
      '目前有效曝險 1.53 倍，高於目標 1.4 倍，建議賣出 00631L 60 股，讓曝險回到目標。',
    );

    const low = adviceMessage({
      advice: makeAdvice({
        action: '加碼',
        eActual: 1.2,
        orders: [{ symbol: '00631L', shares: 70 }],
      }),
      state: 'OFFENSE',
      drawdownPct: -1,
      distanceToTriggerPct: 7,
      profileLabel: '平衡',
    });
    expect(low).toBe(
      '目前有效曝險 1.2 倍，低於目標 1.4 倍，建議買進 00631L 70 股，讓曝險回到目標。',
    );
  });
});
