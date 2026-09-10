import { readString, writeString } from '@/utils/storage';

const STORAGE_KEY = 'portfolio.feeDiscountRate';

/** 2.8折 — a common TW e-brokerage electronic-order discount tier. */
export const DEFAULT_FEE_DISCOUNT_RATE = 0.28;

/** A per-browser preference, not account data — kept in localStorage rather than the backend
 * so saving it doesn't need a DB table just to remember one number. */
export function loadFeeDiscountRate(): number {
  const raw = readString(STORAGE_KEY);
  const parsed = raw != null ? parseFloat(raw) : NaN;
  return Number.isFinite(parsed) && parsed > 0 && parsed <= 1 ? parsed : DEFAULT_FEE_DISCOUNT_RATE;
}

export function saveFeeDiscountRate(rate: number): void {
  writeString(STORAGE_KEY, String(rate));
}
