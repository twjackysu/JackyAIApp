import Grid from '@mui/material/Grid';
import Divider from '@mui/material/Divider';
import Typography from '@mui/material/Typography';
import { grey } from '@mui/material/colors';

interface Props {
  text: string;
}

function DividerWithText(props: Props) {
  const { text } = props;
  return (
    <Grid container>
      <Grid item xs="auto">
        <Typography variant="subtitle2" color={grey[700]}>
          {text}
        </Typography>
      </Grid>
      <Grid item xs>
        <Divider sx={{ m: 1, width: '100%' }} />
      </Grid>
    </Grid>
  );
}

export default DividerWithText;
