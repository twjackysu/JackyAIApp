import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Divider from '@mui/material/Divider';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';

import { ChangeText } from './atoms';
import { formatBillion, formatNumber, formatROCDate, formatVolumeBillion } from './format';

import type { MarketIndexDay } from '@/apis/financeApis/types';

export const MarketIndexSection = ({ data }: { data: MarketIndexDay[] }) => {
  if (data.length === 0) return null;
  const latest = data[data.length - 1];
  const changePercent =
    latest.taiex > 0 ? (latest.change / (latest.taiex - latest.change)) * 100 : 0;

  return (
    <Card sx={{ height: '100%' }}>
      <CardContent>
        <Typography variant="subtitle2" color="text.secondary" gutterBottom>
          📈 加權指數 (TAIEX)
        </Typography>
        <Typography
          variant="h4"
          sx={{
            fontWeight: 'bold',
          }}
        >
          {formatNumber(Math.round(latest.taiex))}
        </Typography>
        <Stack
          direction="row"
          spacing={1}
          sx={{
            alignItems: 'center',
            mt: 0.5,
          }}
        >
          <ChangeText value={latest.change} />
          <ChangeText value={changePercent} suffix="%" />
        </Stack>
        <Divider sx={{ my: 1.5 }} />
        <Stack spacing={0.5}>
          <Typography variant="caption" color="text.secondary">
            成交量: {formatVolumeBillion(latest.tradeVolume)}股
          </Typography>
          <Typography variant="caption" color="text.secondary">
            成交值: NT${formatBillion(latest.tradeValue)}
          </Typography>
          <Typography variant="caption" color="text.secondary">
            成交筆數: {formatNumber(latest.transaction)}
          </Typography>
        </Stack>
        {data.length > 1 && (
          <>
            <Divider sx={{ my: 1.5 }} />
            <Typography
              variant="caption"
              color="text.secondary"
              sx={{
                fontWeight: 'bold',
              }}
            >
              近期走勢
            </Typography>
            <Stack spacing={0.3} sx={{ mt: 0.5 }}>
              {data.slice(-5).map((d) => (
                <Stack
                  key={d.date}
                  direction="row"
                  sx={{
                    justifyContent: 'space-between',
                    alignItems: 'center',
                  }}
                >
                  <Typography variant="caption" color="text.secondary">
                    {formatROCDate(d.date)}
                  </Typography>
                  <Typography variant="caption">{formatNumber(Math.round(d.taiex))}</Typography>
                  <ChangeText value={d.change} />
                </Stack>
              ))}
            </Stack>
          </>
        )}
      </CardContent>
    </Card>
  );
};
