import { describe, expect, it } from 'vitest';

import { Advice } from './drawdownBrake';
import {
  aboveTargetNotice,
  adviceMessage,
  BACKTEST_NEXT_DAY,
  BACKTEST_PERIOD,
  BACKTEST_SCOPE_NOTE,
  cappedNotice,
  CAPPED_NO_OTHERS,
  cooldownHint,
  coreFactorAnomalyMessage,
  describeCashRowAdvice,
  describeComposition,
  describeCoreRowAdvice,
  describeLev2RowAdvice,
  describeOrders,
  describeReconciliation,
  DISCLAIMER,
  fmtMultiple,
  lastCheckedLabel,
  LEV2_LABEL,
  LEV2_ROW_TITLE,
  OTHERS_ADVICE,
  OTHERS_REMINDER,
  priceFailedMessage,
  refreshFailedMessage,
  refreshResultMessage,
  RISK_POINTS,
  signalDateLabel,
  SIMULATED_NOTE,
  throttledMessage,
} from './drawdownBrakeCopy';

function makeAdvice(overrides: Partial<Advice>): Advice {
  return {
    action: '不動',
    switched: false,
    scopeAssets: 1_000_000,
    eActual: 1.4,
    eTarget: 1.4,
    eReach: 1.4,
    aboveTarget: false,
    capped: false,
    targets: { '0050': 200_000, lev2: 600_000, cash: 200_000 },
    weights: { '0050': 0.2, lev2: 0.6, cash: 0.2 },
    othersShare: 0,
    orders: [],
    lev2Change: 0,
    cashChange: 0,
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
  it('目標配置：非 0 項目、台股正 2 在前；只列類別，不逐檔列目標（DECISION-021）', () => {
    expect(LEV2_LABEL).toBe('台股正 2');
    expect(describeComposition({ '0050': 0.2, lev2: 0.6, cash: 0.2 })).toBe(
      '台股正 2 60% / 0050 20% / 現金 20%',
    );
    expect(describeComposition({ '0050': 0, lev2: 0, cash: 1 })).toBe('現金 100%');
    expect(describeComposition({ '0050': 0.3, lev2: 0, cash: 0.7 })).toBe('0050 30% / 現金 70%');
    // 浮點誤差不會漏成 20.000000000000018%
    expect(describeComposition({ '0050': 0.20000000000000018, lev2: 0.6, cash: 0.2 })).toBe(
      '台股正 2 60% / 0050 20% / 現金 20%',
    );
    expect(describeComposition({ '0050': 0.2, lev2: 0.6, cash: 0.2 })).not.toMatch(/00631L|00685L/);
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
        weights: { '0050': 0, lev2: 0, cash: 1 },
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
      '大盤收盤站回 60 日均線，解除煞車，回到進攻。依『平衡』等級，目標配置：台股正 2 60% / 0050 20% / 現金 20%（有效曝險 1.4 倍）。建議買進 00631L 840 股、0050 115 股。',
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
      advice: makeAdvice({ eActual: 0, eTarget: 0, weights: { '0050': 0, lev2: 0, cash: 1 } }),
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

describe('需求 12 文案', () => {
  const fmt = (n: number) => n.toLocaleString('zh-TW');

  it('其他持股一列的建議欄、提醒與績效範圍標示逐字固定（R8、A26）', () => {
    expect(OTHERS_ADVICE).toBe('不動（由您自行決定）');
    expect(OTHERS_REMINDER).toBe('其他持股未經回測，僅計入整體曝險、不給建議，由您自行決定。');
    expect(BACKTEST_SCOPE_NOTE).toBe(
      '以上績效依 0050＋00631L＋現金回測；00685L 視同 00631L（您的判斷），未單獨回測，也不是整個流動資產的績效。',
    );
  });

  it('aboveTarget 標示（12c）：差額來自其他持股', () => {
    expect(aboveTargetNotice(0.4, 0)).toBe(
      '即使 0050 與台股正 2 全部轉為現金，整體曝險仍有 0.4 倍，高於目標 0 倍；差額來自您自行決定的其他持股',
    );
  });

  it('capped 標示（12c）：沒有其他持股沿用原文，有其他持股用新文案', () => {
    expect(cappedNotice(1.6, 1.8, false)).toBe('現金比例過高，無法達到此曝險');
    expect(CAPPED_NO_OTHERS).toBe('現金比例過高，無法達到此曝險');
    expect(cappedNotice(0.9, 1.4, true)).toBe(
      '受其他持股與最低現金限制，整體曝險最高只能到 0.9 倍（目標 1.4 倍）',
    );
  });

  it('資料防護文案', () => {
    expect(priceFailedMessage(2)).toBe('2 檔持股價格取得失敗，暫時無法算出建議');
    expect(coreFactorAnomalyMessage('0050', 1)).toContain('0050');
    expect(coreFactorAnomalyMessage('00631L', 2)).toContain('2 倍');
    expect(coreFactorAnomalyMessage('00685L', 2)).toBe(
      '00685L 的曝險倍數與策略定義（2 倍）不符，暫時無法算出建議，請回報給作者',
    );
  });

  it('對帳行（12a／A24，機制保留）：流動總資產、水位、不含 X、策略範圍、曝險', () => {
    expect(
      describeReconciliation({
        liquidGross: 'NT$11,000,000',
        waterPct: 109.1,
        excludedSymbols: ['NTSD'],
        excludedValue: 'NT$1,000,000',
        scopeAssets: 'NT$10,000,000',
        eActual: 1.05,
      }),
    ).toBe(
      '流動總資產 NT$11,000,000（水位 109.1%）；不含 NTSD NT$1,000,000；策略範圍 NT$10,000,000 → 曝險 1.05 倍',
    );
  });

  it('對帳行：水位不顯示（隱私模式）時不留空括號；多個被排除標的以頓號分隔', () => {
    const text = describeReconciliation({
      liquidGross: 'NT$***',
      waterPct: null,
      excludedSymbols: ['NTSD', 'XYZ'],
      excludedValue: 'NT$***',
      scopeAssets: 'NT$***',
      eActual: 1,
    });
    expect(text).toBe('流動總資產 NT$***；不含 NTSD、XYZ NT$***；策略範圍 NT$*** → 曝險 1 倍');
    expect(text).not.toContain('（）');
  });

  it('台股正 2 類一列：標題附成員；類別層級的加碼／減碼／不動與金額', () => {
    expect(LEV2_ROW_TITLE).toBe('台股正 2（00631L／00685L）');
    expect(describeLev2RowAdvice(1_500_000, fmt)).toBe('加碼：買進約 NT$1,500,000');
    expect(describeLev2RowAdvice(-1_100_000, fmt)).toBe('減碼：賣出約 NT$1,100,000');
    expect(describeLev2RowAdvice(0, fmt)).toBe('不動');
    expect(describeLev2RowAdvice(0.4, fmt)).toBe('不動'); // 四捨五入為 0 不出現「約 NT$0」
  });

  it('核心股票列：加碼／減碼／不動，含整數股數與金額', () => {
    expect(describeCoreRowAdvice({ symbol: '0050', shares: -58_333 }, 60, fmt)).toBe(
      '減碼：賣出 58,333 股（約 NT$3,499,980）',
    );
    expect(describeCoreRowAdvice({ symbol: '00631L', shares: 116_667 }, 30, fmt)).toBe(
      '加碼：買進 116,667 股（約 NT$3,500,010）',
    );
    expect(describeCoreRowAdvice(undefined, 60, fmt)).toBe('不動');
  });

  it('現金類列：金額變動 ＝ −Σ(實際建議股數 × 價格)；0 → 不動', () => {
    expect(describeCashRowAdvice(-30, fmt)).toBe('現金減少 NT$30');
    expect(describeCashRowAdvice(1_500_000, fmt)).toBe('現金增加 NT$1,500,000');
    expect(describeCashRowAdvice(0, fmt)).toBe('不動');
    expect(describeCashRowAdvice(0.3, fmt)).toBe('不動'); // 四捨五入為 0 不出現「增加 NT$0」
  });

  it('目標配置：有其他持股時補上它們的占比，各項加起來是 100%', () => {
    expect(describeComposition({ '0050': 0.05, lev2: 0.6, cash: 0.2 }, 0.15)).toBe(
      '台股正 2 60% / 0050 5% / 現金 20% / 其他持股 15%',
    );
    expect(describeComposition({ '0050': 0.2, lev2: 0.6, cash: 0.2 }, 0)).toBe(
      '台股正 2 60% / 0050 20% / 現金 20%',
    );
  });

  it('再平衡句型：目標不可達時拿可達曝險說，不承諾做不到的事', () => {
    const text = adviceMessage({
      advice: {
        action: '加碼',
        switched: false,
        scopeAssets: 10_000_000,
        eActual: 0.7,
        eTarget: 1.4,
        eReach: 0.9,
        aboveTarget: false,
        capped: true,
        targets: { '0050': 0, lev2: 1_000_000, cash: 2_000_000 },
        weights: { '0050': 0, lev2: 0.1, cash: 0.2 },
        othersShare: 0.7,
        orders: [{ symbol: '00631L', shares: 33_333 }],
        lev2Change: 999_990,
        cashChange: -999_990,
      },
      state: 'OFFENSE',
      drawdownPct: -1,
      distanceToTriggerPct: 7,
      profileLabel: '平衡',
    });
    expect(text).toBe(
      '目前有效曝險 0.7 倍，低於可達曝險 0.9 倍，建議買進 00631L 33,333 股，讓曝險回到可達的水準。',
    );
  });
});

describe('資料時間與重新整理文案（TASK-043）', () => {
  it('資料時間兩段的字句；asOfDate 缺漏顯示「—」，不出現 null／undefined', () => {
    expect(signalDateLabel('2026-10-08')).toBe('訊號資料日期：2026-10-08');
    expect(signalDateLabel(null)).toBe('訊號資料日期：—');
    expect(lastCheckedLabel('10/09 15:05')).toBe('伺服器最近檢查：10/09 15:05');
  });

  it('四種結果的字句與任務書一致', () => {
    expect(refreshResultMessage('Updated', '2026-10-09')).toBe('已更新到 2026-10-09');
    expect(refreshResultMessage('NoNewData', '2026-10-09')).toBe('目前沒有更新的資料');
    expect(refreshResultMessage('SourceUnavailable', '2026-10-09')).toBe(
      '資料未更新：證交所暫時取不到，稍後再試',
    );
    expect(throttledMessage(45)).toBe('剛剛已有人更新過，請 45 秒後再試');
  });

  it('只有 Updated 的字句會說「已更新」；asOfDate 缺漏時不產生 null／undefined 字樣', () => {
    expect(refreshResultMessage('Updated', null)).toBe('已更新到 —');
    for (const result of ['NoNewData', 'SourceUnavailable'] as const) {
      expect(refreshResultMessage(result, '2026-10-09')).not.toContain('已更新');
    }
  });

  it('冷卻原因與失敗字句', () => {
    expect(cooldownHint(12)).toBe('12 秒後可再次重新整理');
    expect(refreshFailedMessage('Cannot reach the server.')).toBe(
      '重新整理失敗：Cannot reach the server.',
    );
  });
});
