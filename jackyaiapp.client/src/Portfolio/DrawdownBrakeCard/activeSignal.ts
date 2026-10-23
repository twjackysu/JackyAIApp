import { BrakeState, DD_TRIGGER_PCT } from '../drawdownBrake';

import { DrawdownBrakeDto } from '@/apis/portfolioApis/types';

/** 建議依據的訊號：盤中預估或收盤後的正式訊號。 */
export interface ActiveSignal {
  state: BrakeState;
  prevState: BrakeState | null;
  drawdownPct: number;
  distanceToTriggerPct: number;
  isEstimate: boolean;
}

/** 有預估時以預估狀態計算；沒有（第一階段恆為如此）就用收盤後的正式訊號。資料準備中則為 null。 */
export function pickActiveSignal(signal: DrawdownBrakeDto): ActiveSignal | null {
  if (signal.isInitializing) return null;
  const { estimate } = signal;
  if (estimate) {
    return {
      state: estimate.state,
      prevState: estimate.prevState,
      drawdownPct: estimate.drawdownPct,
      // 預估沒有單獨的 distance 欄位：drawdownPct 距 -8% 觸發線的空間。
      distanceToTriggerPct: estimate.drawdownPct + DD_TRIGGER_PCT,
      isEstimate: true,
    };
  }
  if (signal.state == null || signal.drawdownPct == null || signal.distanceToTriggerPct == null) {
    return null;
  }
  return {
    state: signal.state,
    prevState: signal.prevState,
    drawdownPct: signal.drawdownPct,
    distanceToTriggerPct: signal.distanceToTriggerPct,
    isEstimate: false,
  };
}
