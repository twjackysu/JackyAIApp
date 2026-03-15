import LeaderboardIcon from '@mui/icons-material/Leaderboard';
import SummarizeIcon from '@mui/icons-material/Summarize';
import Box from '@mui/material/Box';
import Tab from '@mui/material/Tab';
import Tabs from '@mui/material/Tabs';
import { useState } from 'react';

import { useGetWeeklyReportQuery, useGetLeaderboardQuery } from '@/apis/statsApis';

import Leaderboard from './components/Leaderboard';
import WeeklyReport from './components/WeeklyReport';

function Stats() {
  const [tab, setTab] = useState(0);
  const { data: reportData, isLoading: isReportLoading } = useGetWeeklyReportQuery();
  const { data: leaderboardData, isLoading: isLeaderboardLoading } = useGetLeaderboardQuery();

  return (
    <Box sx={{ maxWidth: 700, mx: 'auto', mt: 2 }}>
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
