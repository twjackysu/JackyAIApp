/**
 * Questions the assistant can answer, one per kind of data its stock tools reach (the backend's
 * system prompt lists the same scope). Shown as the one-line box's placeholder, in turn, and as
 * suggestions under the greeting — so a reader sees there is more to ask than a share price.
 * Short enough to fit the box on a phone.
 */
export const EXAMPLE_QUESTIONS = [
  '分析台積電最近的走勢',
  '外資最近在買什麼股票？',
  '今天有什麼重大消息？',
  '推薦一些高殖利率股票',
  '下個月有哪些公司除權息？',
  '台指期籌碼偏多還是偏空？',
  '幫我做台積電的財報體檢',
  '2317 買進前有被列處置嗎？',
  '比較台積電近四季毛利率',
  '環球晶的大戶持股比例？',
  '0050 現在溢價多少？',
  '00878 前十大持股是哪些？',
  '現在景氣燈號是什麼顏色？',
  '新台幣最近一個月升還是貶？',
] as const;

/** Under the greeting: one from each of the main kinds, the same every time. */
export const GREETING_SUGGESTIONS = [
  '分析台積電最近的走勢',
  '幫我做台積電的財報體檢',
  '台指期籌碼偏多還是偏空？',
  '0050 現在溢價多少？',
] as const;
