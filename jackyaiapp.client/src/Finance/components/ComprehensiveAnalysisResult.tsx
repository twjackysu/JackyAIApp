import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import WarningIcon from '@mui/icons-material/Warning';
import Accordion from '@mui/material/Accordion';
import AccordionDetails from '@mui/material/AccordionDetails';
import AccordionSummary from '@mui/material/AccordionSummary';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Chip from '@mui/material/Chip';
import Divider from '@mui/material/Divider';
import Grid from '@mui/material/Grid';
import LinearProgress from '@mui/material/LinearProgress';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Typography from '@mui/material/Typography';

import type { StockAnalysisResultData, CategoryScore, IndicatorResult, IndicatorCategory } from '@/apis/financeApis/types';

import {
  getDirectionColor, getDirectionLabel, getDirectionChipColor, getDirectionEmoji,
  getRiskLabel, getRiskChipColor, getRiskColor,
  getCategoryLabel, getCategoryEmoji, getScoreColor,
} from '../utils/financeHelpers';
import ScoreGauge from './ScoreGauge';

interface Props { data: StockAnalysisResultData; }

const OverallScoreSection = ({ data }: Props) => {
  if (!data.scoring) return null;
  const { scoring } = data;
  return (
    <Card sx={{ mb: 3, border: '2px solid', borderColor: 'primary.main' }}>
      <CardContent>
        <Grid container spacing={3} alignItems="center">
          <Grid item xs={12} md={3} sx={{ display: 'flex', justifyContent: 'center' }}>
            <ScoreGauge score={scoring.overallScore} label="綜合評分" />
          </Grid>
          <Grid item xs={12} md={9}>
            <Stack spacing={2}>
              <Stack direction="row" spacing={2} alignItems="center">
                <Typography variant="h5" fontWeight="bold">{data.companyName} ({data.stockCode})</Typography>
                <Chip label={`${getDirectionEmoji(scoring.overallDirection)} ${getDirectionLabel(scoring.overallDirection)}`} color={getDirectionChipColor(scoring.overallDirection)} variant="filled" sx={{ fontWeight: 'bold', fontSize: '0.9rem' }} />
              </Stack>
              {data.latestClose && <Typography variant="h6" color="text.secondary">最新收盤價: NT${data.latestClose.toFixed(2)}</Typography>}
              <Paper sx={{ p: 2, bgcolor: getDirectionColor(scoring.overallDirection, 0.08) }}>
                <Typography variant="body1">{scoring.recommendation}</Typography>
              </Paper>
              <Typography variant="caption" color="text.secondary">
                資料範圍: {data.dataRange} | 分析時間: {new Date(data.generatedAt).toLocaleString('zh-TW')}
              </Typography>
            </Stack>
          </Grid>
        </Grid>
      </CardContent>
    </Card>
  );
};

const CategoryScoreCard = ({ cs }: { cs: CategoryScore }) => {
  const color = getScoreColor(cs.score);
  return (
    <Card sx={{ height: '100%' }}>
      <CardContent>
        <Stack spacing={1.5}>
          <Stack direction="row" justifyContent="space-between" alignItems="center">
            <Typography variant="h6" fontWeight="bold">{getCategoryEmoji(cs.category)} {getCategoryLabel(cs.category)}</Typography>
            <Chip label={getDirectionLabel(cs.direction)} color={getDirectionChipColor(cs.direction)} size="small" />
          </Stack>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 2 }}>
            <Typography variant="h4" fontWeight="bold" sx={{ color }}>{Math.round(cs.score)}</Typography>
            <Box sx={{ flexGrow: 1 }}>
              <LinearProgress variant="determinate" value={cs.score} sx={{ height: 10, borderRadius: 5, '& .MuiLinearProgress-bar': { bgcolor: color } }} />
            </Box>
          </Box>
          <Typography variant="body2" color="text.secondary">{cs.summary}</Typography>
          <Stack direction="row" justifyContent="space-between">
            <Typography variant="caption" color="text.secondary">權重: {(cs.weight * 100).toFixed(0)}%</Typography>
            <Typography variant="caption" color="text.secondary">加權分數: {cs.weightedScore.toFixed(1)}</Typography>
          </Stack>
        </Stack>
      </CardContent>
    </Card>
  );
};

