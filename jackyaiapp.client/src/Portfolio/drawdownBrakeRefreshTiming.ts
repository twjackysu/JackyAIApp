/**
 * 「回落煞車」手動重新整理（TASK-043／需求 8）的純函式：資料時間格式、冷卻秒數。
 * 畫面與請求在 `DrawdownBrakeRefresh.tsx`，文案在 `drawdownBrakeCopy.ts`。
 */

/** 持股報價重新整理的客戶端冷卻（ARCHITECTURE.md：避免連按放大對報價來源的請求）。
 * 全站訊號的冷卻（60 秒）由後端負責，前端只顯示。 */
export const PRICE_REFRESH_COOLDOWN_SECONDS = 30;

/** `Throttled` 卻沒給可用秒數時的後備值＝後端全站冷卻上限（ARCHITECTURE.md：60 秒）。 */
export const THROTTLED_FALLBACK_SECONDS = 60;

/** 後端秒數的上限，避免異常值把按鈕鎖上很久。 */
const MAX_RETRY_AFTER_SECONDS = 300;

function pad2(n: number): string {
  return String(n).padStart(2, '0');
}

/**
 * `lastCheckedAt`（UTC ISO 8601）→ 使用者當地時間「MM/DD HH:mm」。
 * null、空字串、解析失敗、或 epoch 以前（後端沒有時間時不得顯示成 1970）→ null，由呼叫端省略。
 * 以當地時間的 getter 組字串，不依賴 Intl／ICU，輸出固定。
 */
export function formatLastChecked(iso: string | null | undefined): string | null {
  if (!iso) return null;
  const date = new Date(iso);
  const time = date.getTime();
  if (Number.isNaN(time) || time <= 0) return null;
  return `${pad2(date.getMonth() + 1)}/${pad2(date.getDate())} ${pad2(date.getHours())}:${pad2(date.getMinutes())}`;
}

/** 距離 `untilMs` 還有幾秒（無條件進位，最小 0）：倒數到 0 才解除，不會提早放行。 */
export function secondsLeft(untilMs: number, nowMs: number): number {
  return Math.max(0, Math.ceil((untilMs - nowMs) / 1000));
}

/** 後端的 `retryAfterSeconds` → 可用的正整數秒；null／非有限／≤ 0 用後備值，過大則截斷。 */
export function sanitizeRetryAfter(seconds: number | null | undefined): number {
  if (typeof seconds !== 'number' || !Number.isFinite(seconds) || seconds <= 0) {
    return THROTTLED_FALLBACK_SECONDS;
  }
  return Math.min(Math.ceil(seconds), MAX_RETRY_AFTER_SECONDS);
}
