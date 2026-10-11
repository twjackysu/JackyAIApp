import { Advice, BrakeState, Order, ProfileKey, StrategySymbol, Weights } from './drawdownBrake';

import { fmtNum } from '@/utils/format';

/**
 * 「回落煞車」卡片的文案（#431 §10）與風險揭露數字。
 *
 * 這些字串是合規相關的固定揭露，卡片所有狀態都要看得到、不可關閉。回測數字以 Boss 2026-10-11
 * 更正後的為準（#431 §2 原表誤用不保留現金的 k=0，已作廢）：逐項對照
 * `回測數據/tw_backtest/results/reproduce_spec.txt`（隔日成交＝`lag2` 欄）與 REQUIREMENTS.md
 * 「Boss 更正的回測數字」，更新時不得憑記憶。績效一律同時顯示期間與最大回撤（R3），且第一階段
 * 用隔日成交版本（R4）— 兩者 Boss 皆已同意。
 */

/** 固定警語（#431 §1 / DECISION-013）— 常駐可見、不可關閉。 */
export const DISCLAIMER = '依歷史回測規則產生的訊號，過去績效不代表未來，不構成投資建議。';

/** 資料限制註記（PM 假設 A7，Boss 2026-10-11：0050 與 00631L 皆為報酬指數模擬、早年股利為估計）。
 * 範圍比 #431 §10 原附註（只寫 00631L）廣；Boss 若要改字只需改這一行。 */
export const SIMULATED_NOTE =
  '2014 年以前的 0050 與 00631L 績效為依報酬指數模擬的數字；1997–2002 年的股利為估計值。';

/** 風險等級說明必含的三點（#431 §10）。 */
export const RISK_POINTS: string[] = [
  '槓桿版本在盤整年可能比 0050 虧更多（例如 2011 年平衡等級 -28%，0050 是 -18%）。',
  '00631L 曾經單日跌 20%，煞車是收盤才判斷，擋不住盤中或單日的急跌。',
  '平均每年約觸發 3 次進出，約 29% 的時間處於防守。',
];

/** 回測期間（#431 §2 表頭「回測年化 / 最大回撤（1997–2026）」）。 */
export const BACKTEST_PERIOD = '1997–2026';

/** 「隔日成交版本」（`reproduce_spec.txt` 的 `lag2` 欄）：第一階段只在收盤後提醒，等同隔日成交，
 * 所以用這一欄而不是收盤集合競價版本（保守 13.5% / -24%、平衡 17.2% / -32%、積極 20.9% / -40%；
 * 先前的 13.2/-23、17.4/-32、20.8/-39 是 k=0 的結果，已作廢）。 */
export const BACKTEST_NEXT_DAY: Record<ProfileKey, { annualPct: number; maxDrawdownPct: number }> =
  {
    conservative: { annualPct: 12.1, maxDrawdownPct: -27 },
    balanced: { annualPct: 15.3, maxDrawdownPct: -37 },
    aggressive: { annualPct: 18.4, maxDrawdownPct: -45 },
  };

export const STATE_LABEL: Record<BrakeState, string> = {
  OFFENSE: '進攻',
  DEFENSE: '防守',
};

/** 權重 → 「60%」。 */
function fmtWeightPct(w: number): string {
  return `${Math.round(w * 1000) / 10}%`;
}

/** 台股正 2 類（00631L＋00685L）在畫面上的名稱。目標只有類別層級，不逐檔列。 */
export const LEV2_LABEL = '台股正 2';

/** 台股正 2 類一列的標題，附上成員。 */
export const LEV2_ROW_TITLE = `${LEV2_LABEL}（00631L／00685L）`;

/** 目標配置，例：「台股正 2 60% / 0050 20% / 現金 20%」；0% 的項目不列。有其他持股時最後補上它們
 * 占策略範圍資產的比例，讓各項加起來是 100%（核心的權重是占「策略範圍資產」，不是占核心）。 */