const RiskSection = ({ data }: Props) => {
  if (!data.risk) return null;
  const { risk } = data;
  return (
    <Card sx={{ mb: 3 }}>
      <CardContent>
        <Stack direction="row" spacing={2} alignItems="center" sx={{ mb: 2 }}>
          <WarningIcon sx={{ color: getRiskColor(risk.level) }} />
          <Typography variant="h6" fontWeight="bold">風險評估</Typography>
          <Chip label={getRiskLabel(risk.level)} color={getRiskChipColor(risk.level)} variant="filled" />
          <Typography variant="body2" color="text.secondary">分歧指數: {risk.divergenceScore.toFixed(1)}</Typography>
        </Stack>
        {risk.factors.length > 0 ? (
          <Stack spacing={1}>
            {risk.factors.map((f, i) => <Alert key={i} severity="warning" variant="outlined" sx={{ py: 0 }}>{f}</Alert>)}
          </Stack>
        ) : (
          <Alert severity="success" variant="outlined" sx={{ py: 0 }}>未檢測到明顯風險因素</Alert>
        )}
      </CardContent>
    </Card>
  );
};

const IndicatorTable = ({ indicators, category }: { indicators: IndicatorResult[]; category: IndicatorCategory }) => {
  const filtered = indicators.filter(i => i.category === category);
  if (filtered.length === 0) return null;
  return (
    <Accordion defaultExpanded={category === 'Technical'}>
      <AccordionSummary expandIcon={<ExpandMoreIcon />}>
        <Typography variant="h6" fontWeight="bold">{getCategoryEmoji(category)} {getCategoryLabel(category)}指標明細 ({filtered.length})</Typography>
      </AccordionSummary>
      <AccordionDetails>
        <TableContainer>
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell>指標</TableCell>
                <TableCell align="center">數值</TableCell>
                <TableCell align="center">方向</TableCell>
                <TableCell align="center">分數</TableCell>
                <TableCell>訊號</TableCell>
                <TableCell>說明</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {filtered.map(ind => (
                <TableRow key={ind.name} hover>
                  <TableCell><Typography variant="body2" fontWeight="bold">{ind.name}</Typography></TableCell>
                  <TableCell align="center">{ind.value.toFixed(2)}</TableCell>
                  <TableCell align="center"><Chip label={getDirectionLabel(ind.direction)} color={getDirectionChipColor(ind.direction)} size="small" variant="outlined" /></TableCell>
                  <TableCell align="center"><Typography variant="body2" fontWeight="bold" sx={{ color: getScoreColor(ind.score) }}>{ind.score}</Typography></TableCell>
                  <TableCell>{ind.signal}</TableCell>
                  <TableCell><Typography variant="caption" color="text.secondary">{ind.reason}</Typography></TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      </AccordionDetails>
    </Accordion>
  );
};

export const ComprehensiveAnalysisResult = ({ data }: Props) => {
  const categories: IndicatorCategory[] = ['Technical', 'Chip', 'Fundamental'];
  const active = categories.filter(c => data.indicators.some(i => i.category === c));
  return (
    <Box>
      <OverallScoreSection data={data} />
      {data.scoring && data.scoring.categoryScores.length > 0 && (
        <Grid container spacing={3} sx={{ mb: 3 }}>
          {data.scoring.categoryScores.map(cs => (
            <Grid item xs={12} md={4} key={cs.category}><CategoryScoreCard cs={cs} /></Grid>
          ))}
        </Grid>
      )}
      <RiskSection data={data} />
      <Divider sx={{ my: 3 }} />
      <Typography variant="h6" fontWeight="bold" sx={{ mb: 2 }}>📋 指標明細</Typography>
      <Stack spacing={1}>
        {active.map(c => <IndicatorTable key={c} indicators={data.indicators} category={c} />)}
      </Stack>
    </Box>
  );
};

export default ComprehensiveAnalysisResult;
