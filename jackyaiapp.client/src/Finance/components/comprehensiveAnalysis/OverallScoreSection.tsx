import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Chip from '@mui/material/Chip';
import Grid from '@mui/material/Grid';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';

import {
  getDirectionColor,
  getDirectionLabel,
  getDirectionChipColor,
} from '../../utils/financeHelpers';
import { DirectionIcon } from '../financeIcons';
import ScoreGauge from '../ScoreGauge';

import type { StockAnalysisResultData } from '@/apis/financeApis/types';

export const OverallScoreSection = ({ data }: { data: StockAnalysisResultData }) => {
  const { scoring } = data;
  return (
    <Card sx={{ mb: 3, border: '2px solid', borderColor: 'primary.main' }}>
      <CardContent>
        <Grid
          container
          spacing={3}
          sx={{
            alignItems: 'center',
          }}
        >
          {/* Score gauge — only when scoring is enabled */}
          {scoring && (
            <Grid
              sx={{ display: 'flex', justifyContent: 'center' }}
              size={{
                xs: 12,
                md: 3,
              }}
            >
              <ScoreGauge score={scoring.overallScore} label="綜合評分" />
            </Grid>
          )}
          <Grid
            size={{
              xs: 12,
              md: scoring ? 9 : 12,
            }}
          >
            <Stack spacing={2}>
              <Stack
                direction="row"
                spacing={2}
                sx={{
                  alignItems: 'center',
                  flexWrap: 'wrap',
                }}
              >
                <Chip
                  label={data.market === 'US' ? 'US' : 'TW'}
                  size="small"
                  variant="outlined"
                  sx={{ fontWeight: 'bold' }}
                />
                <Typography
                  variant="h5"
                  sx={{
                    fontWeight: 'bold',
                  }}
                >
                  {data.companyName} ({data.stockCode})
                </Typography>
                {scoring && (
                  <Chip
                    icon={<DirectionIcon direction={scoring.overallDirection} />}
                    label={getDirectionLabel(scoring.overallDirection)}
                    color={getDirectionChipColor(scoring.overallDirection)}
                    variant="filled"
                    sx={{ fontWeight: 'bold', fontSize: '0.9rem' }}
                  />
                )}
              </Stack>
              {data.latestClose && (
                <Typography variant="h6" color="text.secondary">
                  最新收盤價: {data.market === 'US' ? '$' : 'NT$'}
                  {data.latestClose.toFixed(2)}
                </Typography>
              )}
              {scoring && (
                <Paper sx={{ p: 2, bgcolor: getDirectionColor(scoring.overallDirection, 0.08) }}>
                  <Typography variant="body1">{scoring.recommendation}</Typography>
                </Paper>
              )}
              <Typography variant="caption" color="text.secondary">
                資料範圍: {data.dataRange} | 分析時間:{' '}
                {new Date(data.generatedAt).toLocaleString('zh-TW')}
                {' | '}指標數量: {data.indicators.length}
              </Typography>
            </Stack>
          </Grid>
        </Grid>
      </CardContent>
    </Card>
  );
};
