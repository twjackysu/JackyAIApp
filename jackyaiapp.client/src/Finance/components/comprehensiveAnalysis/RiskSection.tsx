import WarningIcon from '@mui/icons-material/Warning';
import Alert from '@mui/material/Alert';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';

import { getRiskLabel, getRiskChipColor, getRiskColor } from '../../utils/financeHelpers';

import type { StockAnalysisResultData } from '@/apis/financeApis/types';

export const RiskSection = ({ data }: { data: StockAnalysisResultData }) => {
  if (!data.risk) return null;
  const { risk } = data;
  return (
    <Card sx={{ mb: 3 }}>
      <CardContent>
        <Stack
          direction="row"
          spacing={2}
          sx={{
            alignItems: 'center',
            mb: 2,
          }}
        >
          <WarningIcon sx={{ color: getRiskColor(risk.level) }} />
          <Typography
            variant="h6"
            sx={{
              fontWeight: 'bold',
            }}
          >
            風險評估
          </Typography>
          <Chip
            label={getRiskLabel(risk.level)}
            color={getRiskChipColor(risk.level)}
            variant="filled"
          />
          <Typography variant="body2" color="text.secondary">
            分歧指數: {risk.divergenceScore.toFixed(1)}
          </Typography>
        </Stack>
        {risk.factors.length > 0 ? (
          <Stack spacing={1}>
            {risk.factors.map((f, i) => (
              <Alert key={i} severity="warning" variant="outlined" sx={{ py: 0 }}>
                {f}
              </Alert>
            ))}
          </Stack>
        ) : (
          <Alert severity="success" variant="outlined" sx={{ py: 0 }}>
            未檢測到明顯風險因素
          </Alert>
        )}
      </CardContent>
    </Card>
  );
};
