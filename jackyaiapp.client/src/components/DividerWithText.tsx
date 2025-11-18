import { grey } from '@mui/material/colors';
import Divider from '@mui/material/Divider';
import Grid from '@mui/material/Grid';
import Typography from '@mui/material/Typography';

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
