import AccountBalanceWalletIcon from '@mui/icons-material/AccountBalanceWallet';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Grid from '@mui/material/Grid';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';

import { ChangeText } from './atoms';
import { formatNumber } from './format';

import type { MacroEconomyData } from '@/apis/financeApis/types';

export const MarginSection = ({ data }: { data: MacroEconomyData }) => {
  if (!data.margin) return null;
  const m = data.margin;

  return (
    <Card sx={{ height: '100%' }}>
      <CardContent>
        <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center', mb: 0.5 }}>
          <AccountBalanceWalletIcon fontSize="small" color="action" />
          <Typography variant="subtitle2" color="text.secondary">
            融資融券
          </Typography>
        </Stack>
        <Grid container spacing={2}>
          <Grid size={6}>
            <Typography variant="caption" color="text.secondary">
              融資
            </Typography>
            <Stack spacing={0.3}>
              <Typography variant="body2">買進: {formatNumber(m.marginBuyTotal)}</Typography>
              <Typography variant="body2">賣出: {formatNumber(m.marginSellTotal)}</Typography>
              <Stack
                direction="row"
                spacing={0.5}
                sx={{
                  alignItems: 'center',
                }}
              >
                <Typography variant="body2">增減:</Typography>
                {m.marginChange === null ? (
                  <Typography variant="body2">—</Typography>
                ) : (
                  <ChangeText value={m.marginChange} />
                )}
              </Stack>
              <Typography
                variant="body2"
                sx={{
                  fontWeight: 'bold',
                }}
              >
                餘額: {formatNumber(m.marginBalanceTotal)}
              </Typography>
            </Stack>
          </Grid>
          <Grid size={6}>
            <Typography variant="caption" color="text.secondary">
              融券
            </Typography>
            <Stack spacing={0.3}>
              <Typography variant="body2">賣出: {formatNumber(m.shortSellTotal)}</Typography>
              <Typography variant="body2">買進: {formatNumber(m.shortBuyTotal)}</Typography>
              <Stack
                direction="row"
                spacing={0.5}
                sx={{
                  alignItems: 'center',
                }}
              >
                <Typography variant="body2">增減:</Typography>
                {m.shortChange === null ? (
                  <Typography variant="body2">—</Typography>
                ) : (
                  <ChangeText value={m.shortChange} />
                )}
              </Stack>
              <Typography
                variant="body2"
                sx={{
                  fontWeight: 'bold',
                }}
              >
                餘額: {formatNumber(m.shortBalanceTotal)}
              </Typography>
            </Stack>
          </Grid>
        </Grid>
      </CardContent>
    </Card>
  );
};
