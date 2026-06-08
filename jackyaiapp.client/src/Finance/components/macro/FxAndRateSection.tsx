import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Chip from '@mui/material/Chip';
import Divider from '@mui/material/Divider';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Typography from '@mui/material/Typography';

import type { MacroEconomyData } from '@/apis/financeApis/types';

export const FxAndRateSection = ({ data }: { data: MacroEconomyData }) => {
  return (
    <Card sx={{ height: '100%' }}>
      <CardContent>
        <Typography variant="subtitle2" color="text.secondary" gutterBottom>
          💱 匯率與利率
        </Typography>
        {data.exchangeRates.length > 0 && (
          <TableContainer>
            <Table size="small">
              <TableHead>
                <TableRow>
                  <TableCell>幣別</TableCell>
                  <TableCell align="right">買入</TableCell>
                  <TableCell align="right">賣出</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {data.exchangeRates.map((fx) => (
                  <TableRow key={fx.currency} hover>
                    <TableCell>
                      <Typography variant="body2">
                        {fx.displayName} ({fx.currency})
                      </Typography>
                    </TableCell>
                    <TableCell align="right">
                      <Typography variant="body2">{fx.buyRate?.toFixed(4) ?? '-'}</Typography>
                    </TableCell>
                    <TableCell align="right">
                      <Typography variant="body2">{fx.sellRate?.toFixed(4) ?? '-'}</Typography>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>
        )}
        {data.bankRate && (
          <>
            <Divider sx={{ my: 1.5 }} />
            <Typography
              variant="caption"
              color="text.secondary"
              sx={{
                fontWeight: 'bold',
              }}
            >
              銀行利率 ({data.bankRate.bankName})
            </Typography>
            <Stack spacing={0.3} sx={{ mt: 0.5 }}>
              <Stack
                direction="row"
                sx={{
                  justifyContent: 'space-between',
                }}
              >
                <Typography variant="body2">一年期定存(固定)</Typography>
                <Chip
                  label={`${data.bankRate.oneYearFixed?.toFixed(3) ?? '-'}%`}
                  size="small"
                  variant="outlined"
                />
              </Stack>
              <Stack
                direction="row"
                sx={{
                  justifyContent: 'space-between',
                }}
              >
                <Typography variant="body2">一年期定存(機動)</Typography>
                <Chip
                  label={`${data.bankRate.oneYearFloating?.toFixed(3) ?? '-'}%`}
                  size="small"
                  variant="outlined"
                />
              </Stack>
              {data.bankRate.baseLendingRate && (
                <Stack
                  direction="row"
                  sx={{
                    justifyContent: 'space-between',
                  }}
                >
                  <Typography variant="body2">基準利率</Typography>
                  <Chip
                    label={`${data.bankRate.baseLendingRate.toFixed(3)}%`}
                    size="small"
                    color="primary"
                    variant="outlined"
                  />
                </Stack>
              )}
            </Stack>
          </>
        )}
      </CardContent>
    </Card>
  );
};
