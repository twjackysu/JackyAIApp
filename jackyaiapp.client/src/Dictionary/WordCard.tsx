import Button from '@mui/material/Button';
import Stack from '@mui/material/Stack';
import Grid from '@mui/material/Grid';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import IconButton from '@mui/material/IconButton';
import Typography from '@mui/material/Typography';
import FavoriteBorder from '@mui/icons-material/FavoriteBorder';
import { useGetWordQuery } from '@/apis/dictionaryApis';

interface Props {
  word?: string | null;
}

function WordCard({ word }: Props) {
  const { data, isError } = useGetWordQuery(word!, { skip: !word });
  console.log(data?.Data);
  return (
    <Card>
      <Stack direction="row" justifyContent="space-between">
        <Typography variant="h2">{word}</Typography>
        <IconButton sx={{ m: 2, p: 2 }}>
          <FavoriteBorder />
        </IconButton>
      </Stack>
      <CardContent>
        {isError ? (
          <Typography>Something wrong</Typography>
        ) : (
          <Grid container>
            <Grid item xs={12}>
              <Typography variant="h6">{JSON.stringify(data?.Data)}</Typography>
            </Grid>
          </Grid>
        )}
      </CardContent>
    </Card>
  );
}
export default WordCard;
