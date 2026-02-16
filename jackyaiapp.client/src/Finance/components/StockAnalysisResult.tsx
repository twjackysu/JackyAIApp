import Avatar from '@mui/material/Avatar';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import CardHeader from '@mui/material/CardHeader';
import Chip from '@mui/material/Chip';
import Grid from '@mui/material/Grid';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';

import { StockTrendAnalysis } from '@/apis/financeApis/types';

import {
  getTrendColor,
  getTrendLabel,
  getRecommendationColor,
  getRecommendationLabel,
  getRecommendationChipColor,
  getConfidenceLabel,
} from '../utils/financeHelpers';
import TrendIcon from './TrendIcon';

interface StockAnalysisResultProps {
  data: StockTrendAnalysis;
}

interface TrendCardProps {
  title: string;
  trend: string;
  summary: string;
}

const TrendCard = ({ title, trend, summary }: TrendCardProps) => (
  <Card sx={{ height: '100%', bgcolor: getTrendColor(trend, 0.1) }}>
    <CardHeader
      avatar={
        <Avatar sx={{ bgcolor: getTrendColor(trend) }}>
          <TrendIcon trend={trend} />
        </Avatar>
      }
      title={title}
      subheader={getTrendLabel(trend)}
    />
    <CardContent>
      <Typography variant="body2">{summary}</Typography>
    </CardContent>
  </Card>
);

interface ChipListCardProps {
  title: string;
  items: string[];
  color: 'primary' | 'warning';
}

const ChipListCard = ({ title, items, color }: ChipListCardProps) => (
  <Card sx={{ height: '100%' }}>
    <CardHeader title={title} />
    <CardContent>
      <Stack spacing={1}>
        {items.map((item, index) => (
          <Chip key={index} label={item} variant="outlined" color={color} />
        ))}
      </Stack>
    </CardContent>
  </Card>
);

/**
 * Display stock trend analysis results
 */
export const StockAnalysisResult = ({ data }: StockAnalysisResultProps) => {
  return (
    <Paper
      sx={{
        p: 3,
        mb: 3,
        bgcolor: 'background.paper',
        border: '2px solid',
        borderColor: 'primary.main',
      }}
    >
      <Typography variant="h5" gutterBottom sx={{ fontWeight: 'bold', color: 'primary.main' }}>
        {data.companyName} ({data.stockCode}) - 趨勢分析
      </Typography>
      {data.currentPrice && (
        <Typography variant="h6" color="text.secondary" gutterBottom>
          當前股價: ${data.currentPrice}
        </Typography>
      )}

      <Grid container spacing={3} sx={{ mt: 1 }}>
        {/* Short Term */}
        <Grid item xs={12} md={4}>
          <TrendCard
            title="短期趨勢 (1-3個月)"
            trend={data.shortTermTrend}
            summary={data.shortTermSummary}
          />
        </Grid>

        {/* Medium Term */}
        <Grid item xs={12} md={4}>
          <TrendCard
            title="中期趨勢 (3-12個月)"
            trend={data.mediumTermTrend}
            summary={data.mediumTermSummary}
          />
        </Grid>

        {/* Long Term */}
        <Grid item xs={12} md={4}>
          <TrendCard
            title="長期趨勢 (1-3年)"
            trend={data.longTermTrend}
            summary={data.longTermSummary}
          />
        </Grid>

        {/* Key Factors */}
        <Grid item xs={12} md={6}>
          <ChipListCard title="關鍵因素" items={data.keyFactors} color="primary" />
        </Grid>

        {/* Risk Factors */}
        <Grid item xs={12} md={6}>
          <ChipListCard title="風險因素" items={data.riskFactors} color="warning" />
        </Grid>

        {/* Investment Recommendation */}
        <Grid item xs={12}>
          <Card sx={{ bgcolor: getRecommendationColor(data.recommendation, 0.1) }}>
            <CardHeader
              title="投資建議"
              action={
                <Chip
                  label={getRecommendationLabel(data.recommendation)}
                  color={getRecommendationChipColor(data.recommendation)}
                  variant="filled"
                  sx={{ fontWeight: 'bold' }}
                />
              }
            />
            <CardContent>
              <Stack direction="row" justifyContent="space-between" alignItems="center">
                <Typography variant="body1">
                  信心水平: <strong>{getConfidenceLabel(data.confidenceLevel)}</strong>
                </Typography>
                <Typography variant="caption" color="text.secondary">
                  更新時間: {new Date(data.lastUpdated).toLocaleString('zh-TW')}
                </Typography>
              </Stack>
            </CardContent>
          </Card>
        </Grid>
      </Grid>
    </Paper>
  );
};

export default StockAnalysisResult;
