import AccountBalanceIcon from '@mui/icons-material/AccountBalance';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Grid from '@mui/material/Grid';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';

import { FxAndRateSection } from './macro/FxAndRateSection';
import { MacroSkeleton } from './macro/MacroSkeleton';
import { MarginSection } from './macro/MarginSection';
import { MarketIndexSection } from './macro/MarketIndexSection';
import { SectorSection } from './macro/SectorSection';

import { useGetMacroEconomyQuery } from '@/apis/financeApis';

export const MacroEconomyOverview = () => {
  const { data: apiResponse, isLoading, isError } = useGetMacroEconomyQuery();
  const data = apiResponse?.data;

  if (isLoading) return <MacroSkeleton />;
  if (isError)
    return (
      <Alert severity="warning" sx={{ mb: 2 }}>
        總體經濟資料載入失敗
      </Alert>
    );
  if (!data) return null;

  return (
    <Box sx={{ mb: 3 }}>
      <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center', mb: 1 }}>
        <AccountBalanceIcon fontSize="small" />
        <Typography variant="h6" sx={{ fontWeight: 'bold' }}>
          總體經濟概覽
        </Typography>
      </Stack>
      <Grid container spacing={2}>
        <Grid
          size={{
            xs: 12,
            md: 6,
          }}
        >
          <MarketIndexSection data={data.marketIndex} />
        </Grid>
        <Grid
          size={{
            xs: 12,
            md: 6,
          }}
        >
          <SectorSection sectors={data.sectorIndices} />
        </Grid>
        <Grid
          size={{
            xs: 12,
            md: 6,
          }}
        >
          <MarginSection data={data} />
        </Grid>
        <Grid
          size={{
            xs: 12,
            md: 6,
          }}
        >
          <FxAndRateSection data={data} />
        </Grid>
      </Grid>
    </Box>
  );
};

export default MacroEconomyOverview;
