import Box from '@mui/material/Box';
import LinearProgress from '@mui/material/LinearProgress';
import Typography from '@mui/material/Typography';

function AILoading() {
  return (
    <Box sx={{ width: '100%' }}>
      <LinearProgress />
      <Typography variant="h6">Generating AI response, please wait...</Typography>
    </Box>
  );
}

export default AILoading;
