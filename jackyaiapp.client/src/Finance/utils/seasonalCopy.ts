import type { DateRange, RangeProblem } from './seasonal';

// User-facing wording of the 過去 5 年同期間走勢 chart in one place, so PM can change a phrase
// (REQUIREMENTS A17) without touching component code. Nothing here may read as a forecast.

export const SEASONAL_TITLE = '過去 5 年同期間走勢';

/** What is plotted — "不含息" keeps it from being mistaken for the total-return index (A15). */
export const SEASONAL_SUBJECT = '加權指數（價格指數，不含息）';

export const SEASONAL_DISCLAIMER = '過去走勢僅供參考，不代表未來表現，不構成投資建議。';

export const SEASONAL_UNAVAILABLE = '資料暫時無法取得';

export const SEASONAL_REBASE_NOTE =
  '各年以區間內第一個交易日的收盤為 100，方便比較；滑過或點按圖面可看實際收盤價。';

export const SEASONAL_NO_TRADING_DAYS = '此區間沒有任何交易日資料，請調整起訖日期。';

export const seasonalAsOfText = (asOf: string | null) => `資料至 ${asOf ?? '—'}`;

export const seasonalOmittedYearsText = (years: number[]) =>
  `${years.join('、')} 年在此區間沒有交易日，未繪製。`;

export function seasonalRangeProblemText(problem: RangeProblem, { start, end }: DateRange): string {
  switch (problem) {
    case 'invalid-start':
      return `起始日期不存在：${start.month} 月沒有 ${start.day} 日，請重新選擇。`;
    case 'invalid-end':
      return `結束日期不存在：${end.month} 月沒有 ${end.day} 日，請重新選擇。`;
    case 'start-after-end':
      return '起始日不可晚於結束日，請重新選擇。';
  }
}
