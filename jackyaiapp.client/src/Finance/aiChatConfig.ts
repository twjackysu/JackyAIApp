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
  errorMessages: {
    refused: '這個問題我無法回答，請換個問法，或詢問台股相關的問題。',
  },
};
