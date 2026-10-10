import { Advice, BrakeState, Order, ProfileKey, Weights } from './drawdownBrake';

import { fmtNum } from '@/utils/format';

/**
 * 「回落煞車」卡片的文案（#431 §10）與風險揭露數字。
 *
 * 這些字串是合規相關的固定揭露，卡片所有狀態都要看得到、不可關閉；回測數字取自 #431 §2，更新時
 * 必須逐一對照 Issue 原文，不得憑記憶。PM 對「績效顯示」的提議（R3 同時顯示期間與最大回撤、R4
 * 第一階段用隔日成交版本）尚待 Boss 確認 — Boss 若改指示，只需改這個檔案的數字區塊。
 */

/** 固定警語（#431 §1 / DECISION-013）— 常駐可見、不可關閉。 */
export const DISCLAIMER = '依歷史回測規則產生的訊號，過去績效不代表未來，不構成投資建議。';

/** 固定附註（#431 §10）。 */
export const SIMULATED_NOTE = '2014 年以前的 00631L 績效為模擬值。';

/** 風險等級說明必含的三點（#431 §10）。 */
export const RISK_POINTS: string[] = [
  '槓桿版本在盤整年可能比 0050 虧更多（例如 2011 年平衡等級 -29%，0050 是 -18%）。',
  '00631L 曾經單日跌 20%，煞車是收盤才判斷，擋不住盤中或單日的急跌。',
  '平均每年約觸發 3 次進出，約 29% 的時間處於防守。',
];

/** 回測期間（#431 §2 表頭「回測年化 / 最大回撤（1997–2026）」）。 */
export const BACKTEST_PERIOD = '1997–2026';

/** 「隔日成交版本」（#431 §2）：第一階段只在收盤後提醒，等同隔日成交，所以用這一欄而不是收盤集合
 * 競價版本（保守 13.2% / -23%、平衡 17.4% / -32%、積極 20.8% / -39%）。 */
export const BACKTEST_NEXT_DAY: Record<ProfileKey, { annualPct: number; maxDrawdownPct: number }> =
  {
    conservative: { annualPct: 11.9, maxDrawdownPct: -27 },
    balanced: { annualPct: 15.4, maxDrawdownPct: -37 },
    aggressive: { annualPct: 18.1, maxDrawdownPct: -45 },
  };

export const STATE_LABEL: Record<BrakeState, string> = {
  OFFENSE: '進攻',
  DEFENSE: '防守',
};

/** 權重 → 「60%」。 */
function fmtWeightPct(w: number): string {
  return `${Math.round(w * 1000) / 10}%`;
}

/** 目標配置，例：「00631L 60% / 0050 20% / 現金 20%」；0% 的項目不列。 */
export function describeComposition(weights: Weights): string {
  const parts: string[] = [];
  if (weights['00631L'] > 1e-9) parts.push(`00631L ${fmtWeightPct(weights['00631L'])}`);
  if (weights['0050'] > 1e-9) parts.push(`0050 ${fmtWeightPct(weights['0050'])}`);
  if (weights.cash > 1e-9) parts.push(`現金 ${fmtWeightPct(weights.cash)}`);
  return parts.join(' / ');
}

/** 有效曝險倍數，最多兩位小數且去掉多餘的 0：1.38、1.4、0。 */
export function fmtMultiple(x: number): string {
  return String(Number(x.toFixed(2)));
}

/** 單筆，例：「賣出 0050 120 股」。 */
export function describeOrder(order: Order): string {
  return `${order.shares < 0 ? '賣出' : '買進'} ${order.symbol} ${fmtNum(Math.abs(order.shares))} 股`;
}

function describeSide(orders: Order[], verb: string): string {
  return `${verb} ${orders.map((o) => `${o.symbol} ${fmtNum(Math.abs(o.shares))} 股`).join('、')}`;
}

/** 先賣後買，例：「建議賣出 0050 120 股、00631L 850 股」「建議賣出 00631L 60 股；買進 0050 10 股」。 */
export function describeOrders(orders: Order[]): string {
  const sells = orders.filter((o) => o.shares < 0);
  const buys = orders.filter((o) => o.shares > 0);
  const parts: string[] = [];
  if (sells.length > 0) parts.push(describeSide(sells, '賣出'));
  if (buys.length > 0) parts.push(describeSide(buys, '買進'));
  return `建議${parts.join('；')}`;
}

export interface AdviceMessageInput {
  advice: Advice;
  state: BrakeState;
  /** 距近一年高點（%，例 -1.2）。 */
  drawdownPct: number;
  /** 距煞車觸發點（百分點，例 6.8；< 0 表示已低於觸發線）。 */
  distanceToTriggerPct: number;
  profileLabel: string;
}

/**
 * #431 §10 的四種文案：減碼（切換進防守）、加碼（切換回進攻）、不動、再平衡。
 *
 * 再平衡句型與範例略有不同：範例「00631L 漲多，有效曝險升到 1.53 倍」假設曝險是行情推動的，但
 * 切換日之後沒照做、曝險仍偏離目標時也會走到這條路，那時說「漲多／升到」是錯的 — 所以改為只陳述
 * 實際曝險與目標的差距。
 */
export function adviceMessage({
  advice,
  state,
  drawdownPct,
  distanceToTriggerPct,
  profileLabel,
}: AdviceMessageInput): string {
  const { action, switched, eActual, eTarget, weights, orders } = advice;

  if (action === '不動') {
    const head = `目前狀態：${STATE_LABEL[state]}（有效曝險 ${fmtMultiple(eActual)} 倍，目標 ${fmtMultiple(eTarget)} 倍）。距離近一年高點 ${drawdownPct.toFixed(1)}%`;
    if (state === 'DEFENSE') return `${head}；收盤站回 60 日均線才會解除煞車。`;
    return distanceToTriggerPct >= 0
      ? `${head}，距離煞車觸發點還有 ${distanceToTriggerPct.toFixed(1)}%。`
      : `${head}，已低於煞車觸發點 ${Math.abs(distanceToTriggerPct).toFixed(1)}%。`;
  }

  const ordersText = describeOrders(orders);
  const target = describeComposition(weights);

  if (switched && state === 'DEFENSE') {
    return `大盤報酬指數已從近一年高點回落 ${Math.abs(drawdownPct).toFixed(1)}%，觸發回落煞車，進入防守。依『${profileLabel}』等級，目標配置：${target}。${ordersText}。`;
  }
  if (switched) {
    return `大盤收盤站回 60 日均線，解除煞車，回到進攻。依『${profileLabel}』等級，目標配置：${target}（有效曝險 ${fmtMultiple(eTarget)} 倍）。${ordersText}。`;
  }
  const relation = eActual > eTarget ? '高於' : '低於';
  return `目前有效曝險 ${fmtMultiple(eActual)} 倍，${relation}目標 ${fmtMultiple(eTarget)} 倍，${ordersText}，讓曝險回到目標。`;
}
