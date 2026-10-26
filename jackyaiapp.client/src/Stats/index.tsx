import LeaderboardIcon from '@mui/icons-material/Leaderboard';
import SummarizeIcon from '@mui/icons-material/Summarize';
import TrendingDownIcon from '@mui/icons-material/TrendingDown';
import Box from '@mui/material/Box';
import Tab from '@mui/material/Tab';
import Tabs from '@mui/material/Tabs';
import { useState } from 'react';

import ActivityHeatmap from './components/ActivityHeatmap';
import Leaderboard from './components/Leaderboard';
import WeeklyReport from './components/WeeklyReport';

import { useGetWeeklyReportQuery, useGetLeaderboardQuery } from '@/apis/statsApis';
import { FIND_MY_WEAK_SPOTS } from '@/components/StudioAssistant/askStudio';
import AskStudioCard from '@/components/StudioAssistant/AskStudioCard';

function Stats() {
  const [tab, setTab] = useState(0);
  const { data: reportData, isLoading: isReportLoading } = useGetWeeklyReportQuery();
  const { data: leaderboardData, isLoading: isLeaderboardLoading } = useGetLeaderboardQuery();

  return (
    <Box sx={{ maxWidth: 700, mx: 'auto', mt: 2 }}>
      {/* The page counts the results; Studio AI reads them (weak_spots) and says what to work on next. */}
      <AskStudioCard
        icon={<TrendingDownIcon />}
        title="AI 弱點分析"
        description="看你最近 30 天的測驗，找出最常錯的字與最弱的題型，再考你那幾個字"
        ask={FIND_MY_WEAK_SPOTS}
      />
      <Box sx={{ mb: 3 }}>
        <ActivityHeatmap />
      </Box>

      <Tabs value={tab} onChange={(_, v) => setTab(v)} centered sx={{ mb: 3 }}>
        <Tab icon={<SummarizeIcon />} label="Weekly Report" />
        <Tab icon={<LeaderboardIcon />} label="Leaderboard" />
      </Tabs>

      {tab === 0 && <WeeklyReport data={reportData?.data} isLoading={isReportLoading} />}
      {tab === 1 && <Leaderboard data={leaderboardData?.data} isLoading={isLeaderboardLoading} />}
    </Box>
  );
}

export default Stats;
