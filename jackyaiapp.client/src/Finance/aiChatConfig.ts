import CandlestickChartIcon from '@mui/icons-material/CandlestickChart';
import PieChartOutlineIcon from '@mui/icons-material/PieChartOutlined';
import ReceiptLongIcon from '@mui/icons-material/ReceiptLong';
import ShowChartIcon from '@mui/icons-material/ShowChart';

import type { AiChatConfig } from '@/components/aiChat/config';

/**
 * Questions the assistant can answer, one per kind of data its stock tools reach (the backend's
 * system prompt, FinanceChatProfile, lists the same scope). Shown as the one-line box's
 * placeholder, in turn, and as suggestions under the greeting — so a reader sees there is more to
 * ask than a share price. Short enough to fit the box on a phone.
 */
export const FINANCE_EXAMPLE_QUESTIONS = [
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

/**
 * The tw-stock tools in words, for the chips and the header while the assistant works: the server
 * sends no display name for a hosted tool. The ones the assistant uses most; any other shows its own
 * name, as before.
 */
export const FINANCE_TOOL_LABELS: Readonly<Record<string, string>> = {
  get_stock_history: '個股歷史股價',
  get_otc_stock_history: '上櫃歷史股價',
  get_adjusted_price_history: '還原股價',
  get_realtime_quote: '即時報價',
  get_technical_indicators: '技術指標',
  get_stock_daily_trading: '個股日成交',
  get_stock_valuation_ratios: '本益比與殖利率',
  get_otc_valuation: '上櫃本益比與殖利率',
  get_stock_screener: '選股',
  get_stock_price_changes: '漲跌排行',
  get_top_20_volume_stocks: '成交量前 20 名',
  get_company_profile: '公司基本資料',
  get_company_income_statement: '損益表',
  get_company_balance_sheet: '資產負債表',
  get_company_cash_flow_statement: '現金流量表',
  get_company_profitability_analysis: '獲利能力分析',
  get_company_eps_statistics: 'EPS 統計',
  get_company_monthly_revenue: '月營收',
  get_company_dividend: '股利',
  get_dividend_rights_schedule: '除權息預告',
  get_company_major_news: '重大訊息',
  get_company_major_shareholders: '大股東',
  get_shareholding_distribution: '股權分散',
  get_industry_peers: '同業公司',
  get_twse_institutional_investors_by_stock: '個股三大法人',
  get_twse_institutional_investors_summary: '三大法人買賣超',
  get_institutional_general: '三大法人',
  get_otc_institutional: '上櫃三大法人',
  get_foreign_holdings: '外資持股',
  get_margin_balance: '融資融券',
  get_otc_margin_balance: '上櫃融資融券',
  get_market_disposal_stocks: '處置股',
  get_otc_disposal_stocks: '上櫃處置股',
  get_today_notice_stocks: '注意股',
  get_market_index_info: '大盤指數',
  get_taiex_index_history: '加權指數歷史',
  get_twse_news: '證交所新聞',
  get_etf_info: 'ETF 基本資料',
  get_etf_daily_nav: 'ETF 淨值與折溢價',
  get_etf_holdings: 'ETF 持股',
  get_futures_institutional: '期貨三大法人',
  get_large_traders_futures_oi: '期貨大額交易人',
  get_put_call_ratio: '選擇權 P/C 比',
  get_daily_futures_market_report: '期貨行情',
  get_business_cycle_indicators: '景氣燈號',
  get_exchange_rates: '匯率',
  get_taiwan_pmi: '台灣 PMI',
  get_market_holiday_schedule: '休市日',
};

/** Stock Intelligence's assistant: Taiwan market data through the tw-stock tools. */
export const FINANCE_AI_CHAT: AiChatConfig = {
  profile: 'finance',
  greetingTitle: '今天想研究哪一檔？',
  greeting:
    '我是台股 AI 助手，可以查個股走勢、財報與股利、三大法人籌碼、ETF、期貨選擇權和景氣指標，也能分析您附上的檔案或截圖。',
  examples: FINANCE_EXAMPLE_QUESTIONS,
  // One from each of the main kinds: trend, financials, futures, ETF.
  suggestions: [
    { text: '分析台積電最近的走勢', icon: ShowChartIcon },
    { text: '幫我做台積電的財報體檢', icon: ReceiptLongIcon },
    { text: '台指期籌碼偏多還是偏空？', icon: CandlestickChartIcon },
    { text: '0050 現在溢價多少？', icon: PieChartOutlineIcon },
  ],
  connectedLabel: '台股資料已連線',
  disclaimer: 'AI 回答可能有誤，數據請以官方資料為準，不構成投資建議。',
  signedOutPlaceholder: '登入後即可問 AI 助手台股問題',
  inputPlaceholder: {
    narrow: '問台股、財報、ETF…',
    wide: '問台股、財報、ETF、期貨…（Enter 送出，Shift+Enter 換行）',
  },
  // After a stock's prices, its chips and financials; after its financials, its peers.
  followUps: [
    {
      tools: ['get_stock_history', 'get_technical_indicators', 'get_realtime_quote'],
      texts: ['它的三大法人最近在買還是賣？', '幫我做它的財報體檢'],
    },
    {
      tools: ['get_company_income_statement', 'get_company_profitability_analysis'],
      texts: ['跟同業比起來如何？', '它的股利政策穩定嗎？'],
    },
  ],
  toolLabels: FINANCE_TOOL_LABELS,
  errorMessages: {
    refused: '這個問題我無法回答，請換個問法，或詢問台股相關的問題。',
  },
};
