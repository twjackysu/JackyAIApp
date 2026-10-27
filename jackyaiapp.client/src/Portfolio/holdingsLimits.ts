/**
 * The server's limits on one save of holdings (`HoldingsLimits.cs`, D-165), checked here too so the
 * editor and the CSV import can say which row is past them instead of the save failing as a whole.
 * Its test holds the numbers to the server's.
 */
export const MAX_POSITIONS = 300;
export const MAX_SYMBOL_LENGTH = 40;
export const MAX_NAME_LENGTH = 200;
/** Market, currency and valuation method. */
export const MAX_CODE_LENGTH = 20;

interface LimitedFields {
  symbol?: string | null;
  name?: string | null;
  market?: string | null;
  currency?: string | null;
  costCurrency?: string | null;
  valuationMethod?: string | null;
}

const tooLong = (value: string | null | undefined, max: number) =>
  value != null && value.length > max;

/** Why a row's text is past the server's limits, or null. Same order and wording as the server's. */
export function lengthProblem(r: LimitedFields): string | null {
  if (tooLong(r.symbol, MAX_SYMBOL_LENGTH)) return `代號最多 ${MAX_SYMBOL_LENGTH} 個字`;
  if (tooLong(r.name, MAX_NAME_LENGTH)) return `名稱最多 ${MAX_NAME_LENGTH} 個字`;
  if (
    tooLong(r.market, MAX_CODE_LENGTH) ||
    tooLong(r.currency, MAX_CODE_LENGTH) ||
    tooLong(r.costCurrency, MAX_CODE_LENGTH) ||
    tooLong(r.valuationMethod, MAX_CODE_LENGTH)
  )
    return `市場、幣別與估價方式最多 ${MAX_CODE_LENGTH} 個字`;
  return null;
}

/** The editor's message when it holds more rows than one save takes. */
export const tooManyPositionsMessage = (count: number) =>
  `一次最多儲存 ${MAX_POSITIONS} 筆持倉，目前有 ${count} 筆，請刪掉 ${count - MAX_POSITIONS} 筆。`;

/** An imported row past what the editor still has room for. */
export const OVER_POSITIONS_LIMIT = `超過一次可儲存的 ${MAX_POSITIONS} 筆`;
