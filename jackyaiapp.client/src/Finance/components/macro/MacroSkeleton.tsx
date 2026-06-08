import Grid from '@mui/material/Grid';
import Paper from '@mui/material/Paper';
import Skeleton from '@mui/material/Skeleton';

export const MacroSkeleton = () => (
  <Grid container spacing={2}>
    {[1, 2, 3, 4].map((i) => (
      <Grid
        key={i}
        size={{
          xs: 12,
          sm: 6,
          md: 3,
        }}
      >
        <Paper sx={{ p: 2 }}>
          <Skeleton
            variant="text"
            sx={{
              width: '60%',
            }}
          />
          <Skeleton
            variant="rectangular"
            sx={{
              height: 120,
              mt: 1,
            }}
          />
        </Paper>
      </Grid>
    ))}
  </Grid>
);
