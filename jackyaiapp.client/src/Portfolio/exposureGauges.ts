import { GAIN_COLOR, LOSS_COLOR, WARN_COLOR } from '@/utils/format';

/** Formulas surfaced on hover rather than as always-visible caption text: three gauges over the
 * same numerator differ only by denominator, and that distinction is the entire reason all three
 * exist — but spelling it out inline would bury the numbers under more explanation than figures. */
export const EXPOSURE_TOOLTIP = [
  '市場曝險 = Σ(市值 × 曝險係數)，正2 ×2、債券/現金/負債/不動產 0',
  '',
  '水位 = 市場曝險 ÷ 流動資產（不扣負債）',
  '　　　投入市場的程度，不管錢是哪來的',
  '',
  '槓桿 = 市場曝險 ÷ 流動資產淨值（已扣負債）',
  '　　　相對自有資金押多大，>1× 才算開槓桿',
  '',
  '抗跌空間 = 流動資產淨值 ÷ 市場曝險',
  '　　　大盤再跌多少，淨值歸零（槓桿的倒數）',
  '',
  '三者皆不含不動產：房子無法變現應付追繳。',
  '抗跌空間假設所有部位同幅漲跌，為保守估計。',
].join('\n');

/** Thresholds read as "a market fall of this size is how far you are from zero": under 10% is
 * within a single bad week, under 25% is within a normal correction. Deliberately coarse — the
 * underlying figure assumes every position moves together, so finer bands would imply a precision
 * it doesn't have. Zero and below means already insolvent, which shares the red band since there's
 * no meaningful gradation past "gone". */
export function drawdownBufferColor(bufferPct: number): string {
  if (bufferPct < 10) return LOSS_COLOR;
  if (bufferPct < 25) return WARN_COLOR;
  return GAIN_COLOR;
}
