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
  '幫我找出這份資料最值得注意的三件事',
  '上個月各地區營收是多少？',
  '畫出近 12 個月的營收趨勢',
  '哪個產品類別成長最快？',
  '比較今年和去年各月的營收',
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
    '我是資料分析 AI 助手，會自己查詢、彙總畫面上選的資料，畫成圖表並說明重點；說一聲「存到 dashboard」我就幫你存（會先問你）。用平常說話的方式問就好。',
  examples: DATA_ANALYSIS_EXAMPLE_QUESTIONS,
  suggestions: [
    { text: '幫我找出這份資料最值得注意的三件事', icon: TravelExploreIcon },
    { text: '上個月各地區營收是多少？', icon: TableChartIcon },
    { text: '畫出近 12 個月的營收趨勢', icon: ShowChartIcon },
    // One sentence, a whole dashboard: the charts asked together, saved together on one confirmation (D-090).
    { text: '幫我做一個業績總覽 dashboard（3 到 4 張圖），存完打開', icon: DashboardCustomizeIcon },
  ],
  connectedLabel: '資料源已連線',
  disclaimer: 'AI 回答可能有誤，重要的數字請以明細資料核對。',
  signedOutPlaceholder: '登入後即可問 AI 你的資料',
  inputPlaceholder: {
    narrow: '問問你的資料…',
    wide: '問問你的資料，例如：去年各月營收趨勢，畫成折線圖（Enter 送出，Shift+Enter 換行）',
  },
  cardRenderers: { chart: ChartCardView },
  // After a chart, the next things one does with it; after a look at the columns, a first question.
  followUps: [
    { tools: ['save_to_dashboard'], texts: ['再畫一張不同角度的圖，存到同一個 dashboard'] },
    { tools: ['ask_data'], texts: ['這個結果有什麼值得注意的？', '存到 dashboard'] },
    { tools: ['describe_data'], texts: ['幫我找出這份資料最值得注意的三件事'] },
    { texts: ['畫出近 12 個月的趨勢'] },
  ],
};

/**
 * The assistant for what is on screen. An uploaded file (a session) has no source a dashboard widget could query
 * again, so its charts cannot be saved: nothing offers to save one there (D-094).
 */
export function dataAnalysisAiChatFor(uploadedFile: boolean): AiChatConfig {
  if (!uploadedFile) return DATA_ANALYSIS_AI_CHAT;
  return {
    ...DATA_ANALYSIS_AI_CHAT,
    greeting:
      '我是資料分析 AI 助手，會自己查詢、彙總你上傳的檔案，畫成圖表並說明重點。用平常說話的方式問就好。',
    suggestions: DATA_ANALYSIS_AI_CHAT.suggestions.filter((s) => !s.text.includes('dashboard')),
    followUps: (DATA_ANALYSIS_AI_CHAT.followUps ?? [])
      .filter((rule) => !rule.tools?.includes('save_to_dashboard'))
      .map((rule) => ({ ...rule, texts: rule.texts.filter((t) => !t.includes('dashboard')) }))
      .filter((rule) => rule.texts.length > 0),
  };
}
