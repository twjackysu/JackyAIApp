import { trendlineName } from '../../utils/trendlineChartOption';

import type { TrendlineSide } from '../../utils/trendlines';

export const TRENDLINE_METHOD =
  '線是程式依規則自動畫的，沒有人工挑日期：先用 ZigZag 濾掉小於 2 倍 ATR（平均真實波幅）的小波動，' +
  '只留下真正的波段高低點；再把每兩個高點（或低點）都試著連成一條線，挑「被波段點碰觸最多、收盤最少穿越、' +
  '離價格最近」的那條。每條線至少連 2 個點，畫到被連續兩日收盤穿越（或單日穿越 1 倍 ATR），' +
  '或價格離開超過 3 倍 ATR 為止。線只從選的起始日期之後的 K 棒找，所以換起始日期會畫出不同的線。' +
  '藍色是支撐、紫色是壓力，粗線是今天仍有效的線；滑過圖面可看當天每條線的價位。' +
  '線可以自己拖曳、刪除、改成支撐或壓力，或選好種類後新畫，改過的線存在這個瀏覽器。';

export const TRENDLINE_DISCLAIMER = '趨勢線僅供參考，不構成投資建議。';

/** What to do next: where to draw, how to move the selected line, or how to start. */
export function lineHint(drawing: TrendlineSide | null, hasSelection: boolean): string {
  if (drawing) {
    return `畫${trendlineName(drawing)}：在圖上點起點和終點（或按住拖曳），靠近 K 棒最高、最低點會自動對齊；按 Esc 取消。`;
  }
  return hasSelection
    ? '拖曳圓點調整端點、拖曳線段整條移動；鍵盤可用方向鍵微調、Delete 刪除。'
    : '點一下線就能拖曳或刪除，按「畫線」可以自己畫。';
}
