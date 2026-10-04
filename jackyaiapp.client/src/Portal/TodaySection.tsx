import CheckCircleIcon from '@mui/icons-material/CheckCircle';
import LocalFireDepartmentIcon from '@mui/icons-material/LocalFireDepartment';
import PieChartIcon from '@mui/icons-material/PieChart';
import QuizIcon from '@mui/icons-material/Quiz';
import ReplayIcon from '@mui/icons-material/Replay';
import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import CardActionArea from '@mui/material/CardActionArea';
import Skeleton from '@mui/material/Skeleton';
import Typography from '@mui/material/Typography';
import { Link } from 'react-router-dom';

import type { ReactNode } from 'react';

import { useGetStatsQuery } from '@/apis/dailyChallengeApis';
import { useGetUpdateHistoryQuery } from '@/apis/portfolioApis';
import { useGetDueCountQuery } from '@/apis/reviewApis';

interface TodayItemProps {
  to: string;
  icon: ReactNode;
  color: string;
  title: string;
  caption: string;
}

function TodayItem({ to, icon, color, title, caption }: TodayItemProps) {
  return (
    <Card elevation={0} sx={{ border: '1px solid', borderColor: 'divider', borderRadius: 2 }}>
      <CardActionArea
        component={Link}
        to={to}
        sx={{ p: 2, display: 'flex', alignItems: 'center', gap: 1.5, justifyContent: 'flex-start' }}
      >
        <Box sx={{ color, display: 'flex', flexShrink: 0 }}>{icon}</Box>
        <Box sx={{ minWidth: 0 }}>
          <Typography variant="subtitle1" sx={{ fontWeight: 700, lineHeight: 1.3 }}>
            {title}
          </Typography>
          <Typography variant="caption" color="text.secondary">
            {caption}
          </Typography>
        </Box>
      </CardActionArea>
    </Card>
  );
}

function ItemSkeleton() {
  return <Skeleton variant="rounded" height={72} data-testid="today-skeleton" />;
}

/**
 * Logged-in "today" block. Every query is independent: its own skeleton while loading, and an
 * item whose request failed or has nothing to say (0 streak, 0 due, no holdings) is simply left
 * out. Only reuses existing cheap endpoints — no AI / [UsageLimit] call, and deliberately not
 * getPortfolioSummary / net-worth-history, which fan out to upstream price providers.
 */
function TodaySection() {
  const stats = useGetStatsQuery();
  const due = useGetDueCountQuery();
  const history = useGetUpdateHistoryQuery();

  const s = stats.data?.data;
  const dueCount = due.data?.data?.dueCount ?? 0;
  const hasHoldings = (history.data?.data ?? []).some((h) => h.positionCount > 0);

  const items: ReactNode[] = [];

  if (stats.isLoading) items.push(<ItemSkeleton key="stats" />);
  else if (s) {
    items.push(
      s.todayCompleted ? (
        <TodayItem
          key="daily"
          to="/english/daily"
          icon={<CheckCircleIcon />}
          color="#16A34A"
          title="今日挑戰已完成"
          caption={s.todayScore != null ? `得分 ${s.todayScore}` : '明天再來'}
        />
      ) : (
        <TodayItem
          key="daily"
          to="/english/daily"
          icon={<QuizIcon />}
          color="#2563EB"
          title="開始今日挑戰"
          caption="5 題，約 2 分鐘"
        />
      ),
    );
    if (s.currentStreak > 0) {
      items.push(
        <TodayItem
          key="streak"
          to="/english/stats"
          icon={<LocalFireDepartmentIcon />}
          color="#F97316"
          title={`連續 ${s.currentStreak} 天`}
          caption="查看學習統計"
        />,
      );
    }
  }

  if (due.isLoading) items.push(<ItemSkeleton key="due" />);
  else if (dueCount > 0) {
    items.push(
      <TodayItem
        key="due"
        to="/english/review"
        icon={<ReplayIcon />}
        color="#9333EA"
        title={`${dueCount} 個單字待複習`}
        caption="開始複習"
      />,
    );
  }

  if (history.isLoading) items.push(<ItemSkeleton key="portfolio" />);
  else if (hasHoldings) {
    items.push(
      <TodayItem
        key="portfolio"
        to="/portfolio"
        icon={<PieChartIcon />}
        color="#F59E0B"
        title="查看投資組合"
        caption="最新資產與損益"
      />,
    );
  }

  if (items.length === 0) return null;

  return (
    <Box component="section" aria-label="今日" sx={{ mb: { xs: 4, md: 5 } }}>
      <Typography
        variant="overline"
        component="h2"
        sx={{ display: 'block', mb: 1.5, letterSpacing: 2, color: 'text.secondary' }}
      >
        今日
      </Typography>
      <Box
        sx={{
          display: 'grid',
          gap: 1.5,
          gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, 1fr)' },
        }}
      >
        {items}
      </Box>
    </Box>
  );
}

export default TodaySection;