export function describeComposition(weights: Weights, othersShare = 0): string {
  const parts: string[] = [];
  if (weights.lev2 > 1e-9) parts.push(`${LEV2_LABEL} ${fmtWeightPct(weights.lev2)}`);
  if (weights['0050'] > 1e-9) parts.push(`0050 ${fmtWeightPct(weights['0050'])}`);
  if (weights.cash > 1e-9) parts.push(`現金 ${fmtWeightPct(weights.cash)}`);
  if (othersShare > 1e-9) parts.push(`其他持股 ${fmtWeightPct(othersShare)}`);
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
  const { action, switched, eActual, eTarget, eReach, weights, othersShare, orders } = advice;

  if (action === '不動') {
    const head = `目前狀態：${STATE_LABEL[state]}（有效曝險 ${fmtMultiple(eActual)} 倍，目標 ${fmtMultiple(eTarget)} 倍）。距離近一年高點 ${drawdownPct.toFixed(1)}%`;
    if (state === 'DEFENSE') return `${head}；收盤站回 60 日均線才會解除煞車。`;
    return distanceToTriggerPct >= 0
      ? `${head}，距離煞車觸發點還有 ${distanceToTriggerPct.toFixed(1)}%。`
      : `${head}，已低於煞車觸發點 ${Math.abs(distanceToTriggerPct).toFixed(1)}%。`;
  }

  const ordersText = describeOrders(orders);
  const target = describeComposition(weights, othersShare);

  if (switched && state === 'DEFENSE') {
    return `大盤報酬指數已從近一年高點回落 ${Math.abs(drawdownPct).toFixed(1)}%，觸發回落煞車，進入防守。依『${profileLabel}』等級，目標配置：${target}。${ordersText}。`;
  }
  if (switched) {
    return `大盤收盤站回 60 日均線，解除煞車，回到進攻。依『${profileLabel}』等級，目標配置：${target}（有效曝險 ${fmtMultiple(eReach)} 倍）。${ordersText}。`;
  }
  // 目標不可達（capped／aboveTarget）時，「回到目標」要拿可達的曝險說，否則會承諾做不到的事。
  const reachable = advice.capped || advice.aboveTarget;
  const goal = reachable ? eReach : eTarget;
  const relation = eActual > goal ? '高於' : '低於';
  return `目前有效曝險 ${fmtMultiple(eActual)} 倍，${relation}${reachable ? '可達曝險' : '目標'} ${fmtMultiple(goal)} 倍，${ordersText}，讓曝險回到${reachable ? '可達的水準' : '目標'}。`;
}

// ---- 需求 12：整個流動資產納入曝險判斷、分桶顯示 ----
// 以下字句為 Tech Lead 提議（PM／Boss 可改），集中在這裡，要改字只改一處。

/** 其他持股一列的建議欄位（DECISION-020、A26）：進攻、防守、再平衡一律如此。 */
export const OTHERS_ADVICE = '不動（由您自行決定）';

/** 其他持股的提醒（R8）：未經回測，所以不替它們訂目標，也不給建議。 */
export const OTHERS_REMINDER = '其他持股未經回測，僅計入整體曝險、不給建議，由您自行決定。';

/** 績效區塊的範圍標示（R8）：回測績效不是整個流動資產的績效；00685L 沒有單獨回測（PM v4.3 用語，
 * PM 可再改字）。 */
export const BACKTEST_SCOPE_NOTE =
  '以上績效依 0050＋00631L＋現金回測；00685L 視同 00631L（您的判斷），未單獨回測，也不是整個流動資產的績效。';

/** 現金類包含外幣與債券／貨幣型，金額變動不等於「換成台幣現金」（架構決策第 2 點）。 */
export const CASH_BUCKET_NOTE =
  '現金類包含外幣現金與債券／貨幣型商品；這裡只標示金額變動，不對它們給買賣建議。';

/** 沒有其他持股時，目標不可達沿用原文（#431 §3 / §10）。 */
export const CAPPED_NO_OTHERS = '現金比例過高，無法達到此曝險';

/** 其他持股單獨就已高於目標（12c）。`eReach` 即核心降到最低後的整體曝險。 */
export function aboveTargetNotice(eReach: number, eTarget: number): string {
  return `即使 0050 與${LEV2_LABEL} 全部轉為現金，整體曝險仍有 ${fmtMultiple(eReach)} 倍，高於目標 ${fmtMultiple(eTarget)} 倍；差額來自您自行決定的其他持股`;
}

/** 目標在其他持股與最低現金限制下無法達到（12c）。 */
export function cappedNotice(eReach: number, eTarget: number, hasOthers: boolean): string {
  if (!hasOthers) return CAPPED_NO_OTHERS;
  return `受其他持股與最低現金限制，整體曝險最高只能到 ${fmtMultiple(eReach)} 倍（目標 ${fmtMultiple(eTarget)} 倍）`;
}

/** 範圍內有持股價格取得失敗 → 不給建議（A 與 E 會被失敗的 0 值扭曲）。 */
export function priceFailedMessage(count: number): string {
  return `${count} 檔持股價格取得失敗，暫時無法算出建議`;
}

/** 核心標的（0050、台股正 2 類的 00631L／00685L）的曝險倍數與策略定義不符（名稱分類意外）→ 不給建議。 */
export function coreFactorAnomalyMessage(symbol: StrategySymbol, expected: number): string {
  return `${symbol} 的曝險倍數與策略定義（${expected} 倍）不符，暫時無法算出建議，請回報給作者`;
}

/** 沒有任何可納入策略範圍的流動資產。 */
export const NO_SCOPE_ASSETS =
  '目前沒有可納入的流動資產，請先在持股中新增資產，才能算出目標配置與建議。';

