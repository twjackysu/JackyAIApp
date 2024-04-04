import Box from '@mui/material/Box';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import TextField from '@mui/material/TextField';

function Dictionary() {
  return (
    <Box>
      <Stack>
        <TextField label="Search" variant="outlined" />
        <Typography variant="body1" gutterBottom>
          body
        </Typography>
      </Stack>
    </Box>
  );
}

export default Dictionary;
