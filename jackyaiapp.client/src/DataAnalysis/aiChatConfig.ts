import DashboardCustomizeIcon from '@mui/icons-material/DashboardCustomizeOutlined';
import ShowChartIcon from '@mui/icons-material/ShowChart';
import TableChartIcon from '@mui/icons-material/TableChartOutlined';
import TravelExploreIcon from '@mui/icons-material/TravelExplore';

import ChartCardView from './assistant/ChartCardView';

import type { AiChatConfig } from '@/components/aiChat/config';

/**
 * Questions about the data source on the page: what it holds, a summary, a chart, a widget.
 * Shown in turn as the one-line box's placeholder and as suggestions under the greeting.
 */
export const DATA_ANALYSIS_EXAMPLE_QUESTIONS = [
  '這份資料有哪些欄位？',
  '上個月各地區營收是多少？',
  '畫出近 12 個月的營收趨勢',
  '哪個產品類別成長最快？',
  '把這張圖存成 dashboard widget',
] as const;

/**
 * The Data Analysis assistant (docs/ai-native/design/05 §2): the data source on the page as its
 * tools — query, summarize, chart, save to a dashboard. Its charts are this page's own renderers
 * (`assistant/ChartCardView`), so a chart in the answer looks like one below it.
 */
export const DATA_ANALYSIS_AI_CHAT: AiChatConfig = {
  profile: 'data-analysis',
  greetingTitle: '想從資料裡知道什麼？',
  greeting:
    '我是資料分析 AI 助手，可以直接查詢、彙總畫面上選的資料源，畫成圖表，還能把結果存成 dashboard widget。用平常說話的方式問就好。',
  examples: DATA_ANALYSIS_EXAMPLE_QUESTIONS,
  suggestions: [
    { text: '這份資料有哪些欄位？', icon: TravelExploreIcon },
    { text: '上個月各地區營收是多少？', icon: TableChartIcon },
    { text: '畫出近 12 個月的營收趨勢', icon: ShowChartIcon },
    { text: '把這張圖存成 dashboard widget', icon: DashboardCustomizeIcon },
  ],
  connectedLabel: '資料源已連線',
  signedOutPlaceholder: '登入後即可問 AI 你的資料',
  inputPlaceholder: {
    narrow: '問問你的資料…',
    wide: '問問你的資料，例如：去年各月營收趨勢，畫成折線圖（Enter 送出，Shift+Enter 換行）',
  },
  cardRenderers: { chart: ChartCardView },
};