export interface ReconciliationInput {
  /** 頁面的流動總資產（TWD），已套用隱私遮罩的字串。 */
  liquidGross: string;
  /** 頁面右上角「水位」（%，例 109.1）；沒有或隱私模式隱藏時為 null。 */
  waterPct: number | null;
  /** 被排除的標的代號，例 ['NTSD']。 */
  excludedSymbols: string[];
  excludedValue: string;
  scopeAssets: string;
  /** 卡片的整體曝險 E（倍）。 */
  eActual: number;
}

/** 對帳行（12a／A24）：讓使用者看出卡片的 E 為何和右上角水位不同。只有存在被排除持股才顯示；
 * `STRATEGY_EXCLUDED` 目前是空表（DECISION-021，NTSD 已納入），所以正常情況不會出現。 */
export function describeReconciliation(input: ReconciliationInput): string {
  const water = input.waterPct == null ? '' : `（水位 ${input.waterPct.toFixed(1)}%）`;
  return `流動總資產 ${input.liquidGross}${water}；不含 ${input.excludedSymbols.join('、')} ${input.excludedValue}；策略範圍 ${input.scopeAssets} → 曝險 ${fmtMultiple(input.eActual)} 倍`;
}

/** 核心股票（0050、00631L、00685L）的建議欄：「加碼：買進 116,667 股（約 NT$3,500,010）」「不動」。 */
export function describeCoreRowAdvice(
  order: Order | undefined,
  price: number,
  fmtAmount: (n: number) => string,
): string {
  if (!order) return '不動';
  const amount = Math.abs(order.shares) * price;
  const verb = order.shares < 0 ? '減碼：賣出' : '加碼：買進';
  return `${verb} ${fmtNum(Math.abs(order.shares))} 股（約 NT$${fmtAmount(amount)}）`;
}

/** 台股正 2 類一列的建議欄：類別層級的加碼／減碼／不動與金額，各檔股數另列在其下。
 * 金額 ＝ Σ(實際建議股數 × 價格)，與下方各檔的「約 NT$」合計一致。 */
export function describeLev2RowAdvice(change: number, fmtAmount: (n: number) => string): string {
  const rounded = Math.round(change);
  if (rounded === 0) return '不動';
  return `${rounded > 0 ? '加碼：買進' : '減碼：賣出'}約 NT$${fmtAmount(Math.abs(rounded))}`;
}

/** 現金類列的建議欄：金額變動 ＝ −Σ(實際建議股數 × 價格)。 */
export function describeCashRowAdvice(
  cashChange: number,
  fmtAmount: (n: number) => string,
): string {
  const rounded = Math.round(cashChange);
  if (rounded === 0) return '不動';
  return `${rounded > 0 ? '現金增加' : '現金減少'} NT$${fmtAmount(Math.abs(rounded))}`;
}

// ---- 資料時間與「重新整理資料」（TASK-043／需求 8） ----

export const REFRESH_BUTTON_LABEL = '重新整理資料';
export const REFRESH_BUTTON_BUSY_LABEL = '更新中…';

/** 資料時間的兩段，各自是不可拆開的一個單位（窄螢幕放不下時整段換行，不從字中間切斷）。
 * 訊號資料日期必顯示；伺服器最近檢查（已格式化的當地時間）沒有就整句省略，不補 1970。 */
export function signalDateLabel(asOfDate: string | null): string {
  return `訊號資料日期：${asOfDate ?? '—'}`;
}

export function lastCheckedLabel(formatted: string): string {
  return `伺服器最近檢查：${formatted}`;
}

/** 重新整理成功（HTTP 200）的四種結果。`Throttled` 的字句另見 {@link throttledMessage}（秒數會倒數）。 */
export function refreshResultMessage(
  result: 'Updated' | 'NoNewData' | 'SourceUnavailable',
  asOfDate: string | null,
): string {
  switch (result) {
    case 'Updated':
      return `已更新到 ${asOfDate ?? '—'}`;
    case 'NoNewData':
      return '目前沒有更新的資料';
    case 'SourceUnavailable':
      return '資料未更新：證交所暫時取不到，稍後再試';
  }
}

/** 全站冷卻中：有人剛更新過。秒數隨倒數變動。 */
export function throttledMessage(seconds: number): string {
  return `剛剛已有人更新過，請 ${seconds} 秒後再試`;
}

/** 按鈕停用的原因要寫在畫面上（停用的按鈕沒有 hover／點擊，不能只靠 tooltip）。 */
export function cooldownHint(seconds: number): string {
  return `${seconds} 秒後可再次重新整理`;
}

/** 請求本身失敗（網路、功能未開啟、伺服器錯誤）；原本的訊號資料照舊顯示。 */
export function refreshFailedMessage(detail: string): string {
  return `重新整理失敗：${detail}`;
}

/** 持股報價的重新取得失敗（不影響訊號資料的更新結果）。 */
export const PRICE_REFRESH_FAILED = '持股報價重新取得失敗，仍顯示原本的價格。';
